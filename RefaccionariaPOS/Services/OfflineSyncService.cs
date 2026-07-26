using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RefaccionariaPOS.Services
{
    /// <summary>
    /// Recuerda si la ultima operacion contra Neon fallo por red para que las vistas
    /// no se congelen reintentando la conexion en cada accion. Tras una falla se
    /// trabaja offline durante un lapso corto y despues se vuelve a intentar.
    /// </summary>
    public static class EstadoConexion
    {
        private static readonly TimeSpan EsperaTrasFalla = TimeSpan.FromSeconds(30);
        private static long ultimaFallaTicksUtc;

        public static bool HayFallaReciente =>
            DateTime.UtcNow.Ticks - Interlocked.Read(ref ultimaFallaTicksUtc) < EsperaTrasFalla.Ticks;

        public static bool DebeIntentarOnline => !HayFallaReciente;

        public static void MarcarFalla()
        {
            Interlocked.Exchange(ref ultimaFallaTicksUtc, DateTime.UtcNow.Ticks);
        }

        public static void MarcarExito()
        {
            Interlocked.Exchange(ref ultimaFallaTicksUtc, 0);
        }

        /// <summary>
        /// Distingue una falla de red (candidata a modo offline) de un error de negocio
        /// o de SQL, donde el servidor si respondio y no debe activarse el modo offline.
        /// </summary>
        public static bool EsErrorDeConexion(Exception ex)
        {
            for (Exception? actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual is PostgresException)
                {
                    return false;
                }

                if (actual is NpgsqlException or TimeoutException or SocketException or IOException)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Sube a Neon las ventas registradas sin conexion y refresca el catalogo local
    /// de productos y usuarios que usa el modo offline.
    /// </summary>
    public static class OfflineSyncService
    {
        private static readonly SemaphoreSlim SyncLock = new(1, 1);

        private const string QueryInsertarVenta = @"
            INSERT INTO ventas (usuario_id, cliente_id, total, fecha_venta, estado, metodo_pago, efectivo_recibido, cambio_entregado)
            VALUES (@usuarioId, @clienteId, @total, @fecha, 'Completada', @metodoPago, @efectivoRecibido, @cambioEntregado)
            RETURNING id;";

        private const string QueryInsertarDetalle = @"
            INSERT INTO detalles_venta (venta_id, producto_id, cantidad, precio_unitario, subtotal, descripcion_manual, tipo_articulo)
            VALUES (@ventaId, @productoId, @cantidad, @precioUnitario, @subtotal, @descripcionManual, @tipoArticulo);";

        private const string QueryDescontarStock = @"
            UPDATE productos
            SET stock_actual = GREATEST(stock_actual - @cantidad, 0)
            WHERE codigo_barras = @codigo
            RETURNING nombre, stock_actual, stock_minimo;";

        /// <summary>
        /// Sube las ventas pendientes en orden cronologico. Devuelve cuantas se
        /// sincronizaron. Lanza excepcion si la conexion falla a media carga
        /// (las ventas ya subidas quedan eliminadas de la cola, no se duplican).
        /// </summary>
        public static async Task<int> SincronizarVentasPendientesAsync()
        {
            if (!await SyncLock.WaitAsync(0))
            {
                return 0;
            }

            try
            {
                List<VentaOffline> pendientes = OfflineStore.CargarVentasPendientes();
                if (pendientes.Count == 0)
                {
                    return 0;
                }

                DatabaseConnection db = new DatabaseConnection();
                await using NpgsqlConnection conexion = db.GetConnection();
                await conexion.OpenAsync();

                int sincronizadas = 0;
                foreach (VentaOffline venta in pendientes.OrderBy(v => v.Fecha))
                {
                    int ventaId = await GuardarVentaAsync(conexion, venta);
                    OfflineStore.EliminarVentaPendiente(venta.IdLocal);
                    sincronizadas++;
                    TelegramNotificationService.NotificarVentaEnSegundoPlano(ventaId);
                }

                EstadoConexion.MarcarExito();
                return sincronizadas;
            }
            finally
            {
                SyncLock.Release();
            }
        }

        public static async Task RefrescarCachesAsync()
        {
            DatabaseConnection db = new DatabaseConnection();
            await using NpgsqlConnection conexion = db.GetConnection();
            await conexion.OpenAsync();

            List<ProductoOffline> productos = new();
            const string queryProductos = @"
                SELECT id, codigo_barras, nombre, descripcion, categoria,
                       COALESCE(tipo_venta, 'Unidad') AS tipo_venta, precio_venta, stock_actual
                FROM productos;";
            await using (NpgsqlCommand cmd = new NpgsqlCommand(queryProductos, conexion))
            await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    productos.Add(new ProductoOffline
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        CodigoBarras = reader["codigo_barras"].ToString() ?? string.Empty,
                        Nombre = reader["nombre"].ToString() ?? string.Empty,
                        Descripcion = reader["descripcion"].ToString() ?? string.Empty,
                        Categoria = reader["categoria"].ToString() ?? "General",
                        TipoVenta = reader["tipo_venta"].ToString() ?? "Unidad",
                        PrecioVenta = Convert.ToDecimal(reader["precio_venta"]),
                        Stock = Convert.ToDecimal(reader["stock_actual"])
                    });
                }
            }

            List<UsuarioOffline> usuarios = new();
            const string queryUsuarios = "SELECT id, username, rol, password_hash FROM usuarios;";
            await using (NpgsqlCommand cmd = new NpgsqlCommand(queryUsuarios, conexion))
            await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    usuarios.Add(new UsuarioOffline
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Username = reader["username"].ToString() ?? string.Empty,
                        Rol = reader["rol"].ToString() ?? "Vendedor",
                        PasswordHash = reader["password_hash"].ToString() ?? string.Empty
                    });
                }
            }

            OfflineStore.GuardarProductos(productos);
            OfflineStore.GuardarUsuarios(usuarios);
            EstadoConexion.MarcarExito();
        }

        private static async Task<int> GuardarVentaAsync(NpgsqlConnection conexion, VentaOffline venta)
        {
            List<ProductoBajoStock> productosBajoStock = new();
            await using NpgsqlTransaction transaccion = await conexion.BeginTransactionAsync();

            int ventaId;
            await using (NpgsqlCommand cmdVenta = new NpgsqlCommand(QueryInsertarVenta, conexion, transaccion))
            {
                cmdVenta.Parameters.AddWithValue("@usuarioId", venta.UsuarioId == 0 ? DBNull.Value : (object)venta.UsuarioId);
                cmdVenta.Parameters.AddWithValue("@clienteId", venta.ClienteId.HasValue ? (object)venta.ClienteId.Value : DBNull.Value);
                cmdVenta.Parameters.AddWithValue("@total", venta.Total);
                cmdVenta.Parameters.AddWithValue("@fecha", venta.Fecha);
                cmdVenta.Parameters.AddWithValue("@metodoPago", venta.MetodoPago);
                cmdVenta.Parameters.AddWithValue("@efectivoRecibido", venta.EfectivoRecibido);
                cmdVenta.Parameters.AddWithValue("@cambioEntregado", venta.CambioEntregado);

                object? resultado = await cmdVenta.ExecuteScalarAsync();
                ventaId = Convert.ToInt32(resultado);
            }

            foreach (VentaOfflineDetalle detalle in venta.Detalles)
            {
                await using (NpgsqlCommand cmdDetalle = new NpgsqlCommand(QueryInsertarDetalle, conexion, transaccion))
                {
                    cmdDetalle.Parameters.AddWithValue("@ventaId", ventaId);
                    cmdDetalle.Parameters.AddWithValue("@productoId", detalle.ProductoId.HasValue ? (object)detalle.ProductoId.Value : DBNull.Value);
                    cmdDetalle.Parameters.AddWithValue("@cantidad", detalle.Cantidad);
                    cmdDetalle.Parameters.AddWithValue("@precioUnitario", detalle.PrecioUnitario);
                    cmdDetalle.Parameters.AddWithValue("@subtotal", detalle.Subtotal);
                    cmdDetalle.Parameters.AddWithValue("@descripcionManual", detalle.EsArticuloComun ? detalle.Nombre : string.Empty);
                    cmdDetalle.Parameters.AddWithValue("@tipoArticulo", detalle.EsArticuloComun ? "Comun" : "Inventario");
                    await cmdDetalle.ExecuteNonQueryAsync();
                }

                if (detalle.EsArticuloComun)
                {
                    continue;
                }

                await using NpgsqlCommand cmdStock = new NpgsqlCommand(QueryDescontarStock, conexion, transaccion);
                cmdStock.Parameters.AddWithValue("@cantidad", detalle.Cantidad);
                cmdStock.Parameters.AddWithValue("@codigo", detalle.CodigoBarras);
                await using (NpgsqlDataReader readerStock = await cmdStock.ExecuteReaderAsync())
                {
                    if (await readerStock.ReadAsync())
                    {
                        decimal stockRestante = Convert.ToDecimal(readerStock["stock_actual"]);
                        decimal stockMinimo = readerStock["stock_minimo"] != DBNull.Value
                            ? Convert.ToDecimal(readerStock["stock_minimo"])
                            : 0m;

                        if (stockRestante <= stockMinimo)
                        {
                            productosBajoStock.Add(new ProductoBajoStock
                            {
                                Nombre = readerStock["nombre"].ToString() ?? detalle.Nombre,
                                Codigo = detalle.CodigoBarras,
                                StockActual = stockRestante,
                                StockMinimo = stockMinimo
                            });
                        }
                    }
                }
            }

            await transaccion.CommitAsync();
            TelegramNotificationService.NotificarBajoStockEnSegundoPlano(productosBajoStock);
            return ventaId;
        }
    }
}

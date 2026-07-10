using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace RefaccionariaPOS.Views
{
    public partial class VentaView : Window
    {
        private ObservableCollection<ProductoCarrito> listaCarrito = new ObservableCollection<ProductoCarrito>();
        private decimal totalVenta = 0;
        private Producto? productoEnVistaPrevia; // Reutilizamos tu variable perfectamente
        private readonly int usuarioId;
        private readonly string codigoInicial;
        private CancellationTokenSource? busquedaCancellation;
        private Task? cargaClientesTask;
        private List<ClienteVentaOpcion> clientesVenta = new();
        private int busquedaVersion;
        private bool activada;

        public VentaView(int usuarioId, string codigoInicial = "")
        {
            InitializeComponent();
            this.usuarioId = usuarioId;
            this.codigoInicial = codigoInicial;
            dgCarrito.ItemsSource = listaCarrito;
            PreviewKeyDown += VentaView_PreviewKeyDown;
            Loaded += VentaView_Loaded;
        }

        public VentaView() : this(0)
        {
        }

        private async void VentaView_Loaded(object sender, RoutedEventArgs e)
        {
            await AsegurarClientesFrecuentesAsync();
            await ActivarAsync();
        }

        public async void ActivarDesdePanel()
        {
            await AsegurarClientesFrecuentesAsync();
            await ActivarAsync();
        }

        private Task AsegurarClientesFrecuentesAsync()
        {
            return cargaClientesTask ??= CargarClientesFrecuentesAsync();
        }

        private async Task ActivarAsync()
        {
            if (activada)
            {
                return;
            }

            activada = true;
            txtBuscarId.Focus();
            if (string.IsNullOrWhiteSpace(codigoInicial))
            {
                return;
            }

            txtBuscarId.Text = codigoInicial;
            await ProcesarCodigoEscaneadoAsync();
        }

        // ==========================================================
        // 1. BUSCADOR EN TIEMPO REAL (Reemplaza al BuscarProductoReal)
        // ==========================================================
        private void TxtBuscarId_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = txtBuscarId.Text.Trim();
            CancelarBusquedaPendiente();

            // Si hay menos de 2 letras, ocultamos la tablita flotante y la vista previa
            if (busqueda.Length < 2)
            {
                if (dgResultadosBusqueda != null) dgResultadosBusqueda.Visibility = Visibility.Collapsed;
                OcultarVistaPrevia();
                return;
            }

            busquedaCancellation = new CancellationTokenSource();
            int versionActual = ++busquedaVersion;
            _ = BuscarProductosConEsperaAsync(busqueda, versionActual, busquedaCancellation.Token);
        }

        private async Task BuscarProductosConEsperaAsync(string busqueda, int version, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(250, cancellationToken);
                List<Producto> resultados = await BuscarProductosAsync(busqueda, cancellationToken);

                if (cancellationToken.IsCancellationRequested || version != busquedaVersion)
                {
                    return;
                }

                if (resultados.Count > 0)
                {
                    dgResultadosBusqueda.ItemsSource = resultados;
                    dgResultadosBusqueda.Visibility = Visibility.Visible;
                    OcultarVistaPrevia();
                    return;
                }

                dgResultadosBusqueda.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    dgResultadosBusqueda.Visibility = Visibility.Collapsed;
                }
            }
        }

        private static async Task<List<Producto>> BuscarProductosAsync(string busqueda, CancellationToken cancellationToken)
        {
            if (EstadoConexion.DebeIntentarOnline)
            {
                try
                {
                    List<Producto> resultados = await BuscarProductosOnlineAsync(busqueda, cancellationToken);
                    EstadoConexion.MarcarExito();
                    return resultados;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (EstadoConexion.EsErrorDeConexion(ex))
                {
                    EstadoConexion.MarcarFalla();
                }
            }

            return BuscarProductosOffline(busqueda);
        }

        private static List<Producto> BuscarProductosOffline(string busqueda)
        {
            return OfflineStore.BuscarProductos(busqueda, 15)
                .Select(ConvertirProductoOffline)
                .ToList();
        }

        private static Producto ConvertirProductoOffline(ProductoOffline producto)
        {
            return new Producto
            {
                Id = producto.Id,
                CodigoBarras = producto.CodigoBarras,
                Nombre = producto.Nombre,
                PrecioVenta = producto.PrecioVenta,
                Stock = producto.Stock,
                TipoVenta = producto.TipoVenta
            };
        }

        private static async Task<List<Producto>> BuscarProductosOnlineAsync(string busqueda, CancellationToken cancellationToken)
        {
            List<Producto> resultados = new List<Producto>();
            DatabaseConnection db = new DatabaseConnection();

            await using (NpgsqlConnection conexion = db.GetConnection())
            {
                await conexion.OpenAsync(cancellationToken);

                const string query = @"SELECT id, codigo_barras, nombre, precio_venta, stock_actual,
                                              COALESCE(tipo_venta, 'Unidad') AS tipo_venta
                                       FROM productos
                                       WHERE nombre ILIKE @busqueda OR codigo_barras ILIKE @busqueda
                                       ORDER BY nombre ASC LIMIT 15;";

                await using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@busqueda", "%" + busqueda + "%");

                    await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            resultados.Add(new Producto
                            {
                                CodigoBarras = reader["codigo_barras"].ToString() ?? string.Empty,
                                Nombre = reader["nombre"].ToString() ?? string.Empty,
                                PrecioVenta = Convert.ToDecimal(reader["precio_venta"]),
                                Stock = Convert.ToDecimal(reader["stock_actual"]),
                                TipoVenta = reader["tipo_venta"].ToString() ?? "Unidad",
                                Id = Convert.ToInt32(reader["id"])
                            });
                        }
                    }
                }
            }

            return resultados;
        }

        // ==========================================================
        // 2. AL SELECCIONAR UN PRODUCTO DE LA LISTA FLOTANTE
        // ==========================================================
        private void DgResultadosBusqueda_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgResultadosBusqueda.SelectedItem is Producto seleccionado)
            {
                // Pasamos el producto seleccionado a tu variable global
                productoEnVistaPrevia = seleccionado;

                // Llamamos a tu método que ya tenías para llenar la interfaz
                MostrarVistaPrevia(productoEnVistaPrevia);

                // Ocultamos la lista flotante porque ya eligió uno
                dgResultadosBusqueda.Visibility = Visibility.Collapsed;

                // Limpiamos la selección para que pueda volver a elegir el mismo después si quiere
                dgResultadosBusqueda.SelectedItem = null;

                if (EsVentaAGranel(seleccionado))
                {
                    AbrirCalculadoraGranel(seleccionado);
                }
            }
        }
        private async void TxtBuscarId_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CancelarBusquedaPendiente();
            await ProcesarCodigoEscaneadoAsync();
        }

        // ==========================================================
        // TUS MÉTODOS ORIGINALES (Se mantienen intactos)
        // ==========================================================
        private void MostrarVistaPrevia(Producto p)
        {
            bool esGranel = EsVentaAGranel(p);
            string unidad = ObtenerUnidadVenta(p).ToLowerInvariant();
            lblPreviewCodigo.Text = p.CodigoBarras;
            lblPreviewNombre.Text = p.Nombre;
            lblPreviewPrecio.Text = esGranel
                ? $"{p.PrecioVenta:C} por {unidad}"
                : string.Format("{0:C}", p.PrecioVenta);
            lblPreviewStock.Text = esGranel
                ? $"{FormatearCantidad(p.Stock)} {unidad}s"
                : FormatearCantidad(p.Stock);
            lblCantidadAgregar.Text = esGranel ? "Cantidad a vender:" : "Cantidad a agregar:";
            txtCantidadAgregar.Text = "1";
            txtCantidadAgregar.Visibility = esGranel ? Visibility.Collapsed : Visibility.Visible;


            brdPreview.Visibility = Visibility.Visible;
            btnAgregarAlCarrito.Visibility = Visibility.Visible;
            btnAgregarAlCarrito.Content = esGranel ? "Calcular y agregar" : "Agregar al carrito";

            if (!esGranel)
            {
                txtCantidadAgregar.Focus();
                txtCantidadAgregar.SelectAll();
            }
        }

        private void OcultarVistaPrevia()
        {
            brdPreview.Visibility = Visibility.Collapsed;
            btnAgregarAlCarrito.Visibility = Visibility.Collapsed;
            productoEnVistaPrevia = null;
        }

        private void BtnAgregarAlCarrito_Click(object sender, RoutedEventArgs e)
        {
            if (productoEnVistaPrevia == null) return;

            if (EsVentaAGranel(productoEnVistaPrevia))
            {
                AbrirCalculadoraGranel(productoEnVistaPrevia);
                return;
            }

            if (!decimal.TryParse(txtCantidadAgregar.Text, out decimal cantidad) || cantidad <= 0)
            {
                MessageBox.Show("Ingresa una cantidad válida a agregar.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (productoEnVistaPrevia.TipoVenta.Equals("Unidad", StringComparison.OrdinalIgnoreCase) && !EsCantidadEntera(cantidad))
            {
                MessageBox.Show("Este producto se vende por unidad. Ingresa una cantidad entera.", "Cantidad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AgregarProductoAlCarrito(productoEnVistaPrevia, cantidad);
        }

        private void AbrirCalculadoraGranel(Producto producto)
        {
            CalculadoraGranelWindow calculadora = new CalculadoraGranelWindow(
                producto.Nombre,
                ObtenerUnidadVenta(producto),
                producto.PrecioVenta,
                producto.Stock)
            {
                Owner = this
            };

            if (calculadora.ShowDialog() == true)
            {
                AgregarProductoAlCarrito(producto, calculadora.CantidadSeleccionada);
            }
        }

        private static bool EsVentaAGranel(Producto producto)
        {
            return !producto.TipoVenta.Equals("Unidad", StringComparison.OrdinalIgnoreCase);
        }

        private static string ObtenerUnidadVenta(Producto producto)
        {
            return producto.TipoVenta.Equals("Litro", StringComparison.OrdinalIgnoreCase) ? "Litro" : "Metro";
        }

        private void AgregarProductoAlCarrito(Producto producto, decimal cantidad)
        {
            if (cantidad > producto.Stock)
            {
                MessageBox.Show($"¡Error de Stock! Solo quedan {producto.Stock} piezas.", "Sin Existencias", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var itemExistente = listaCarrito.FirstOrDefault(i => i.CodigoBarras == producto.CodigoBarras);

            if (itemExistente != null)
            {
                if ((itemExistente.Cantidad + cantidad) > producto.Stock)
                {
                    MessageBox.Show($"Límite excedido. Ya tienes {itemExistente.Cantidad} en el carrito.", "Límite de Stock", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                itemExistente.Cantidad += cantidad;
                dgCarrito.Items.Refresh();
            }
            else
            {
                listaCarrito.Add(new ProductoCarrito
                {
                    ProductoId = producto.Id,
                    CodigoBarras = producto.CodigoBarras,
                    Nombre = producto.Nombre,
                    PrecioVenta = producto.PrecioVenta,
                    Cantidad = cantidad
                });
            }

            ActualizarTotales();
            txtBuscarId.Clear();
            OcultarVistaPrevia();
            txtBuscarId.Focus();
        }

        private void VentaView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBoxBase)
            {
                return;
            }

            if (dgCarrito.SelectedItem is not ProductoCarrito item)
            {
                return;
            }

            if (e.Key == Key.Add || e.Key == Key.OemPlus)
            {
                item.Cantidad++;
                dgCarrito.Items.Refresh();
                ActualizarTotales();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
            {
                item.Cantidad--;
                if (item.Cantidad <= 0)
                {
                    listaCarrito.Remove(item);
                }

                dgCarrito.Items.Refresh();
                ActualizarTotales();
                e.Handled = true;
            }
        }

        private async Task ProcesarCodigoEscaneadoAsync()
        {
            string codigo = txtBuscarId.Text.Trim();
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return;
            }

            Producto? producto;
            try
            {
                producto = await BuscarProductoPorCodigoExactoAsync(codigo);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo consultar el código escaneado: " + ex.Message, "Error de conexión", MessageBoxButton.OK, MessageBoxImage.Error);
                txtBuscarId.SelectAll();
                return;
            }

            if (producto == null)
            {
                MessageBox.Show("No se encontró una refacción con el código escaneado: " + codigo, "Código no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtBuscarId.SelectAll();
                return;
            }

            if (EsVentaAGranel(producto))
            {
                productoEnVistaPrevia = producto;
                MostrarVistaPrevia(producto);
                AbrirCalculadoraGranel(producto);
                return;
            }

            AgregarProductoAlCarrito(producto, 1);
        }

        private static async Task<Producto?> BuscarProductoPorCodigoExactoAsync(string codigo)
        {
            if (EstadoConexion.DebeIntentarOnline)
            {
                try
                {
                    Producto? resultado = await BuscarProductoPorCodigoExactoOnlineAsync(codigo);
                    EstadoConexion.MarcarExito();
                    return resultado;
                }
                catch (Exception ex) when (EstadoConexion.EsErrorDeConexion(ex))
                {
                    EstadoConexion.MarcarFalla();
                }
            }

            ProductoOffline? productoLocal = OfflineStore.BuscarProductoPorCodigo(codigo);
            return productoLocal == null ? null : ConvertirProductoOffline(productoLocal);
        }

        private static async Task<Producto?> BuscarProductoPorCodigoExactoOnlineAsync(string codigo)
        {
            DatabaseConnection db = new DatabaseConnection();

            await using (NpgsqlConnection conexion = db.GetConnection())
            {
                await conexion.OpenAsync();
                const string query = @"SELECT id, codigo_barras, nombre, precio_venta, stock_actual,
                                              COALESCE(tipo_venta, 'Unidad') AS tipo_venta
                                       FROM productos
                                       WHERE codigo_barras = @codigo
                                       LIMIT 1;";

                await using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@codigo", codigo);

                    await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                        {
                            return null;
                        }

                        return new Producto
                        {
                            CodigoBarras = reader["codigo_barras"].ToString() ?? string.Empty,
                            Nombre = reader["nombre"].ToString() ?? string.Empty,
                            PrecioVenta = Convert.ToDecimal(reader["precio_venta"]),
                            Stock = Convert.ToDecimal(reader["stock_actual"]),
                            TipoVenta = reader["tipo_venta"].ToString() ?? "Unidad",
                            Id = Convert.ToInt32(reader["id"])
                        };
                    }
                }
            }
        }

        private void CancelarBusquedaPendiente()
        {
            busquedaVersion++;
            busquedaCancellation?.Cancel();
            busquedaCancellation?.Dispose();
            busquedaCancellation = null;
        }

        private void ActualizarTotales()
        {
            totalVenta = listaCarrito.Sum(item => item.Subtotal);
            lblTotalCarrito.Text = string.Format("{0:C}", totalVenta);
        }

        private async Task CargarClientesFrecuentesAsync()
        {
            List<ClienteVentaOpcion> clientes = new()
            {
                new ClienteVentaOpcion { Id = 0, Nombre = "Sin cliente" }
            };

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                await using NpgsqlConnection conexion = db.GetConnection();
                await conexion.OpenAsync();

                const string query = @"
                    SELECT id, nombre
                    FROM clientes
                    ORDER BY nombre ASC;";

                await using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    clientes.Add(new ClienteVentaOpcion
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        Nombre = reader["nombre"].ToString() ?? string.Empty
                    });
                }
            }
            catch
            {
            }

            clientesVenta = clientes;
        }

        private static void SumarPuntoCliente(NpgsqlConnection conexion, NpgsqlTransaction transaccion, int? clienteId)
        {
            if (!clienteId.HasValue)
            {
                return;
            }

            using (NpgsqlCommand cmd = new NpgsqlCommand("UPDATE clientes SET puntos = puntos + 1 WHERE id = @clienteId;", conexion, transaccion))
            {
                cmd.Parameters.AddWithValue("@clienteId", clienteId.Value);
                cmd.ExecuteNonQuery();
            }
        }

        // =========================================================================
        // MOTOR DE COBRO DEFINITIVO: POSTGRESQL GENERA EL FOLIO Y C# LO IMPRIME
        // =========================================================================
        private void BtnCobrar_Click(object sender, RoutedEventArgs e)
        {
            if (listaCarrito.Count == 0)
            {
                MessageBox.Show("El carrito de compras está vacío.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CobroWindow cobro = new CobroWindow(totalVenta, clientesVenta);
            Window? duenio = Application.Current?.MainWindow;
            if (duenio != null && duenio.IsVisible && !ReferenceEquals(duenio, this))
            {
                cobro.Owner = duenio;
                cobro.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            if (cobro.ShowDialog() != true)
            {
                return;
            }

            string metodoPago = cobro.MetodoPago;
            decimal efectivoRecibido = cobro.EfectivoRecibido;
            decimal cambioEntregado = cobro.CambioEntregado;
            int? clienteId = cobro.ClienteId;

            int ventaIdGenerado = 0;
            bool ventaOffline = true;

            if (EstadoConexion.DebeIntentarOnline)
            {
                try
                {
                    ventaIdGenerado = RegistrarVentaEnBase(metodoPago, efectivoRecibido, cambioEntregado, clienteId);
                    EstadoConexion.MarcarExito();
                    ventaOffline = false;
                }
                catch (Exception ex) when (EstadoConexion.EsErrorDeConexion(ex))
                {
                    // Sin conexion: la venta se registra localmente y se sincroniza despues.
                    EstadoConexion.MarcarFalla();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al procesar la venta en la Base de Datos: " + ex.Message, "Venta Cancelada", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            if (ventaOffline)
            {
                RegistrarVentaOffline(metodoPago, efectivoRecibido, cambioEntregado, clienteId, cobro.Imprimir, cobro.Impresora);
            }
            else
            {
                GenerarTicket(ventaIdGenerado, cobro.Imprimir, cobro.Impresora);

                // Aviso por WhatsApp a los numeros configurados (no bloquea ni afecta la venta si falla).
                WhatsAppNotificationService.NotificarVentaEnSegundoPlano(ventaIdGenerado);
            }

            listaCarrito.Clear();
            ActualizarTotales();
            txtBuscarId.Focus();
        }

        private int RegistrarVentaEnBase(string metodoPago, decimal efectivoRecibido, decimal cambioEntregado, int? clienteId)
        {
            int ventaIdGenerado = 0;
            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                using (NpgsqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        string queryVenta = @"
                    INSERT INTO ventas (usuario_id, cliente_id, total, fecha_venta, estado, metodo_pago, efectivo_recibido, cambio_entregado)
                    VALUES (@usuarioId, @clienteId, @total, @fecha, @estado, @metodoPago, @efectivoRecibido, @cambioEntregado)
                    RETURNING id, folio;";

                        using (NpgsqlCommand cmdVenta = new NpgsqlCommand(queryVenta, conexion, transaccion))
                        {
                            cmdVenta.Parameters.AddWithValue("@usuarioId", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                            cmdVenta.Parameters.AddWithValue("@clienteId", clienteId.HasValue ? (object)clienteId.Value : DBNull.Value);
                            cmdVenta.Parameters.AddWithValue("@total", totalVenta);
                            cmdVenta.Parameters.AddWithValue("@fecha", DateTime.Now);
                            cmdVenta.Parameters.AddWithValue("@estado", "Completada");
                            cmdVenta.Parameters.AddWithValue("@metodoPago", metodoPago);
                            cmdVenta.Parameters.AddWithValue("@efectivoRecibido", efectivoRecibido);
                            cmdVenta.Parameters.AddWithValue("@cambioEntregado", cambioEntregado);

                            using (NpgsqlDataReader reader = cmdVenta.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    ventaIdGenerado = Convert.ToInt32(reader["id"]);
                                }
                            }
                        }

                        foreach (var item in listaCarrito)
                        {
                            int? productoId = item.ProductoId > 0 ? item.ProductoId : null;

                            if (!item.EsArticuloComun)
                            {
                                string queryProducto = @"
                        SELECT id, stock_actual
                        FROM productos
                        WHERE codigo_barras = @codigo;";

                                using (NpgsqlCommand cmdProducto = new NpgsqlCommand(queryProducto, conexion, transaccion))
                                {
                                    cmdProducto.Parameters.AddWithValue("@codigo", item.CodigoBarras);

                                    using (NpgsqlDataReader reader = cmdProducto.ExecuteReader())
                                    {
                                        if (!reader.Read())
                                        {
                                            throw new Exception("No se encontró el producto con código: " + item.CodigoBarras);
                                        }

                                        productoId = Convert.ToInt32(reader["id"]);
                                    decimal stockActual = Convert.ToDecimal(reader["stock_actual"]);

                                        if (stockActual < item.Cantidad)
                                        {
                                            throw new Exception("Stock insuficiente para el producto: " + item.Nombre);
                                        }
                                    }
                                }
                            }

                            string queryDetalle = @"
                        INSERT INTO detalles_venta
                        (venta_id, producto_id, cantidad, precio_unitario, subtotal, descripcion_manual, tipo_articulo)
                        VALUES
                        (@ventaId, @productoId, @cantidad, @precioUnitario, @subtotal, @descripcionManual, @tipoArticulo);";

                            using (NpgsqlCommand cmdDetalle = new NpgsqlCommand(queryDetalle, conexion, transaccion))
                            {
                                cmdDetalle.Parameters.AddWithValue("@ventaId", ventaIdGenerado);
                                cmdDetalle.Parameters.AddWithValue("@productoId", productoId.HasValue ? (object)productoId.Value : DBNull.Value);
                                cmdDetalle.Parameters.AddWithValue("@cantidad", item.Cantidad);
                                cmdDetalle.Parameters.AddWithValue("@precioUnitario", item.PrecioVenta);
                                cmdDetalle.Parameters.AddWithValue("@subtotal", item.Subtotal);
                                cmdDetalle.Parameters.AddWithValue("@descripcionManual", item.EsArticuloComun ? item.Nombre : string.Empty);
                                cmdDetalle.Parameters.AddWithValue("@tipoArticulo", item.EsArticuloComun ? "Comun" : "Inventario");

                                cmdDetalle.ExecuteNonQuery();
                            }

                            if (item.EsArticuloComun)
                            {
                                continue;
                            }

                            string queryStock = @"
                        UPDATE productos
                        SET stock_actual = stock_actual - @cantidad
                        WHERE codigo_barras = @codigo;";

                            using (NpgsqlCommand cmdStock = new NpgsqlCommand(queryStock, conexion, transaccion))
                            {
                                cmdStock.Parameters.AddWithValue("@cantidad", item.Cantidad);
                                cmdStock.Parameters.AddWithValue("@codigo", item.CodigoBarras);

                                cmdStock.ExecuteNonQuery();
                            }
                        }

                        SumarPuntoCliente(conexion, transaccion, clienteId);

                        transaccion.Commit();
                    }
                    catch
                    {
                        transaccion.Rollback();
                        throw;
                    }
                }
            }

            return ventaIdGenerado;
        }

        private void RegistrarVentaOffline(string metodoPago, decimal efectivoRecibido, decimal cambioEntregado, int? clienteId, bool imprimir, string? impresora)
        {
            DateTime fecha = DateTime.Now;
            VentaOffline venta = new VentaOffline
            {
                UsuarioId = usuarioId,
                ClienteId = clienteId,
                Fecha = fecha,
                Total = totalVenta,
                MetodoPago = metodoPago,
                EfectivoRecibido = efectivoRecibido,
                CambioEntregado = cambioEntregado,
                Detalles = listaCarrito.Select(item => new VentaOfflineDetalle
                {
                    ProductoId = item.ProductoId > 0 ? item.ProductoId : null,
                    CodigoBarras = item.CodigoBarras,
                    Nombre = item.Nombre,
                    Cantidad = item.Cantidad,
                    PrecioUnitario = item.PrecioVenta,
                    Subtotal = item.Subtotal,
                    EsArticuloComun = item.EsArticuloComun
                }).ToList()
            };

            OfflineStore.AgregarVentaPendiente(venta);
            OfflineStore.DescontarStock(venta.Detalles
                .Where(detalle => !detalle.EsArticuloComun)
                .Select(detalle => (detalle.CodigoBarras, detalle.Cantidad)));

            TicketVenta ticket = new TicketVenta
            {
                Folio = 0,
                Fecha = fecha,
                Total = totalVenta,
                MetodoPago = metodoPago,
                EfectivoRecibido = efectivoRecibido,
                CambioEntregado = cambioEntregado,
                Vendedor = ObtenerNombreVendedorLocal()
            };

            foreach (VentaOfflineDetalle detalle in venta.Detalles)
            {
                ticket.Detalles.Add(new TicketDetalle
                {
                    Codigo = detalle.EsArticuloComun ? "COMUN" : detalle.CodigoBarras,
                    Nombre = detalle.Nombre,
                    Cantidad = detalle.Cantidad,
                    PrecioUnitario = detalle.PrecioUnitario,
                    Subtotal = detalle.Subtotal
                });
            }

            try
            {
                TicketService.GenerarTicketLocal(ticket, imprimir, impresora);
            }
            catch (Exception ex)
            {
                MessageBox.Show("La venta se guardo, pero no se pudo generar o imprimir el ticket: " + ex.Message, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            MessageBox.Show(
                "No hay conexion con el servidor. La venta quedo guardada en esta computadora\n" +
                "y se sincronizara automaticamente cuando regrese el internet.",
                "Venta guardada (modo offline)", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string ObtenerNombreVendedorLocal()
        {
            return OfflineStore.CargarUsuarios().FirstOrDefault(u => u.Id == usuarioId)?.Username ?? "CAJA";
        }

        private void GenerarTicket(int ventaId, bool imprimir, string? impresora)
        {
            try
            {
                TicketService.GenerarTicketVenta(ventaId, imprimir, impresora);
            }
            catch (Exception ex)
            {
                MessageBox.Show("La venta se registró, pero no se pudo generar o imprimir el ticket: " + ex.Message, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void AsegurarColumnasPuntoVenta()
        {
            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        ALTER TABLE productos
                            ADD COLUMN IF NOT EXISTS imagen_url text NOT NULL DEFAULT '',
                            ADD COLUMN IF NOT EXISTS tipo_venta varchar(20) NOT NULL DEFAULT 'Unidad';

                        ALTER TABLE productos
                            ALTER COLUMN stock_actual TYPE numeric(12, 3) USING stock_actual::numeric,
                            ALTER COLUMN stock_minimo TYPE numeric(12, 3) USING stock_minimo::numeric;

                        ALTER TABLE detalles_venta
                            ADD COLUMN IF NOT EXISTS descripcion_manual text NOT NULL DEFAULT '',
                            ADD COLUMN IF NOT EXISTS tipo_articulo varchar(30) NOT NULL DEFAULT 'Inventario';

                        ALTER TABLE detalles_venta
                            ALTER COLUMN cantidad TYPE numeric(12, 3) USING cantidad::numeric;

                        CREATE TABLE IF NOT EXISTS clientes (
                            id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                            nombre varchar(180) NOT NULL,
                            telefono varchar(40) NOT NULL DEFAULT '',
                            correo varchar(160) NOT NULL DEFAULT '',
                            notas text NOT NULL DEFAULT '',
                            puntos integer NOT NULL DEFAULT 0,
                            fecha_alta timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                        );

                        ALTER TABLE ventas
                            ADD COLUMN IF NOT EXISTS cliente_id integer NULL REFERENCES clientes(id) ON DELETE SET NULL,
                            ADD COLUMN IF NOT EXISTS efectivo_recibido numeric(12, 2) NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS cambio_entregado numeric(12, 2) NOT NULL DEFAULT 0;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
            }
        }

        private List<string> CrearLineasTicket(int folio)
        {
            List<string> lineas = new List<string>
            {
                "SERVICIO AUTOMOTRIZ LOPEZ",
                "Refacciones y Accesorios Automotrices",
                "Direccion:",
                "Calle Principal #123, Col. Centro, Tehuacan, Puebla",
                "Tel / WhatsApp:",
                "(238) 000-0000",
                $"Ticket No.: {folio}",
                $"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                string.Empty,
                "#  Clave        Descripcion                 Marca      Cant.  P. Unit.      Total",
                "--------------------------------------------------------------------------------"
            };

            int numeroLinea = 1;
            foreach (var item in listaCarrito.Take(15))
            {
                lineas.Add(string.Format(
                    "{0,-2} {1,-12} {2,-27} {3,-10} {4,5} {5,9:C} {6,10:C}",
                    numeroLinea,
                    AjustarTexto(item.CodigoBarras, 12),
                    AjustarTexto(item.Nombre, 27),
                    string.Empty,
                    FormatearCantidad(item.Cantidad),
                    item.PrecioVenta,
                    item.Subtotal));
                numeroLinea++;
            }

            while (numeroLinea <= 15)
            {
                lineas.Add($"{numeroLinea,-2}");
                numeroLinea++;
            }

            lineas.Add("--------------------------------------------------------------------------------");
            lineas.Add(string.Format("{0,63} {1,14:C}", "Subtotal:", totalVenta));
            lineas.Add(string.Format("{0,63} {1,14:C}", "TOTAL:", totalVenta));
            lineas.Add(string.Empty);
            lineas.Add("Gracias por su preferencia  |  Servicio Automotriz Lopez  |  \"Tu refaccionaria de confianza\"");

            return lineas;
        }

        private static string FormatearCantidad(decimal cantidad)
        {
            return cantidad % 1 == 0 ? cantidad.ToString("0") : cantidad.ToString("0.###");
        }

        private static bool EsCantidadEntera(decimal cantidad)
        {
            return cantidad % 1 == 0;
        }

        private static string AjustarTexto(string texto, int longitudMaxima)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }

            return texto.Length <= longitudMaxima
                ? texto
                : texto.Substring(0, longitudMaxima);
        }

    }

    public class ProductoCarrito
    {
        public int ProductoId { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal PrecioVenta { get; set; }
        public decimal Cantidad { get; set; }
        public bool EsArticuloComun { get; set; }
        public decimal Subtotal => PrecioVenta * Cantidad;
    }

    public class ClienteVentaOpcion
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }
}

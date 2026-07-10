using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RefaccionariaPOS.Services
{
    /// <summary>
    /// Guarda en disco (LocalAppData) el catalogo de productos, los usuarios y las
    /// ventas pendientes para que el POS pueda seguir operando sin internet.
    /// Los archivos se escriben de forma atomica (archivo temporal + reemplazo).
    /// </summary>
    public static class OfflineStore
    {
        private static readonly object SyncRoot = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        private static string CarpetaOffline
        {
            get
            {
                string carpeta = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RefaxManager",
                    "Offline");
                Directory.CreateDirectory(carpeta);
                return carpeta;
            }
        }

        private static string RutaProductos => Path.Combine(CarpetaOffline, "productos.json");
        private static string RutaUsuarios => Path.Combine(CarpetaOffline, "usuarios.json");
        private static string RutaVentasPendientes => Path.Combine(CarpetaOffline, "ventas_pendientes.json");

        public static void GuardarProductos(IReadOnlyList<ProductoOffline> productos)
        {
            Escribir(RutaProductos, productos);
        }

        public static List<ProductoOffline> CargarProductos()
        {
            return Leer<List<ProductoOffline>>(RutaProductos) ?? new List<ProductoOffline>();
        }

        public static List<ProductoOffline> BuscarProductos(string termino, int limite)
        {
            string busqueda = termino.Trim();
            return CargarProductos()
                .Where(producto => producto.Nombre.Contains(busqueda, StringComparison.OrdinalIgnoreCase)
                    || producto.CodigoBarras.Contains(busqueda, StringComparison.OrdinalIgnoreCase))
                .OrderBy(producto => producto.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .Take(limite)
                .ToList();
        }

        public static ProductoOffline? BuscarProductoPorCodigo(string codigo)
        {
            return CargarProductos()
                .FirstOrDefault(producto => producto.CodigoBarras.Equals(codigo, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Descuenta stock del catalogo local para que las ventas offline
        /// siguientes vean las existencias actualizadas.
        /// </summary>
        public static void DescontarStock(IEnumerable<(string Codigo, decimal Cantidad)> partidas)
        {
            lock (SyncRoot)
            {
                List<ProductoOffline> productos = CargarProductos();
                foreach ((string codigo, decimal cantidad) in partidas)
                {
                    ProductoOffline? producto = productos
                        .FirstOrDefault(p => p.CodigoBarras.Equals(codigo, StringComparison.OrdinalIgnoreCase));
                    if (producto != null)
                    {
                        producto.Stock = Math.Max(0, producto.Stock - cantidad);
                    }
                }

                GuardarProductos(productos);
            }
        }

        public static void GuardarUsuarios(IReadOnlyList<UsuarioOffline> usuarios)
        {
            Escribir(RutaUsuarios, usuarios);
        }

        public static List<UsuarioOffline> CargarUsuarios()
        {
            return Leer<List<UsuarioOffline>>(RutaUsuarios) ?? new List<UsuarioOffline>();
        }

        public static void AgregarVentaPendiente(VentaOffline venta)
        {
            lock (SyncRoot)
            {
                List<VentaOffline> pendientes = CargarVentasPendientes();
                pendientes.Add(venta);
                Escribir(RutaVentasPendientes, pendientes);
            }
        }

        public static List<VentaOffline> CargarVentasPendientes()
        {
            return Leer<List<VentaOffline>>(RutaVentasPendientes) ?? new List<VentaOffline>();
        }

        public static void EliminarVentaPendiente(Guid idLocal)
        {
            lock (SyncRoot)
            {
                List<VentaOffline> pendientes = CargarVentasPendientes();
                pendientes.RemoveAll(venta => venta.IdLocal == idLocal);
                Escribir(RutaVentasPendientes, pendientes);
            }
        }

        public static int ContarVentasPendientes()
        {
            return CargarVentasPendientes().Count;
        }

        private static void Escribir<T>(string ruta, T datos)
        {
            lock (SyncRoot)
            {
                string temporal = ruta + ".tmp";
                File.WriteAllText(temporal, JsonSerializer.Serialize(datos, JsonOptions));
                File.Move(temporal, ruta, overwrite: true);
            }
        }

        private static T? Leer<T>(string ruta)
        {
            lock (SyncRoot)
            {
                if (!File.Exists(ruta))
                {
                    return default;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(ruta));
                }
                catch
                {
                    // Archivo corrupto: se ignora y se regenera en el siguiente refresco.
                    return default;
                }
            }
        }
    }

    public class ProductoOffline
    {
        public int Id { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Categoria { get; set; } = "General";
        public string TipoVenta { get; set; } = "Unidad";
        public decimal PrecioVenta { get; set; }
        public decimal Stock { get; set; }
    }

    public class UsuarioOffline
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Rol { get; set; } = "Vendedor";
        public string PasswordHash { get; set; } = string.Empty;
    }

    public class VentaOffline
    {
        public Guid IdLocal { get; set; } = Guid.NewGuid();
        public int UsuarioId { get; set; }
        public int? ClienteId { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public string MetodoPago { get; set; } = "Mostrador";
        public decimal EfectivoRecibido { get; set; }
        public decimal CambioEntregado { get; set; }
        public List<VentaOfflineDetalle> Detalles { get; set; } = new();
    }

    public class VentaOfflineDetalle
    {
        public int? ProductoId { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Subtotal { get; set; }
        public bool EsArticuloComun { get; set; }
    }
}

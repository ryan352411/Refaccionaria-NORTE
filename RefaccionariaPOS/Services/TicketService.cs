using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Text.RegularExpressions;

namespace RefaccionariaPOS.Services
{
    public static class TicketService
    {
        public static string GenerarTicketVenta(int ventaId, bool imprimir, string? impresora = null, bool esReimpresion = false)
        {
            TicketVenta ticket = ObtenerTicket(ventaId);
            TicketPlantilla plantilla = TicketPlantillaService.ObtenerPlantilla(TicketPlantillaService.TipoVenta);
            List<string> lineas = CrearLineas(ticket, esReimpresion, esProvisional: false, plantilla);
            string rutaArchivo = GuardarTicket(ticket.Folio, lineas, esReimpresion ? "Reimpresion" : "Ticket");

            if (imprimir)
            {
                ImprimirTicket(lineas, impresora, plantilla.AnchoCaracteres);
            }

            return rutaArchivo;
        }

        /// <summary>
        /// Genera un ticket a partir de datos en memoria (sin consultar la base).
        /// Se usa en modo offline: el folio real se asigna al sincronizar.
        /// </summary>
        public static string GenerarTicketLocal(TicketVenta ticket, bool imprimir, string? impresora = null)
        {
            TicketPlantilla plantilla = TicketPlantillaService.ObtenerPlantilla(TicketPlantillaService.TipoVenta);
            List<string> lineas = CrearLineas(ticket, esReimpresion: false, esProvisional: true, plantilla);
            string rutaArchivo = GuardarTicket(ticket.Folio, lineas, "TicketOffline");

            if (imprimir)
            {
                ImprimirTicket(lineas, impresora, plantilla.AnchoCaracteres);
            }

            return rutaArchivo;
        }

        public static string ReimprimirTicket(int ventaId, int usuarioId)
        {
            AsegurarTablaReimpresiones();

            string rutaArchivo = GenerarTicketVenta(ventaId, imprimir: true, esReimpresion: true);
            RegistrarReimpresion(ventaId, usuarioId);
            return rutaArchivo;
        }

        public static TicketVenta ObtenerTicket(int ventaId)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string ventaQuery = @"
                    SELECT v.id, v.folio, v.fecha_venta, v.total, v.metodo_pago,
                           COALESCE(v.efectivo_recibido, 0) AS efectivo_recibido,
                           COALESCE(v.cambio_entregado, 0) AS cambio_entregado,
                           COALESCE(u.username, 'Sin usuario') AS vendedor
                    FROM ventas v
                    LEFT JOIN usuarios u ON u.id = v.usuario_id
                    WHERE v.id = @ventaId
                    LIMIT 1;";

                TicketVenta? ticket = null;
                using (NpgsqlCommand cmd = new NpgsqlCommand(ventaQuery, conexion))
                {
                    cmd.Parameters.AddWithValue("@ventaId", ventaId);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            ticket = new TicketVenta
                            {
                                VentaId = Convert.ToInt32(reader["id"]),
                                Folio = Convert.ToInt32(reader["folio"]),
                                Fecha = Convert.ToDateTime(reader["fecha_venta"]),
                                Total = Convert.ToDecimal(reader["total"]),
                                MetodoPago = reader["metodo_pago"].ToString() ?? "Mostrador",
                                EfectivoRecibido = Convert.ToDecimal(reader["efectivo_recibido"]),
                                CambioEntregado = Convert.ToDecimal(reader["cambio_entregado"]),
                                Vendedor = reader["vendedor"].ToString() ?? "Sin usuario"
                            };
                        }
                    }
                }

                if (ticket == null)
                {
                    throw new InvalidOperationException("No se encontro la venta seleccionada.");
                }

                const string detalleQuery = @"
                    SELECT COALESCE(NULLIF(dv.descripcion_manual, ''), p.nombre, 'Producto eliminado') AS nombre,
                           dv.cantidad,
                           dv.precio_unitario,
                           dv.subtotal,
                           COALESCE(p.codigo_barras, 'COMUN') AS codigo
                    FROM detalles_venta dv
                    LEFT JOIN productos p ON p.id = dv.producto_id
                    WHERE dv.venta_id = @ventaId
                    ORDER BY dv.id;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(detalleQuery, conexion))
                {
                    cmd.Parameters.AddWithValue("@ventaId", ventaId);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ticket.Detalles.Add(new TicketDetalle
                            {
                                Codigo = reader["codigo"].ToString() ?? "COMUN",
                                Nombre = reader["nombre"].ToString() ?? string.Empty,
                                Cantidad = Convert.ToDecimal(reader["cantidad"]),
                                PrecioUnitario = Convert.ToDecimal(reader["precio_unitario"]),
                                Subtotal = Convert.ToDecimal(reader["subtotal"])
                            });
                        }
                    }
                }

                return ticket;
            }
        }

        private static List<string> CrearLineas(TicketVenta ticket, bool esReimpresion, bool esProvisional = false, TicketPlantilla? plantilla = null)
        {
            plantilla ??= TicketPlantillaService.ObtenerPlantilla(TicketPlantillaService.TipoVenta);
            int ancho = TicketPlantillaService.LimitarAncho(plantilla.AnchoCaracteres);

            List<string> lineas = new();
            AgregarLineasCentradas(lineas, plantilla.LineasEncabezado, ancho);
            lineas.Add(string.Empty);

            if (esReimpresion)
            {
                lineas.Add(Centrar("REIMPRESION", ancho));
            }

            if (esProvisional)
            {
                lineas.Add(Centrar("VENTA SIN CONEXION", ancho));
            }

            Dictionary<string, string> valores = new(StringComparer.OrdinalIgnoreCase)
            {
                ["fecha"] = FormatearFechaTicket(ticket.Fecha),
                ["cajero"] = (ticket.Vendedor ?? string.Empty).ToUpperInvariant(),
                ["folio"] = esProvisional ? "PENDIENTE" : ticket.Folio.ToString(),
                ["metodopago"] = (ticket.MetodoPago ?? string.Empty).ToUpperInvariant(),
                ["articulos"] = FormatearCantidad(ContarArticulos(ticket)),
                ["total"] = ticket.Total.ToString("C"),
                ["pagocon"] = ObtenerPagoCon(ticket).ToString("C"),
                ["cambio"] = ticket.CambioEntregado.ToString("C")
            };

            Dictionary<string, Func<List<string>>> bloques = new(StringComparer.OrdinalIgnoreCase)
            {
                ["partidas"] = () => CrearPartidasVenta(ticket, ancho)
            };

            lineas.AddRange(ExpandirCuerpo(plantilla.Cuerpo, ancho, valores, bloques));

            lineas.Add(string.Empty);
            AgregarLineasCentradas(lineas, plantilla.LineasPie, ancho);
            lineas.Add(string.Empty);
            lineas.Add(string.Empty);
            return lineas;
        }

        /// <summary>
        /// Ticket de corte de caja generado con la plantilla editable. Los datos
        /// llegan ya calculados (del corte real o de la vista previa).
        /// </summary>
        public static List<string> GenerarLineasCorte(TicketCorteDatos datos, TicketPlantilla? plantilla = null)
        {
            plantilla ??= TicketPlantillaService.ObtenerPlantilla(TicketPlantillaService.TipoCorte);
            int ancho = TicketPlantillaService.LimitarAncho(plantilla.AnchoCaracteres);

            List<string> lineas = new();
            AgregarLineasCentradas(lineas, plantilla.LineasEncabezado, ancho);

            Dictionary<string, string> valores = new(StringComparer.OrdinalIgnoreCase)
            {
                ["tipocorte"] = datos.TipoCorte,
                ["fecha"] = $"{datos.Fecha:dd/MM/yyyy HH:mm}",
                ["usuario"] = datos.Usuario,
                ["caja"] = datos.Caja,
                ["periodo"] = datos.Periodo,
                ["ventas"] = datos.VentasTotales.ToString(),
                ["totalvendido"] = datos.TotalVendido.ToString("C"),
                ["pagosprov"] = datos.PagosProveedores.ToString("C")
            };

            Dictionary<string, Func<List<string>>> bloques = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pagos"] = () => CrearTablaMontos(datos.Pagos, "Sin ventas registradas.", ancho),
                ["categorias"] = () => CrearTablaMontos(datos.Categorias, "Sin categorias vendidas.", ancho)
            };

            lineas.AddRange(ExpandirCuerpo(plantilla.Cuerpo, ancho, valores, bloques));

            AgregarLineasCentradas(lineas, plantilla.LineasPie, ancho);
            lineas.Add(string.Empty);
            lineas.Add(string.Empty);
            return lineas;
        }

        // =====================================================================
        // Motor de la plantilla: cada linea del cuerpo puede llevar marcadores
        // {dato}, empezar con ^ para centrarse o usar ~ para separar el texto
        // en izquierda y derecha. {linea} y {lineadoble} dibujan separadores y
        // {partidas}/{pagos}/{categorias} expanden las tablas con los datos.
        // =====================================================================
        private static List<string> ExpandirCuerpo(
            string cuerpo,
            int ancho,
            Dictionary<string, string> valores,
            Dictionary<string, Func<List<string>>> bloques)
        {
            List<string> lineas = new();

            foreach (string cruda in (cuerpo ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                string linea = cruda.TrimEnd();
                string marcador = linea.Trim();

                if (marcador.StartsWith("{") && marcador.EndsWith("}")
                    && bloques.TryGetValue(marcador[1..^1], out Func<List<string>>? bloque))
                {
                    lineas.AddRange(bloque());
                    continue;
                }

                lineas.Add(RenderizarLinea(linea, ancho, valores));
            }

            return lineas;
        }

        private static string RenderizarLinea(string linea, int ancho, Dictionary<string, string> valores)
        {
            bool centrar = linea.StartsWith("^");
            if (centrar)
            {
                linea = linea[1..];
            }

            linea = ReemplazarMarcadores(linea, ancho, valores);

            if (centrar)
            {
                return Centrar(linea, ancho);
            }

            int separador = linea.IndexOf('~');
            if (separador >= 0)
            {
                string izquierda = linea[..separador];
                string derecha = linea[(separador + 1)..];
                int espacios = Math.Max(1, ancho - izquierda.Length - derecha.Length);
                return AjustarTexto(izquierda + new string(' ', espacios) + derecha, ancho);
            }

            return AjustarTexto(linea, ancho);
        }

        private static string ReemplazarMarcadores(string linea, int ancho, Dictionary<string, string> valores)
        {
            return Regex.Replace(linea, @"\{([a-zA-Z]+)\}", coincidencia =>
            {
                string clave = coincidencia.Groups[1].Value;

                if (clave.Equals("linea", StringComparison.OrdinalIgnoreCase))
                {
                    return new string('-', ancho);
                }

                if (clave.Equals("lineadoble", StringComparison.OrdinalIgnoreCase))
                {
                    return new string('=', ancho);
                }

                return valores.TryGetValue(clave, out string? valor) ? valor : coincidencia.Value;
            });
        }

        private static List<string> CrearPartidasVenta(TicketVenta ticket, int ancho)
        {
            const int anchoCantidad = 5;
            const int anchoImporte = 11;
            int anchoDescripcion = Math.Max(8, ancho - anchoCantidad - anchoImporte);

            List<string> lineas = new();
            foreach (TicketDetalle detalle in ticket.Detalles)
            {
                List<string> descripcion = PartirTexto((detalle.Nombre ?? string.Empty).ToUpperInvariant(), anchoDescripcion);
                if (descripcion.Count == 0)
                {
                    descripcion.Add("ARTICULO");
                }

                lineas.Add(
                    FormatearCantidad(detalle.Cantidad).PadRight(anchoCantidad)
                    + descripcion[0].PadRight(anchoDescripcion)
                    + detalle.Subtotal.ToString("C").PadLeft(anchoImporte));

                for (int i = 1; i < descripcion.Count; i++)
                {
                    lineas.Add(new string(' ', anchoCantidad) + descripcion[i]);
                }
            }

            return lineas;
        }

        private static List<string> CrearTablaMontos(List<TicketCorteRenglon> renglones, string mensajeVacio, int ancho)
        {
            if (renglones.Count == 0)
            {
                return new List<string> { mensajeVacio };
            }

            const int anchoMonto = 17;
            int anchoConcepto = Math.Max(6, ancho - anchoMonto - 1);

            List<string> lineas = new();
            foreach (TicketCorteRenglon renglon in renglones)
            {
                lineas.Add(
                    AjustarTexto(renglon.Concepto, anchoConcepto).PadRight(anchoConcepto)
                    + " "
                    + renglon.Monto.ToString("C").PadLeft(anchoMonto));
            }

            return lineas;
        }

        private static void AgregarLineasCentradas(List<string> lineas, IEnumerable<string> textos, int ancho)
        {
            foreach (string texto in textos)
            {
                lineas.Add(string.IsNullOrWhiteSpace(texto) ? string.Empty : Centrar(texto, ancho));
            }
        }

        /// <summary>
        /// Ticket de venta con datos de ejemplo usando la plantilla indicada.
        /// Genera las lineas con el mismo codigo que la impresion real.
        /// </summary>
        public static List<string> GenerarVistaPreviaVenta(TicketPlantilla plantilla)
        {
            TicketVenta demo = new TicketVenta
            {
                Folio = 1234,
                Fecha = DateTime.Now,
                Total = 385.50m,
                MetodoPago = "Efectivo",
                EfectivoRecibido = 400m,
                CambioEntregado = 14.50m,
                Vendedor = "CAJA 1"
            };
            demo.Detalles.Add(new TicketDetalle
            {
                Codigo = "7501031311309",
                Nombre = "Balata delantera",
                Cantidad = 1,
                PrecioUnitario = 245m,
                Subtotal = 245m
            });
            demo.Detalles.Add(new TicketDetalle
            {
                Codigo = "APV-1123",
                Nombre = "Aceite 5W-30 sintetico 1L",
                Cantidad = 2,
                PrecioUnitario = 70.25m,
                Subtotal = 140.50m
            });

            return CrearLineas(demo, esReimpresion: false, esProvisional: false, plantilla);
        }

        /// <summary>
        /// Ticket de corte de caja con datos de ejemplo usando la plantilla indicada.
        /// Genera las lineas con el mismo codigo que la impresion real del corte.
        /// </summary>
        public static List<string> GenerarVistaPreviaCorte(TicketPlantilla plantilla)
        {
            TicketCorteDatos demo = new TicketCorteDatos
            {
                TipoCorte = "DIA",
                Fecha = DateTime.Now,
                Usuario = "ADMIN",
                Caja = "CAJA 1",
                Periodo = $"{DateTime.Today:dd/MM/yyyy}-{DateTime.Today:dd/MM/yyyy}",
                VentasTotales = 12,
                TotalVendido = 4580.50m,
                PagosProveedores = 250m
            };
            demo.Pagos.Add(new TicketCorteRenglon("EFECTIVO", 3980.50m));
            demo.Pagos.Add(new TicketCorteRenglon("TARJETA", 600m));
            demo.Categorias.Add(new TicketCorteRenglon("FRENOS", 2450m));
            demo.Categorias.Add(new TicketCorteRenglon("LUBRICANTES", 2130.50m));

            return GenerarLineasCorte(demo, plantilla);
        }

        private static string GuardarTicket(int folio, List<string> lineas, string prefijo)
        {
            string carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tickets_RefaxManager");
            Directory.CreateDirectory(carpeta);

            string rutaArchivo = Path.Combine(carpeta, $"{prefijo}_{folio}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllLines(rutaArchivo, lineas);
            return rutaArchivo;
        }

        /// <summary>
        /// Impresoras instaladas excluyendo las virtuales (PDF, XPS, OneNote, Fax),
        /// que abren una ventana para guardar archivo en lugar de imprimir.
        /// </summary>
        public static List<string> ObtenerImpresorasFisicas()
        {
            List<string> impresoras = new();
            foreach (string impresora in PrinterSettings.InstalledPrinters)
            {
                if (!EsImpresoraVirtual(impresora))
                {
                    impresoras.Add(impresora);
                }
            }

            return impresoras;
        }

        private static bool EsImpresoraVirtual(string nombre)
        {
            string mayusculas = nombre.ToUpperInvariant();
            return mayusculas.Contains("PDF")
                || mayusculas.Contains("XPS")
                || mayusculas.Contains("ONENOTE")
                || mayusculas.Contains("FAX");
        }

        private static string? ResolverImpresoraFisica(string? impresora)
        {
            if (!string.IsNullOrWhiteSpace(impresora) && !EsImpresoraVirtual(impresora))
            {
                return impresora;
            }

            string impresoraDefault = new PrinterSettings().PrinterName;
            if (!string.IsNullOrWhiteSpace(impresoraDefault) && !EsImpresoraVirtual(impresoraDefault))
            {
                return impresoraDefault;
            }

            List<string> fisicas = ObtenerImpresorasFisicas();
            return fisicas.Count > 0 ? fisicas[0] : null;
        }

        public static void ImprimirTicket(List<string> lineasTicket, string? impresora = null, int anchoCaracteres = TicketPlantillaService.AnchoPredeterminado)
        {
            string? impresoraFinal = ResolverImpresoraFisica(impresora);
            if (impresoraFinal == null)
            {
                // Sin impresora fisica disponible: no se imprime para evitar la ventana de guardar archivo.
                return;
            }

            int lineaActual = 0;
            using (PrintDocument documento = new PrintDocument())
            {
                documento.PrinterSettings.PrinterName = impresoraFinal;

                ConfigurarPapelTermico(documento);
                float tamanoFuente = CalcularTamanoFuente(documento, anchoCaracteres);

                documento.PrintPage += (_, e) =>
                {
                    if (e.Graphics == null)
                    {
                        return;
                    }

                    using Font fuente = new Font("Courier New", tamanoFuente);
                    Brush brocha = Brushes.Black;
                    float altoLinea = fuente.GetHeight(e.Graphics) + 1;
                    float x = e.PageBounds.Left + e.PageSettings.Margins.Left;
                    float y = e.PageBounds.Top + e.PageSettings.Margins.Top;
                    float limiteInferior = e.PageBounds.Bottom - e.PageSettings.Margins.Bottom;

                    while (lineaActual < lineasTicket.Count)
                    {
                        if (y + altoLinea > limiteInferior)
                        {
                            e.HasMorePages = true;
                            return;
                        }

                        e.Graphics.DrawString(lineasTicket[lineaActual], fuente, brocha, x, y);
                        y += altoLinea;
                        lineaActual++;
                    }

                    e.HasMorePages = false;
                };

                documento.Print();
            }
        }

        private static void ConfigurarPapelTermico(PrintDocument documento)
        {
            int anchoMm = ObtenerAnchoTicketMm();
            int anchoPapel = anchoMm <= 58 ? 228 : 315;
            documento.DefaultPageSettings.PaperSize = new PaperSize($"Ticket {anchoMm}mm", anchoPapel, 1100);
            documento.DefaultPageSettings.Margins = new Margins(2, 2, 2, 2);
            documento.OriginAtMargins = false;
        }

        private static int ObtenerAnchoTicketMm()
        {
            string? valor = Environment.GetEnvironmentVariable("REFAXMANAGER_TICKET_WIDTH_MM");
            return int.TryParse(valor, out int ancho) && ancho <= 58 ? 58 : 80;
        }

        /// <summary>
        /// Con el ancho estandar (40 caracteres) se conserva la fuente de siempre;
        /// si el ticket se hace mas ancho, la fuente se reduce para que todas las
        /// columnas quepan en el papel fisico de la impresora.
        /// </summary>
        private static float CalcularTamanoFuente(PrintDocument documento, int anchoCaracteres)
        {
            const float fuenteEstandar = 7f;
            const float avanceCourier = 0.61f; // ancho de cada caracter en proporcion al tamano de la fuente

            int caracteres = TicketPlantillaService.LimitarAncho(anchoCaracteres);
            float anchoImprimiblePulgadas = Math.Max(1f, (documento.DefaultPageSettings.PaperSize.Width - 6) / 100f);
            float fuenteMaxima = anchoImprimiblePulgadas * 72f / (avanceCourier * caracteres);
            return Math.Min(fuenteEstandar, fuenteMaxima);
        }

        private static void RegistrarReimpresion(int ventaId, int usuarioId)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    INSERT INTO reimpresiones_ticket (venta_id, usuario_id)
                    VALUES (@ventaId, @usuarioId);";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@ventaId", ventaId);
                    cmd.Parameters.AddWithValue("@usuarioId", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void AsegurarTablaReimpresiones()
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    ALTER TABLE ventas
                        ADD COLUMN IF NOT EXISTS efectivo_recibido numeric(12, 2) NOT NULL DEFAULT 0,
                        ADD COLUMN IF NOT EXISTS cambio_entregado numeric(12, 2) NOT NULL DEFAULT 0;

                    CREATE TABLE IF NOT EXISTS reimpresiones_ticket (
                        id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        venta_id integer NOT NULL REFERENCES ventas(id) ON DELETE CASCADE,
                        usuario_id integer NULL REFERENCES usuarios(id) ON DELETE SET NULL,
                        fecha_reimpresion timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.ExecuteNonQuery();
                }
            }
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

        public static string Centrar(string texto, int ancho = TicketPlantillaService.AnchoPredeterminado)
        {
            texto = AjustarTexto(texto, ancho);
            int espacios = Math.Max(0, (ancho - texto.Length) / 2);
            return new string(' ', espacios) + texto;
        }

        private static string FormatearFechaTicket(DateTime fecha)
        {
            string periodo = fecha.Hour < 12 ? "AM" : "PM";
            return $"{fecha:dd/MM/yyyy hh:mm} {periodo}";
        }

        private static List<string> PartirTexto(string texto, int ancho)
        {
            List<string> partes = new();
            string pendiente = texto.Trim();

            while (pendiente.Length > ancho)
            {
                int corte = pendiente.LastIndexOf(' ', ancho);
                if (corte <= 0)
                {
                    corte = ancho;
                }

                partes.Add(pendiente.Substring(0, corte).Trim());
                pendiente = pendiente.Substring(corte).Trim();
            }

            if (pendiente.Length > 0)
            {
                partes.Add(pendiente);
            }

            return partes;
        }

        private static decimal ContarArticulos(TicketVenta ticket)
        {
            decimal total = 0;
            foreach (TicketDetalle detalle in ticket.Detalles)
            {
                total += detalle.Cantidad;
            }

            return total;
        }

        private static decimal ObtenerPagoCon(TicketVenta ticket)
        {
            return ticket.EfectivoRecibido > 0 ? ticket.EfectivoRecibido : ticket.Total;
        }

        private static string FormatearCantidad(decimal cantidad)
        {
            return cantidad % 1 == 0 ? cantidad.ToString("0") : cantidad.ToString("0.###");
        }
    }

    public class TicketVenta
    {
        public int VentaId { get; set; }
        public int Folio { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
        public decimal EfectivoRecibido { get; set; }
        public decimal CambioEntregado { get; set; }
        public string Vendedor { get; set; } = string.Empty;
        public List<TicketDetalle> Detalles { get; } = new();
    }

    public class TicketDetalle
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Subtotal { get; set; }
    }

    /// <summary>
    /// Datos ya calculados de un corte de caja, listos para rellenar la plantilla.
    /// </summary>
    public class TicketCorteDatos
    {
        public string TipoCorte { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Caja { get; set; } = string.Empty;
        public string Periodo { get; set; } = string.Empty;
        public int VentasTotales { get; set; }
        public decimal TotalVendido { get; set; }
        public decimal PagosProveedores { get; set; }
        public List<TicketCorteRenglon> Pagos { get; } = new();
        public List<TicketCorteRenglon> Categorias { get; } = new();
    }

    public sealed record TicketCorteRenglon(string Concepto, decimal Monto);
}

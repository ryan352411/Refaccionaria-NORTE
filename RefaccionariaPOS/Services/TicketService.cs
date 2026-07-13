using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;

namespace RefaccionariaPOS.Services
{
    public static class TicketService
    {
        private const int CaracteresTicket = 40;
        private const string TelefonoNegocio = "271 104 3233";

        public static string GenerarTicketVenta(int ventaId, bool imprimir, string? impresora = null, bool esReimpresion = false)
        {
            TicketVenta ticket = ObtenerTicket(ventaId);
            List<string> lineas = CrearLineas(ticket, esReimpresion);
            string rutaArchivo = GuardarTicket(ticket.Folio, lineas, esReimpresion ? "Reimpresion" : "Ticket");

            if (imprimir)
            {
                ImprimirTicket(lineas, impresora);
            }

            return rutaArchivo;
        }

        /// <summary>
        /// Genera un ticket a partir de datos en memoria (sin consultar la base).
        /// Se usa en modo offline: el folio real se asigna al sincronizar.
        /// </summary>
        public static string GenerarTicketLocal(TicketVenta ticket, bool imprimir, string? impresora = null)
        {
            List<string> lineas = CrearLineas(ticket, esReimpresion: false, esProvisional: true);
            string rutaArchivo = GuardarTicket(ticket.Folio, lineas, "TicketOffline");

            if (imprimir)
            {
                ImprimirTicket(lineas, impresora);
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

        private static List<string> CrearLineas(TicketVenta ticket, bool esReimpresion, bool esProvisional = false)
        {
            List<string> lineas = new();
            lineas.Add(Centrar("REFACCIONARIA"));
            lineas.Add(Centrar("NORTE"));
            lineas.Add(Centrar("AV.DIVICION DEL NORTE N.63"));
            lineas.Add(Centrar("COL. CENTRO"));
            lineas.Add(Centrar(TelefonoNegocio));
            lineas.Add(string.Empty);

            if (esReimpresion)
            {
                lineas.Add(Centrar("REIMPRESION"));
            }

            if (esProvisional)
            {
                lineas.Add(Centrar("VENTA SIN CONEXION"));
            }

            lineas.Add(Centrar(FormatearFechaTicket(ticket.Fecha)));
            lineas.Add(FilaCampo("CAJERO:", ticket.Vendedor));
            lineas.Add(FilaCampo("FOLIO:", esProvisional ? "PENDIENTE" : ticket.Folio.ToString()));
            lineas.Add(string.Empty);
            lineas.Add(FilaEncabezadoVenta());
            lineas.Add(SeparadorDoble());
            lineas.Add("==");

            foreach (TicketDetalle detalle in ticket.Detalles)
            {
                AgregarPartidaVenta(lineas, detalle);
            }

            lineas.Add(string.Empty);
            lineas.Add(Centrar("NO. DE ARTICULOS: " + FormatearCantidad(ContarArticulos(ticket))));
            lineas.Add(Centrar("TOTAL: " + ticket.Total.ToString("C")));
            lineas.Add(Centrar("PAGO CON: " + ObtenerPagoCon(ticket).ToString("C")));
            lineas.Add(Centrar("SU CAMBIO: " + ticket.CambioEntregado.ToString("C")));

            lineas.Add(string.Empty);
            lineas.Add(Centrar("GRACIAS POR SU COMPRA"));
            lineas.Add(Centrar("VUELVA PRONTO"));
            lineas.Add(string.Empty);
            lineas.Add(string.Empty);
            return lineas;
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

        public static void ImprimirTicket(List<string> lineasTicket, string? impresora = null)
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

                documento.PrintPage += (_, e) =>
                {
                    if (e.Graphics == null)
                    {
                        return;
                    }

                    using Font fuente = new Font("Courier New", 7);
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

        private static void AgregarTextoEnvuelto(List<string> lineas, string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                lineas.Add(string.Empty);
                return;
            }

            string pendiente = texto.Trim();
            while (pendiente.Length > CaracteresTicket)
            {
                int corte = pendiente.LastIndexOf(' ', CaracteresTicket);
                if (corte <= 0)
                {
                    corte = CaracteresTicket;
                }

                lineas.Add(pendiente.Substring(0, corte).Trim());
                pendiente = pendiente.Substring(corte).Trim();
            }

            if (pendiente.Length > 0)
            {
                lineas.Add(pendiente);
            }
        }

        private static string Separador()
        {
            return new string('-', CaracteresTicket);
        }

        private static string SeparadorDoble()
        {
            return new string('=', CaracteresTicket);
        }

        private static string Centrar(string texto)
        {
            texto = AjustarTexto(texto, CaracteresTicket);
            int espacios = Math.Max(0, (CaracteresTicket - texto.Length) / 2);
            return new string(' ', espacios) + texto;
        }

        private static string FormatearFechaTicket(DateTime fecha)
        {
            string periodo = fecha.Hour < 12 ? "AM" : "PM";
            return $"{fecha:dd/MM/yyyy hh:mm} {periodo}";
        }

        private static string FilaCampo(string etiqueta, string valor)
        {
            etiqueta = AjustarTexto(etiqueta.ToUpperInvariant(), 10);
            valor = AjustarTexto((valor ?? string.Empty).ToUpperInvariant(), CaracteresTicket - 12);
            int espacios = Math.Max(1, CaracteresTicket - etiqueta.Length - valor.Length);
            return etiqueta + new string(' ', espacios) + valor;
        }

        private static string FilaEncabezadoVenta()
        {
            return string.Format(
                "{0,-5}{1,-24}{2,11}",
                "CANT.",
                "DESCRIPCION",
                "IMPORTE");
        }

        private static void AgregarPartidaVenta(List<string> lineas, TicketDetalle detalle)
        {
            const int anchoDescripcion = 24;

            List<string> descripcion = PartirTexto((detalle.Nombre ?? string.Empty).ToUpperInvariant(), anchoDescripcion);
            if (descripcion.Count == 0)
            {
                descripcion.Add("ARTICULO");
            }

            lineas.Add(string.Format(
                "{0,-5}{1,-24}{2,11}",
                FormatearCantidad(detalle.Cantidad),
                descripcion[0],
                detalle.Subtotal.ToString("C")));

            for (int i = 1; i < descripcion.Count; i++)
            {
                lineas.Add(string.Format(
                    "{0,-5}{1,-24}{2,11}",
                    string.Empty,
                    descripcion[i],
                    string.Empty));
            }
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

        private static string FilaImporte(string etiqueta, decimal importe)
        {
            string total = importe.ToString("C");
            int espacios = Math.Max(1, CaracteresTicket - etiqueta.Length - total.Length);
            return etiqueta + new string(' ', espacios) + total;
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
}

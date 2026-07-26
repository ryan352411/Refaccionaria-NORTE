using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows;

namespace RefaccionariaPOS.Views
{
    public partial class CorteCajaView : Window
    {
        private CortePeriodoResumen resumenDia = new();
        private CortePeriodoResumen resumenSemana = new();
        private CortePeriodoResumen resumenMes = new();
        private List<CorteOrigenResumen> resumenOrigenDia = new();
        private readonly int idUsuarioCorte;
        private readonly string usuarioCorte;
        private readonly string cajaActual;
        private DateTime inicioDia;
        private DateTime finDia;
        private DateTime inicioSemana;
        private DateTime finSemana;
        private DateTime inicioMes;
        private DateTime finMes;
        private int anchoTicketCorte = TicketPlantillaService.AnchoPredeterminado;

        public CorteCajaView(int idUsuario = 0, string usuario = "")
        {
            InitializeComponent();
            idUsuarioCorte = idUsuario;
            usuarioCorte = string.IsNullOrWhiteSpace(usuario) ? "Sin usuario" : usuario.Trim();
            cajaActual = ObtenerCajaActual();
            CargarCorte();
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            CargarCorte();
        }

        private void BtnImprimirDia_Click(object sender, RoutedEventArgs e)
        {
            ImprimirCortePeriodo("DIA", inicioDia, finDia, resumenDia);
        }

        private void BtnImprimirSemana_Click(object sender, RoutedEventArgs e)
        {
            ImprimirCortePeriodo("SEMANA", inicioSemana, finSemana, resumenSemana);
        }

        private void BtnImprimirMes_Click(object sender, RoutedEventArgs e)
        {
            ImprimirCortePeriodo("MES", inicioMes, finMes, resumenMes);
        }

        private void CargarCorte()
        {
            try
            {
                DateTime hoy = DateTime.Today;
                int diasDesdeLunes = ((int)hoy.DayOfWeek + 6) % 7;

                inicioDia = hoy;
                finDia = hoy.AddDays(1);
                inicioSemana = hoy.AddDays(-diasDesdeLunes);
                finSemana = inicioSemana.AddDays(7);
                inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
                finMes = inicioMes.AddMonths(1);

                resumenDia = ObtenerResumen(inicioDia, finDia);
                resumenSemana = ObtenerResumen(inicioSemana, finSemana);
                resumenMes = ObtenerResumen(inicioMes, finMes);
                resumenOrigenDia = ObtenerResumenPorOrigen(inicioDia, finDia);

                PintarResumenDia(resumenDia);
                PintarResumenSemana(resumenSemana);
                PintarResumenMes(resumenMes);

                dgOrigenes.ItemsSource = resumenOrigenDia;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar corte de caja: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private CortePeriodoResumen ObtenerResumen(DateTime inicio, DateTime fin)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    WITH costo_por_venta AS (
                        SELECT dv.venta_id, SUM(dv.cantidad * p.costo_proveedor) AS inversion
                        FROM detalles_venta dv
                        INNER JOIN productos p ON p.id = dv.producto_id
                        GROUP BY dv.venta_id
                    )
                    SELECT COUNT(v.id) AS tickets,
                           COALESCE(SUM(v.total), 0) AS ventas,
                           COALESCE(SUM(c.inversion), 0) AS inversion
                    FROM ventas v
                    LEFT JOIN costo_por_venta c ON c.venta_id = v.id
                    WHERE v.fecha_venta >= @inicio
                      AND v.fecha_venta < @fin;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@inicio", inicio);
                    cmd.Parameters.AddWithValue("@fin", fin);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new CortePeriodoResumen
                            {
                                Tickets = Convert.ToInt32(reader["tickets"]),
                                Ventas = Convert.ToDecimal(reader["ventas"]),
                                Inversion = Convert.ToDecimal(reader["inversion"])
                            };
                        }
                    }
                }
            }

            return new CortePeriodoResumen();
        }

        private List<CorteOrigenResumen> ObtenerResumenPorOrigen(DateTime inicio, DateTime fin)
        {
            List<CorteOrigenResumen> resumenes = new();
            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    WITH costo_por_venta AS (
                        SELECT dv.venta_id, SUM(dv.cantidad * p.costo_proveedor) AS inversion
                        FROM detalles_venta dv
                        INNER JOIN productos p ON p.id = dv.producto_id
                        GROUP BY dv.venta_id
                    ),
                    ventas_clasificadas AS (
                        SELECT v.id,
                               v.total,
                               COALESCE(c.inversion, 0) AS inversion,
                               COALESCE(NULLIF(v.metodo_pago, ''), 'Sin metodo') AS origen
                        FROM ventas v
                        LEFT JOIN costo_por_venta c ON c.venta_id = v.id
                        WHERE v.fecha_venta >= @inicio
                          AND v.fecha_venta < @fin
                    )
                    SELECT origen,
                           COUNT(*) AS tickets,
                           COALESCE(SUM(total), 0) AS ventas,
                           COALESCE(SUM(inversion), 0) AS inversion
                    FROM ventas_clasificadas
                    GROUP BY origen
                    ORDER BY origen;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@inicio", inicio);
                    cmd.Parameters.AddWithValue("@fin", fin);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            resumenes.Add(new CorteOrigenResumen
                            {
                                Origen = reader["origen"].ToString() ?? "Sin metodo",
                                Tickets = Convert.ToInt32(reader["tickets"]),
                                Ventas = Convert.ToDecimal(reader["ventas"]),
                                Inversion = Convert.ToDecimal(reader["inversion"])
                            });
                        }
                    }
                }
            }

            return resumenes;
        }

        private void PintarResumenDia(CortePeriodoResumen resumen)
        {
            lblHoyVentas.Text = resumen.Ventas.ToString("C");
            lblHoyInversion.Text = "Inversion: " + resumen.Inversion.ToString("C");
            lblHoyUtilidad.Text = "Utilidad: " + resumen.Utilidad.ToString("C");
            lblHoyTickets.Text = $"{resumen.Tickets} tickets";
        }

        private void PintarResumenSemana(CortePeriodoResumen resumen)
        {
            lblSemanaVentas.Text = resumen.Ventas.ToString("C");
            lblSemanaInversion.Text = "Inversion: " + resumen.Inversion.ToString("C");
            lblSemanaUtilidad.Text = "Utilidad: " + resumen.Utilidad.ToString("C");
            lblSemanaTickets.Text = $"{resumen.Tickets} tickets";
        }

        private void PintarResumenMes(CortePeriodoResumen resumen)
        {
            lblMesVentas.Text = resumen.Ventas.ToString("C");
            lblMesInversion.Text = "Inversion: " + resumen.Inversion.ToString("C");
            lblMesUtilidad.Text = "Utilidad: " + resumen.Utilidad.ToString("C");
            lblMesTickets.Text = $"{resumen.Tickets} tickets";
        }

        private void ImprimirCortePeriodo(string tituloPeriodo, DateTime inicio, DateTime fin, CortePeriodoResumen resumen)
        {
            try
            {
                CargarCorte();
                CortePeriodoResumen resumenActualizado = tituloPeriodo switch
                {
                    "DIA" => resumenDia,
                    "SEMANA" => resumenSemana,
                    "MES" => resumenMes,
                    _ => resumen
                };
                List<CorteOrigenResumen> desglosePagos = ObtenerResumenPorOrigen(inicio, fin);
                List<CorteCategoriaResumen> desgloseCategorias = ObtenerResumenPorCategoria(inicio, fin);
                decimal pagosProveedores = ObtenerPagosProveedores(inicio, fin);
                List<string> lineas = CrearLineasTicketCorte(
                    tituloPeriodo,
                    inicio,
                    fin,
                    resumenActualizado,
                    desglosePagos,
                    desgloseCategorias,
                    pagosProveedores);
                GuardarTicketCorte(tituloPeriodo, lineas);
                TicketService.ImprimirTicket(lineas, anchoCaracteres: anchoTicketCorte);
                MessageBox.Show("Ticket de corte enviado a la impresora.", "Corte de caja", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo imprimir el corte de caja: " + ex.Message, "Corte de caja", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<CorteCategoriaResumen> ObtenerResumenPorCategoria(DateTime inicio, DateTime fin)
        {
            List<CorteCategoriaResumen> resumenes = new();
            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    WITH ventas_por_categoria AS (
                        SELECT CASE
                                   WHEN p.id IS NULL THEN COALESCE(NULLIF(dv.tipo_articulo, ''), 'Articulos comunes')
                                   ELSE COALESCE(NULLIF(p.categoria, ''), 'General')
                               END AS categoria,
                               dv.subtotal
                        FROM detalles_venta dv
                        INNER JOIN ventas v ON v.id = dv.venta_id
                        LEFT JOIN productos p ON p.id = dv.producto_id
                        WHERE v.fecha_venta >= @inicio
                          AND v.fecha_venta < @fin
                    )
                    SELECT categoria,
                           COALESCE(SUM(subtotal), 0) AS total
                    FROM ventas_por_categoria
                    GROUP BY categoria
                    ORDER BY categoria;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@inicio", inicio);
                    cmd.Parameters.AddWithValue("@fin", fin);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            resumenes.Add(new CorteCategoriaResumen
                            {
                                Categoria = reader["categoria"].ToString() ?? "General",
                                Total = Convert.ToDecimal(reader["total"])
                            });
                        }
                    }
                }
            }

            return resumenes;
        }

        private decimal ObtenerPagosProveedores(DateTime inicio, DateTime fin)
        {
            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                string? tabla = ObtenerPrimeraTablaExistente(conexion, "pagos_proveedores", "pagos_a_proveedores", "proveedor_pagos");
                if (tabla == null)
                {
                    return 0;
                }

                string? columnaMonto = ObtenerPrimeraColumnaExistente(conexion, tabla, "monto", "total", "importe", "cantidad");
                string? columnaFecha = ObtenerPrimeraColumnaExistente(conexion, tabla, "fecha_pago", "fecha", "created_at", "fecha_registro");
                if (columnaMonto == null || columnaFecha == null)
                {
                    return 0;
                }

                string query = $@"
                    SELECT COALESCE(SUM({QuoteIdentifier(columnaMonto)}), 0) AS total
                    FROM {QuoteIdentifier(tabla)}
                    WHERE {QuoteIdentifier(columnaFecha)} >= @inicio
                      AND {QuoteIdentifier(columnaFecha)} < @fin;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@inicio", inicio);
                    cmd.Parameters.AddWithValue("@fin", fin);
                    object? resultado = cmd.ExecuteScalar();
                    return resultado == null || resultado == DBNull.Value ? 0 : Convert.ToDecimal(resultado);
                }
            }
        }

        private static string? ObtenerPrimeraTablaExistente(NpgsqlConnection conexion, params string[] tablas)
        {
            const string query = @"
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name = ANY(@tablas)
                LIMIT 1;";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
            {
                cmd.Parameters.AddWithValue("@tablas", tablas);
                object? resultado = cmd.ExecuteScalar();
                return resultado?.ToString();
            }
        }

        private static string? ObtenerPrimeraColumnaExistente(NpgsqlConnection conexion, string tabla, params string[] columnas)
        {
            const string query = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @tabla
                  AND column_name = ANY(@columnas)
                ORDER BY array_position(@columnas, column_name)
                LIMIT 1;";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
            {
                cmd.Parameters.AddWithValue("@tabla", tabla);
                cmd.Parameters.AddWithValue("@columnas", columnas);
                object? resultado = cmd.ExecuteScalar();
                return resultado?.ToString();
            }
        }

        private List<string> CrearLineasTicketCorte(
            string tituloPeriodo,
            DateTime inicio,
            DateTime fin,
            CortePeriodoResumen resumen,
            List<CorteOrigenResumen> desglosePagos,
            List<CorteCategoriaResumen> desgloseCategorias,
            decimal pagosProveedores)
        {
            TicketPlantilla plantilla = TicketPlantillaService.ObtenerPlantilla(TicketPlantillaService.TipoCorte);
            anchoTicketCorte = plantilla.AnchoCaracteres;

            TicketCorteDatos datos = new TicketCorteDatos
            {
                TipoCorte = tituloPeriodo,
                Fecha = DateTime.Now,
                Usuario = usuarioCorte,
                Caja = cajaActual,
                Periodo = $"{inicio:dd/MM/yyyy}-{fin.AddDays(-1):dd/MM/yyyy}",
                VentasTotales = resumen.Tickets,
                TotalVendido = resumen.Ventas,
                PagosProveedores = pagosProveedores
            };

            foreach (CorteOrigenResumen origen in desglosePagos)
            {
                datos.Pagos.Add(new TicketCorteRenglon(origen.Origen, origen.Ventas));
            }

            foreach (CorteCategoriaResumen categoria in desgloseCategorias)
            {
                datos.Categorias.Add(new TicketCorteRenglon(categoria.Categoria, categoria.Total));
            }

            return TicketService.GenerarLineasCorte(datos, plantilla);
        }

        private static string ObtenerCajaActual()
        {
            string? caja = Environment.GetEnvironmentVariable("REFAXMANAGER_CAJA")
                ?? Environment.GetEnvironmentVariable("REFACCIONARIA_CAJA");
            return string.IsNullOrWhiteSpace(caja) ? "Caja 1" : caja.Trim();
        }

        private static string QuoteIdentifier(string valor)
        {
            return "\"" + valor.Replace("\"", "\"\"") + "\"";
        }

        private static void GuardarTicketCorte(string periodo, List<string> lineas)
        {
            string carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tickets_RefaxManager");
            Directory.CreateDirectory(carpeta);
            string archivo = Path.Combine(carpeta, $"Corte_{periodo}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllLines(archivo, lineas);
        }

        private static void ImprimirTicketCorte(List<string> lineas)
        {
            int lineaActual = 0;
            using (PrintDocument documento = new PrintDocument())
            {
                documento.PrintPage += (_, e) =>
                {
                    if (e.Graphics == null)
                    {
                        return;
                    }

                    using Font fuente = new Font("Courier New", 8);
                    Brush brocha = Brushes.Black;
                    float altoLinea = fuente.GetHeight(e.Graphics) + 2;
                    float x = e.MarginBounds.Left;
                    float y = e.MarginBounds.Top;

                    while (lineaActual < lineas.Count)
                    {
                        if (y + altoLinea > e.MarginBounds.Bottom)
                        {
                            e.HasMorePages = true;
                            return;
                        }

                        e.Graphics.DrawString(lineas[lineaActual], fuente, brocha, x, y);
                        y += altoLinea;
                        lineaActual++;
                    }

                    e.HasMorePages = false;
                };

                documento.Print();
            }
        }
    }

    public class CortePeriodoResumen
    {
        public int Tickets { get; set; }
        public decimal Ventas { get; set; }
        public decimal Inversion { get; set; }
        public decimal Utilidad => Ventas - Inversion;
    }

    public class CorteOrigenResumen : CortePeriodoResumen
    {
        public string Origen { get; set; } = string.Empty;
    }

    public class CorteCategoriaResumen
    {
        public string Categoria { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }
}

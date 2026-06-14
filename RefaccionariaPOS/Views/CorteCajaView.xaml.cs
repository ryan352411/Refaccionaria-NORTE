using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.Windows;

namespace RefaccionariaPOS.Views
{
    public partial class CorteCajaView : Window
    {
        public CorteCajaView()
        {
            InitializeComponent();
            CargarCorte();
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            CargarCorte();
        }

        private void CargarCorte()
        {
            try
            {
                DateTime hoy = DateTime.Today;
                DateTime inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
                DateTime inicioAnio = new DateTime(hoy.Year, 1, 1);

                CortePeriodoResumen resumenHoy = ObtenerResumen(hoy, hoy.AddDays(1));
                CortePeriodoResumen resumenMes = ObtenerResumen(inicioMes, inicioMes.AddMonths(1));
                CortePeriodoResumen resumenAnio = ObtenerResumen(inicioAnio, inicioAnio.AddYears(1));

                PintarResumen("Hoy", resumenHoy);
                PintarResumen("Mes", resumenMes);
                PintarResumen("Anio", resumenAnio);

                dgOrigenes.ItemsSource = ObtenerResumenPorOrigen(hoy, hoy.AddDays(1));
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

                string query = @"
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
            List<CorteOrigenResumen> resumenes = new List<CorteOrigenResumen>();
            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                string query = @"
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
                               COALESCE(NULLIF(v.metodo_pago, ''), 'Mostrador') AS origen
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
                                Origen = reader["origen"].ToString() ?? "Sin clasificar",
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

        private void PintarResumen(string periodo, CortePeriodoResumen resumen)
        {
            if (periodo == "Hoy")
            {
                lblHoyVentas.Text = resumen.Ventas.ToString("C");
                lblHoyInversion.Text = "Inversión: " + resumen.Inversion.ToString("C");
                lblHoyUtilidad.Text = "Utilidad: " + resumen.Utilidad.ToString("C");
                lblHoyTickets.Text = $"{resumen.Tickets} tickets";
                return;
            }

            if (periodo == "Mes")
            {
                lblMesVentas.Text = resumen.Ventas.ToString("C");
                lblMesInversion.Text = "Inversión: " + resumen.Inversion.ToString("C");
                lblMesUtilidad.Text = "Utilidad: " + resumen.Utilidad.ToString("C");
                lblMesTickets.Text = $"{resumen.Tickets} tickets";
                return;
            }

            lblAnioVentas.Text = resumen.Ventas.ToString("C");
            lblAnioInversion.Text = "Inversión: " + resumen.Inversion.ToString("C");
            lblAnioUtilidad.Text = "Utilidad: " + resumen.Utilidad.ToString("C");
            lblAnioTickets.Text = $"{resumen.Tickets} tickets";
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
}

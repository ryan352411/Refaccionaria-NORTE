using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class ReimprimirTicketsView : Window
    {
        private readonly int usuarioId;

        public ReimprimirTicketsView(int usuarioId)
        {
            InitializeComponent();
            this.usuarioId = usuarioId;
            CargarVentas();
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            CargarVentas(txtBuscar.Text.Trim());
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            CargarVentas(txtBuscar.Text.Trim());
        }

        private void BtnReimprimir_Click(object sender, RoutedEventArgs e)
        {
            if (dgVentas.SelectedItem is not Venta venta)
            {
                MessageBox.Show("Selecciona una venta para reimprimir.", "Reimprimir", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string ruta = TicketService.ReimprimirTicket(venta.Id, usuarioId);
                lblEstado.Text = "Ticket reimpreso y guardado en: " + ruta;
                MessageBox.Show("Ticket enviado a la impresora.", "Reimprimir", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo reimprimir el ticket: " + ex.Message, "Reimprimir", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CargarVentas(string busqueda = "")
        {
            List<Venta> ventas = new();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        SELECT v.id, v.folio, v.fecha_venta, v.total, v.estado, v.metodo_pago,
                               COALESCE(u.username, 'Sin usuario') AS vendedor
                        FROM ventas v
                        LEFT JOIN usuarios u ON u.id = v.usuario_id
                        WHERE CAST(v.folio AS text) ILIKE @busqueda
                           OR COALESCE(u.username, '') ILIKE @busqueda
                           OR COALESCE(v.metodo_pago, '') ILIKE @busqueda
                           OR COALESCE(v.estado, '') ILIKE @busqueda
                        ORDER BY v.fecha_venta DESC
                        LIMIT 200;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@busqueda", "%" + busqueda + "%");

                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ventas.Add(new Venta
                                {
                                    Id = Convert.ToInt32(reader["id"]),
                                    Folio = Convert.ToInt32(reader["folio"]),
                                    Fecha = Convert.ToDateTime(reader["fecha_venta"]),
                                    Total = Convert.ToDecimal(reader["total"]),
                                    Estado = reader["estado"].ToString() ?? "Completada",
                                    MetodoPago = reader["metodo_pago"].ToString() ?? "Mostrador",
                                    Vendedor = reader["vendedor"].ToString() ?? "Sin usuario"
                                });
                            }
                        }
                    }
                }

                dgVentas.ItemsSource = ventas;
                lblEstado.Text = $"{ventas.Count} ventas encontradas.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudieron cargar las ventas: " + ex.Message, "Reimprimir", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

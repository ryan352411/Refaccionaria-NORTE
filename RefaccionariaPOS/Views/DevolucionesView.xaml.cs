using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class DevolucionesView : Window
    {
        private readonly int usuarioId;

        public DevolucionesView(int usuarioId)
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

        private void BtnRegistrarDevolucion_Click(object sender, RoutedEventArgs e)
        {
            if (dgVentas.SelectedItem is not Venta venta)
            {
                MessageBox.Show("Selecciona una venta para devolver.", "Devoluciones", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (venta.Estado.Equals("Devuelta", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Esta venta ya fue marcada como devuelta.", "Devoluciones", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? motivo = PedirMotivoDevolucion(venta);
            if (motivo == null)
            {
                return;
            }

            MessageBoxResult confirmacion = MessageBox.Show(
                $"Se devolvera la venta folio {venta.Folio} por {venta.Total:C} y se restaurara el inventario disponible.\n\nContinuar?",
                "Confirmar devolucion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmacion != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                RegistrarDevolucionCompleta(venta.Id, motivo);
                lblEstado.Text = $"Devolucion registrada para folio {venta.Folio}.";
                CargarVentas(txtBuscar.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo registrar la devolucion: " + ex.Message, "Devoluciones", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        LIMIT 250;";

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
                MessageBox.Show("No se pudieron cargar las ventas: " + ex.Message, "Devoluciones", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegistrarDevolucionCompleta(int ventaId, string motivo)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();
                using (NpgsqlTransaction transaction = conexion.BeginTransaction())
                {
                    decimal totalDevuelto = ObtenerTotalVenta(conexion, transaction, ventaId);
                    int devolucionId = InsertarDevolucion(conexion, transaction, ventaId, motivo, totalDevuelto);
                    List<DetalleDevolucionItem> detalles = ObtenerDetallesVenta(conexion, transaction, ventaId);

                    foreach (DetalleDevolucionItem detalle in detalles)
                    {
                        InsertarDetalleDevolucion(conexion, transaction, devolucionId, detalle);
                        if (detalle.ProductoId.HasValue)
                        {
                            RestaurarStock(conexion, transaction, detalle);
                        }
                    }

                    using (NpgsqlCommand cmd = new NpgsqlCommand("UPDATE ventas SET estado = 'Devuelta' WHERE id = @ventaId;", conexion, transaction))
                    {
                        cmd.Parameters.AddWithValue("@ventaId", ventaId);
                        cmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }

        private int InsertarDevolucion(NpgsqlConnection conexion, NpgsqlTransaction transaction, int ventaId, string motivo, decimal totalDevuelto)
        {
            const string query = @"
                INSERT INTO devoluciones (venta_id, usuario_id, motivo, total_devuelto)
                VALUES (@ventaId, @usuarioId, @motivo, @totalDevuelto)
                RETURNING id;";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion, transaction))
            {
                cmd.Parameters.AddWithValue("@ventaId", ventaId);
                cmd.Parameters.AddWithValue("@usuarioId", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                cmd.Parameters.AddWithValue("@motivo", motivo);
                cmd.Parameters.AddWithValue("@totalDevuelto", totalDevuelto);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static List<DetalleDevolucionItem> ObtenerDetallesVenta(NpgsqlConnection conexion, NpgsqlTransaction transaction, int ventaId)
        {
            const string query = @"
                SELECT dv.id,
                       dv.producto_id,
                       COALESCE(NULLIF(dv.descripcion_manual, ''), p.nombre, '') AS descripcion,
                       dv.cantidad,
                       dv.precio_unitario,
                       dv.subtotal
                FROM detalles_venta dv
                LEFT JOIN productos p ON p.id = dv.producto_id
                WHERE dv.venta_id = @ventaId;";

            List<DetalleDevolucionItem> detalles = new();
            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion, transaction))
            {
                cmd.Parameters.AddWithValue("@ventaId", ventaId);
                using (NpgsqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        detalles.Add(new DetalleDevolucionItem
                        {
                            DetalleVentaId = Convert.ToInt32(reader["id"]),
                            ProductoId = reader["producto_id"] == DBNull.Value ? null : Convert.ToInt32(reader["producto_id"]),
                            Descripcion = reader["descripcion"].ToString() ?? string.Empty,
                            Cantidad = Convert.ToDecimal(reader["cantidad"]),
                            PrecioUnitario = Convert.ToDecimal(reader["precio_unitario"]),
                            Subtotal = Convert.ToDecimal(reader["subtotal"])
                        });
                    }
                }
            }

            return detalles;
        }

        private static void InsertarDetalleDevolucion(NpgsqlConnection conexion, NpgsqlTransaction transaction, int devolucionId, DetalleDevolucionItem detalle)
        {
            const string query = @"
                INSERT INTO detalles_devolucion
                    (devolucion_id, detalle_venta_id, producto_id, descripcion_manual, cantidad, precio_unitario, subtotal)
                VALUES
                    (@devolucionId, @detalleVentaId, @productoId, @descripcion, @cantidad, @precioUnitario, @subtotal);";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion, transaction))
            {
                cmd.Parameters.AddWithValue("@devolucionId", devolucionId);
                cmd.Parameters.AddWithValue("@detalleVentaId", detalle.DetalleVentaId);
                cmd.Parameters.AddWithValue("@productoId", detalle.ProductoId.HasValue ? (object)detalle.ProductoId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@descripcion", detalle.Descripcion);
                cmd.Parameters.AddWithValue("@cantidad", detalle.Cantidad);
                cmd.Parameters.AddWithValue("@precioUnitario", detalle.PrecioUnitario);
                cmd.Parameters.AddWithValue("@subtotal", detalle.Subtotal);
                cmd.ExecuteNonQuery();
            }
        }

        private static void RestaurarStock(NpgsqlConnection conexion, NpgsqlTransaction transaction, DetalleDevolucionItem detalle)
        {
            using (NpgsqlCommand cmd = new NpgsqlCommand("UPDATE productos SET stock_actual = stock_actual + @cantidad WHERE id = @productoId;", conexion, transaction))
            {
                cmd.Parameters.AddWithValue("@cantidad", detalle.Cantidad);
                cmd.Parameters.AddWithValue("@productoId", detalle.ProductoId!.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private static decimal ObtenerTotalVenta(NpgsqlConnection conexion, NpgsqlTransaction transaction, int ventaId)
        {
            using (NpgsqlCommand cmd = new NpgsqlCommand("SELECT total FROM ventas WHERE id = @ventaId FOR UPDATE;", conexion, transaction))
            {
                cmd.Parameters.AddWithValue("@ventaId", ventaId);
                object? resultado = cmd.ExecuteScalar();
                return resultado == null
                    ? throw new InvalidOperationException("No se encontro la venta.")
                    : Convert.ToDecimal(resultado);
            }
        }

        private static string? PedirMotivoDevolucion(Venta venta)
        {
            Window ventana = new Window
            {
                Title = "Motivo de devolucion",
                Width = 440,
                Height = 310,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            TextBox txtMotivo = new TextBox
            {
                Height = 96,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Margin = new Thickness(0, 8, 0, 16)
            };

            Button btnAceptar = new Button
            {
                Content = "Registrar devolucion",
                Width = 190,
                Height = 38,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = $"Venta folio {venta.Folio} - {venta.Total:C}", FontWeight = FontWeights.Bold });
            panel.Children.Add(new TextBlock { Text = "Motivo", Margin = new Thickness(0, 12, 0, 0) });
            panel.Children.Add(txtMotivo);
            panel.Children.Add(btnAceptar);
            ventana.Content = panel;

            btnAceptar.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(txtMotivo.Text))
                {
                    MessageBox.Show("Escribe el motivo de la devolucion.", "Devoluciones", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ventana.DialogResult = true;
            };

            txtMotivo.Focus();
            return ventana.ShowDialog() == true ? txtMotivo.Text.Trim() : null;
        }

    }

    public class DetalleDevolucionItem
    {
        public int DetalleVentaId { get; set; }
        public int? ProductoId { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Subtotal { get; set; }
    }
}

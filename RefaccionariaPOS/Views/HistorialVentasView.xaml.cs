using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;

namespace RefaccionariaPOS.Views
{
    public partial class HistorialVentasView : Window
    {
        public HistorialVentasView(int usuarioId = 0, bool puedeRegistrarDevoluciones = false)
        {
            InitializeComponent();
            CargarHistorial();
        }

        private void CargarHistorial()
        {
            List<Venta> listaVentas = new List<Venta>();

            try
            {
                DatabaseConnection db = new DatabaseConnection();

                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    string query = @"
                        SELECT v.id, v.folio, v.fecha_venta, v.total, v.estado, v.metodo_pago,
                               COALESCE(u.username, 'Sin usuario') AS vendedor
                        FROM ventas v
                        LEFT JOIN usuarios u ON u.id = v.usuario_id
                        ORDER BY v.fecha_venta DESC";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Venta v = new Venta
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Folio = Convert.ToInt32(reader["folio"]),
                                Fecha = Convert.ToDateTime(reader["fecha_venta"]),
                                Total = Convert.ToDecimal(reader["total"]),
                                Estado = reader["estado"] == DBNull.Value ? "Completada" : reader["estado"].ToString() ?? "Completada",
                                MetodoPago = reader["metodo_pago"] == DBNull.Value ? "Mostrador" : reader["metodo_pago"].ToString() ?? "Mostrador",
                                Vendedor = reader["vendedor"].ToString() ?? "Sin usuario"
                            };

                            listaVentas.Add(v);
                        }
                    }
                }

                dgHistorial.ItemsSource = listaVentas;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar el historial: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DgHistorial_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? dep = e.OriginalSource as DependencyObject;

            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridRow row)
            {
                row.IsSelected = true;
                dgHistorial.SelectedItem = row.Item;
            }
        }

        private void MenuVerDetalles_Click(object sender, RoutedEventArgs e)
        {
            if (dgHistorial.SelectedItem is not Venta ventaSeleccionada)
            {
                MessageBox.Show("Por favor, haz clic derecho sobre una fila valida.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int ventaId = ventaSeleccionada.Id;
            StringBuilder detalleTexto = new StringBuilder();
            DatabaseConnection db = new DatabaseConnection();

            try
            {
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    string query = @"
                        SELECT dv.cantidad,
                               dv.precio_unitario,
                               dv.subtotal,
                               COALESCE(NULLIF(dv.descripcion_manual, ''), p.nombre, 'Producto eliminado') AS producto_nombre
                        FROM detalles_venta dv
                        LEFT JOIN productos p ON dv.producto_id = p.id
                        WHERE dv.venta_id = @ventaId
                        ORDER BY dv.id";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@ventaId", ventaId);

                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            detalleTexto.AppendLine("--- DETALLE DE LA VENTA ---");
                            detalleTexto.AppendLine("ID: " + ventaSeleccionada.Id);
                            detalleTexto.AppendLine("Folio: " + ventaSeleccionada.Folio);
                            detalleTexto.AppendLine("Fecha: " + ventaSeleccionada.Fecha.ToString("dd/MM/yyyy HH:mm"));
                            detalleTexto.AppendLine("Vendedor: " + ventaSeleccionada.Vendedor);
                            detalleTexto.AppendLine("Origen: " + ventaSeleccionada.Origen);
                            detalleTexto.AppendLine("Metodo/Canal: " + ventaSeleccionada.MetodoPago);
                            detalleTexto.AppendLine();
                            detalleTexto.AppendLine(string.Format("{0,-30} | {1,-8} | {2,-10} | {3,-10}", "Producto", "Cant.", "P. Unit", "Subtotal"));
                            detalleTexto.AppendLine(new string('-', 68));

                            bool tieneArticulos = false;

                            while (reader.Read())
                            {
                                tieneArticulos = true;

                                string nombreProd = reader["producto_nombre"].ToString() ?? string.Empty;
                                decimal cantidad = Convert.ToDecimal(reader["cantidad"]);
                                decimal precioUnit = Convert.ToDecimal(reader["precio_unitario"]);
                                decimal subtotal = Convert.ToDecimal(reader["subtotal"]);

                                if (nombreProd.Length > 28)
                                {
                                    nombreProd = nombreProd.Substring(0, 25) + "...";
                                }

                                detalleTexto.AppendLine(string.Format("{0,-30} | {1,-8} | {2,-10:C} | {3,-10:C}", nombreProd, FormatearCantidad(cantidad), precioUnit, subtotal));
                            }

                            if (!tieneArticulos)
                            {
                                detalleTexto.AppendLine("No se encontraron articulos registrados para esta venta.");
                            }
                            else
                            {
                                detalleTexto.AppendLine(new string('-', 68));
                                detalleTexto.AppendLine("TOTAL DE LA VENTA: " + ventaSeleccionada.Total.ToString("C"));
                            }
                        }
                    }
                }

                MostrarDetalleVenta(detalleTexto.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al recuperar los detalles de los productos: " + ex.Message, "Error de Lectura", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MostrarDetalleVenta(string detalle)
        {
            Window ventanaDetalle = new Window
            {
                Title = "Articulos Vendidos",
                Owner = Application.Current.MainWindow,
                Width = 760,
                Height = 520,
                MinWidth = 620,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White
            };

            Grid contenedor = new Grid { Margin = new Thickness(16) };
            contenedor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            contenedor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBox textoDetalle = new TextBox
            {
                Text = detalle,
                IsReadOnly = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10)
            };

            Button btnCerrar = new Button
            {
                Content = "Cerrar",
                Width = 110,
                Height = 34,
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnCerrar.Click += (_, _) => ventanaDetalle.Close();

            Grid.SetRow(textoDetalle, 0);
            Grid.SetRow(btnCerrar, 1);
            contenedor.Children.Add(textoDetalle);
            contenedor.Children.Add(btnCerrar);

            ventanaDetalle.Content = contenedor;
            ventanaDetalle.ShowDialog();
        }

        private static string FormatearCantidad(decimal cantidad)
        {
            return cantidad % 1 == 0 ? cantidad.ToString("0") : cantidad.ToString("0.###");
        }
    }
}

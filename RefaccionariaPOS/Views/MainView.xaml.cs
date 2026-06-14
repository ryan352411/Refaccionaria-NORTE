using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Npgsql;
using RefaccionariaPOS.Data;

namespace RefaccionariaPOS.Views
{
    public partial class MainView : Window
    {
        private const string RolVendedor = "Vendedor";
        private static readonly Brush InventarioNormal = new SolidColorBrush(Color.FromRgb(36, 59, 85));
        private static readonly Brush InventarioAdvertencia = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        private static readonly Brush InventarioCritico = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        private static readonly Brush NotificacionesSinAlertas = new SolidColorBrush(Color.FromRgb(36, 59, 85));
        private static readonly Brush FilaAdvertencia = new SolidColorBrush(Color.FromRgb(255, 237, 213));
        private static readonly Brush FilaCritica = new SolidColorBrush(Color.FromRgb(254, 226, 226));

        private readonly int idUsuarioActual;
        private readonly string usuarioActual;
        private readonly string rolUsuarioActual;
        private readonly List<InventarioAlerta> alertasInventario = new();

        public MainView(int idUsuario, string usuario, string rol)
        {
            InitializeComponent();

            idUsuarioActual = idUsuario;
            usuarioActual = usuario;
            rolUsuarioActual = rol;

            ConfigurarPermisosPorRol();
            CargarAlertasInventario();
        }

        private void ConfigurarPermisosPorRol()
        {
            lblRolVisual.Text = $"{usuarioActual} - {rolUsuarioActual}";

            if (!EsVendedorExacto())
            {
                return;
            }

            btnHistorial.IsEnabled = false;
            btnCorteCaja.IsEnabled = false;
            btnUsuarios.IsEnabled = false;
            lblSubtitulo.Text = "Terminal de cobro activa. Registra ventas y consulta inventario.";
        }

        private void BtnUsuarios_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new RegistrarUsuarioView());
        }

        private void BtnInventario_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new InventarioView(EsVendedorInventario()));
            CargarAlertasInventario();
        }

        private void BtnVenta_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new VentaView(idUsuarioActual));
            CargarAlertasInventario();
        }

        private void BtnHistorial_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new HistorialVentasView());
        }

        private void BtnCorteCaja_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new CorteCajaView());
        }

        private void BtnNotificaciones_Click(object sender, RoutedEventArgs e)
        {
            MostrarVentanaNotificaciones();
        }

        private void CargarAlertasInventario()
        {
            try
            {
                alertasInventario.Clear();

                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        SELECT codigo_barras, nombre, categoria, stock_actual, stock_minimo
                        FROM productos
                        WHERE stock_actual <= stock_minimo
                        ORDER BY
                            CASE WHEN stock_actual = 0 THEN 0 ELSE 1 END,
                            stock_actual ASC,
                            nombre ASC;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int stockActual = Convert.ToInt32(reader["stock_actual"]);
                            int stockMinimo = Convert.ToInt32(reader["stock_minimo"]);

                            alertasInventario.Add(new InventarioAlerta
                            {
                                CodigoBarras = reader["codigo_barras"].ToString() ?? string.Empty,
                                Nombre = reader["nombre"].ToString() ?? string.Empty,
                                Categoria = reader["categoria"].ToString() ?? "General",
                                StockActual = stockActual,
                                StockMinimo = stockMinimo,
                                Tipo = stockActual == 0 ? "Sin stock" : "Bajo stock"
                            });
                        }
                    }
                }

                ActualizarIndicadoresAlertas();
            }
            catch (Exception ex)
            {
                lblContadorAlertas.Text = "!";
                btnNotificaciones.Background = InventarioCritico;
                btnInventario.Background = InventarioCritico;
                btnInventario.Content = "Inventario / Catálogo";
                lblSubtitulo.Text = "No se pudieron cargar las alertas de inventario: " + ex.Message;
            }
        }

        private void ActualizarIndicadoresAlertas()
        {
            int totalAlertas = alertasInventario.Count;
            int agotados = alertasInventario.FindAll(a => a.StockActual == 0).Count;

            lblContadorAlertas.Text = totalAlertas.ToString();

            if (totalAlertas == 0)
            {
                btnNotificaciones.Background = NotificacionesSinAlertas;
                btnInventario.Background = InventarioNormal;
                btnInventario.Content = "Inventario / Catálogo";
                lblSubtitulo.Text = EsVendedorExacto()
                    ? "Terminal de cobro activa. Registra ventas y consulta inventario."
                    : "Inventario sin alertas de bajo stock.";
                return;
            }

            Brush colorAlerta = agotados > 0 ? InventarioCritico : InventarioAdvertencia;
            btnNotificaciones.Background = colorAlerta;
            btnInventario.Background = colorAlerta;
            btnInventario.Content = $"Inventario / Catálogo ({totalAlertas})";
            lblSubtitulo.Text = agotados > 0
                ? $"{totalAlertas} alertas de inventario: {agotados} productos sin stock."
                : $"{totalAlertas} productos estan por debajo del stock minimo.";
        }

        private void MostrarVentanaNotificaciones()
        {
            if (alertasInventario.Count == 0)
            {
                MessageBox.Show("No hay productos con bajo stock ni agotados.", "Notificaciones", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Window ventana = new Window
            {
                Title = "Notificaciones de inventario",
                Owner = this,
                Width = 780,
                Height = 520,
                MinWidth = 680,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White
            };

            Grid contenedor = new Grid { Margin = new Thickness(16) };
            contenedor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contenedor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            contenedor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock titulo = new TextBlock
            {
                Text = $"Alertas activas: {alertasInventario.Count}",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 39, 46)),
                Margin = new Thickness(0, 0, 0, 12)
            };

            DataGrid tabla = new DataGrid
            {
                ItemsSource = alertasInventario,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                RowHeight = 34,
                RowHeaderWidth = 0,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                HeadersVisibility = DataGridHeadersVisibility.Column
            };
            tabla.LoadingRow += (_, e) =>
            {
                if (e.Row.Item is not InventarioAlerta alerta)
                {
                    return;
                }

                e.Row.Background = alerta.StockActual == 0 ? FilaCritica : FilaAdvertencia;
                e.Row.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            };
            tabla.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new System.Windows.Data.Binding("Tipo"), Width = 100 });
            tabla.Columns.Add(new DataGridTextColumn { Header = "Codigo", Binding = new System.Windows.Data.Binding("CodigoBarras"), Width = 120 });
            tabla.Columns.Add(new DataGridTextColumn { Header = "Producto", Binding = new System.Windows.Data.Binding("Nombre"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            tabla.Columns.Add(new DataGridTextColumn { Header = "Categoria", Binding = new System.Windows.Data.Binding("Categoria"), Width = 120 });
            tabla.Columns.Add(new DataGridTextColumn { Header = "Stock", Binding = new System.Windows.Data.Binding("StockActual"), Width = 70 });
            tabla.Columns.Add(new DataGridTextColumn { Header = "Minimo", Binding = new System.Windows.Data.Binding("StockMinimo"), Width = 70 });

            DockPanel acciones = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            Button btnAbrirInventario = new Button
            {
                Content = "Abrir Inventario",
                Width = 140,
                Height = 36,
                Background = alertasInventario.Exists(a => a.StockActual == 0) ? InventarioCritico : InventarioAdvertencia,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            Button btnCerrar = new Button
            {
                Content = "Cerrar",
                Width = 100,
                Height = 36,
                Margin = new Thickness(10, 0, 0, 0)
            };

            btnAbrirInventario.Click += (_, _) =>
            {
                ventana.Close();
                MostrarDialogo(new InventarioView(EsVendedorInventario()));
                CargarAlertasInventario();
            };
            btnCerrar.Click += (_, _) => ventana.Close();

            DockPanel.SetDock(btnCerrar, Dock.Right);
            DockPanel.SetDock(btnAbrirInventario, Dock.Right);
            acciones.Children.Add(btnCerrar);
            acciones.Children.Add(btnAbrirInventario);

            Grid.SetRow(titulo, 0);
            Grid.SetRow(tabla, 1);
            Grid.SetRow(acciones, 2);
            contenedor.Children.Add(titulo);
            contenedor.Children.Add(tabla);
            contenedor.Children.Add(acciones);

            ventana.Content = contenedor;
            ventana.ShowDialog();
        }

        private bool EsVendedorExacto()
        {
            return rolUsuarioActual == RolVendedor;
        }

        private bool EsVendedorInventario()
        {
            return rolUsuarioActual.Equals(RolVendedor, StringComparison.OrdinalIgnoreCase);
        }

        private static void MostrarDialogo(Window ventana)
        {
            ventana.ShowDialog();
        }
    }

    public class InventarioAlerta
    {
        public string Tipo { get; set; } = string.Empty;
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public int StockActual { get; set; }
        public int StockMinimo { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Security;

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

        private int idUsuarioActual;
        private string usuarioActual;
        private string rolUsuarioActual;
        private readonly List<InventarioAlerta> alertasInventario = new();
        private readonly HashSet<string> permisosActuales = new(StringComparer.OrdinalIgnoreCase);
        private readonly StringBuilder scannerBuffer = new();
        private DateTime ultimoCaracterScanner = DateTime.MinValue;
        private Window? vistaEmbebidaActual;

        public MainView(int idUsuario, string usuario, string rol)
        {
            InitializeComponent();

            idUsuarioActual = idUsuario;
            usuarioActual = usuario;
            rolUsuarioActual = rol;

            PreviewTextInput += MainView_PreviewTextInput;
            PreviewKeyDown += MainView_PreviewKeyDown;
            CargarPermisosActuales();
            ConfigurarPermisosPorRol();
            CargarAlertasInventario();
        }

        private void ConfigurarPermisosPorRol()
        {
            lblRolVisual.Text = $"{usuarioActual} - {rolUsuarioActual}";
            btnUsuarioActual.Content = usuarioActual.Length > 20 ? usuarioActual.Substring(0, 20) + "..." : usuarioActual;
            btnUsuarioActual.ToolTip = "Cambiar usuario: " + usuarioActual;
            btnVenta.IsEnabled = TienePermiso("ventas.abrir");
            btnArticulosComunes.IsEnabled = TienePermiso("ventas.abrir");
            btnInventario.IsEnabled = TienePermiso("inventario.ver");
            btnHistorial.IsEnabled = TienePermiso("ventas.reimprimir_ticket") || !EsVendedorExacto();
            btnDevoluciones.IsEnabled = TienePermiso("ventas.devoluciones");
            btnClientes.IsEnabled = TienePermiso("clientes.ver");
            btnReimprimir.IsEnabled = TienePermiso("ventas.reimprimir_ticket");
            btnCorteCaja.IsEnabled = TienePermiso("corte.ver");
            btnUsuarios.IsEnabled = TienePermiso("usuarios.permisos");

            if (!EsVendedorExacto() || permisosActuales.Count > 0)
            {
                return;
            }

            btnHistorial.IsEnabled = false;
            btnDevoluciones.IsEnabled = false;
            btnClientes.IsEnabled = false;
            btnReimprimir.IsEnabled = false;
            btnCorteCaja.IsEnabled = false;
            btnUsuarios.IsEnabled = false;
            lblSubtitulo.Text = "Terminal de cobro activa. Registra ventas y consulta inventario.";
        }

        private void BtnUsuarios_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new RegistrarUsuarioView());
        }

        private void BtnInventario_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new InventarioView(!TienePermiso("inventario.editar")));
            CargarAlertasInventario();
        }

        private void BtnVenta_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new VentaView(idUsuarioActual));
            CargarAlertasInventario();
        }

        private void BtnArticulosComunes_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new ArticulosComunesView(idUsuarioActual));
        }

        private void MainView_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBoxBase || string.IsNullOrWhiteSpace(e.Text))
            {
                return;
            }

            if ((DateTime.Now - ultimoCaracterScanner).TotalMilliseconds > 500)
            {
                scannerBuffer.Clear();
            }

            ultimoCaracterScanner = DateTime.Now;
            scannerBuffer.Append(e.Text);
        }

        private void MainView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || scannerBuffer.Length < 3)
            {
                return;
            }

            string codigo = scannerBuffer.ToString().Trim();
            scannerBuffer.Clear();
            e.Handled = true;

            MostrarEnPanel(new VentaView(idUsuarioActual, codigo));
            CargarAlertasInventario();
        }

        private void BtnHistorial_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new HistorialVentasView(idUsuarioActual, false));
        }

        private void BtnDevoluciones_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new DevolucionesView(idUsuarioActual));
        }

        private void BtnClientes_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new ClientesView());
        }

        private void BtnReimprimir_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new ReimprimirTicketsView(idUsuarioActual));
        }

        private void BtnCorteCaja_Click(object sender, RoutedEventArgs e)
        {
            MostrarEnPanel(new CorteCajaView(idUsuarioActual, usuarioActual));
        }

        private void BtnNotificaciones_Click(object sender, RoutedEventArgs e)
        {
            MostrarVentanaNotificaciones();
        }

        private void BtnUsuarioActual_Click(object sender, RoutedEventArgs e)
        {
            Window ventana = new Window
            {
                Title = "Cambiar usuario",
                Owner = this,
                Width = 440,
                Height = 330,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            ComboBox cmbUsuarios = new ComboBox { Height = 38, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 6, 0, 14), DisplayMemberPath = "Username" };
            PasswordBox txtPassword = new PasswordBox { Height = 38, Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 6, 0, 20) };
            Button btnEntrar = new Button
            {
                Content = "Cambiar",
                Width = 130,
                Height = 38,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock { Text = "Usuario", FontWeight = FontWeights.Bold });
            panel.Children.Add(cmbUsuarios);
            panel.Children.Add(new TextBlock { Text = "Contrasena", FontWeight = FontWeights.Bold });
            panel.Children.Add(txtPassword);
            panel.Children.Add(btnEntrar);
            ventana.Content = panel;

            cmbUsuarios.ItemsSource = ObtenerUsuariosParaCambio();
            cmbUsuarios.SelectedValuePath = "Id";
            cmbUsuarios.SelectedValue = idUsuarioActual;

            btnEntrar.Click += (_, _) =>
            {
                if (cmbUsuarios.SelectedItem is not UsuarioCambio usuario)
                {
                    return;
                }

                if (!ValidarCambioUsuario(usuario.Id, txtPassword.Password, out string username, out string rol))
                {
                    MessageBox.Show("Contrasena incorrecta.", "Cambiar usuario", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPassword.Clear();
                    txtPassword.Focus();
                    return;
                }

                idUsuarioActual = usuario.Id;
                usuarioActual = username;
                rolUsuarioActual = rol;
                CargarPermisosActuales();
                ConfigurarPermisosPorRol();
                CargarAlertasInventario();
                ventana.DialogResult = true;
            };

            ventana.ShowDialog();
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
                            decimal stockActual = Convert.ToDecimal(reader["stock_actual"]);
                            decimal stockMinimo = Convert.ToDecimal(reader["stock_minimo"]);

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
                btnInventario.Content = "Inventario";
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
                btnInventario.Content = "Inventario";
                lblSubtitulo.Text = EsVendedorExacto()
                    ? "Terminal de cobro activa. Registra ventas y consulta inventario."
                    : "Inventario sin alertas de bajo stock.";
                return;
            }

            Brush colorAlerta = agotados > 0 ? InventarioCritico : InventarioAdvertencia;
            btnNotificaciones.Background = colorAlerta;
            btnInventario.Background = colorAlerta;
            btnInventario.Content = $"Inventario ({totalAlertas})";
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
                MostrarEnPanel(new InventarioView(EsVendedorInventario()));
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

        private bool TienePermiso(string clave)
        {
            if (permisosActuales.Count > 0)
            {
                return permisosActuales.Contains(clave);
            }

            if (!EsVendedorExacto())
            {
                return true;
            }

            return clave is "ventas.abrir" or "inventario.ver";
        }

        private void CargarPermisosActuales()
        {
            permisosActuales.Clear();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();
                    AsegurarTablasPermisos(conexion);

                    const string query = @"
                        SELECT p.clave
                        FROM usuario_permisos up
                        INNER JOIN permisos p ON p.id = up.permiso_id
                        WHERE up.usuario_id = @usuarioId
                          AND up.habilitado = true;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@usuarioId", idUsuarioActual);

                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                permisosActuales.Add(reader["clave"].ToString() ?? string.Empty);
                            }
                        }
                    }
                }
            }
            catch
            {
                permisosActuales.Clear();
            }
        }

        private static void AsegurarTablasPermisos(NpgsqlConnection conexion)
        {
            const string query = @"
                CREATE TABLE IF NOT EXISTS permisos (
                    id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    clave varchar(80) NOT NULL UNIQUE,
                    descripcion text NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS usuario_permisos (
                    usuario_id integer NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
                    permiso_id integer NOT NULL REFERENCES permisos(id) ON DELETE CASCADE,
                    habilitado boolean NOT NULL DEFAULT true,
                    PRIMARY KEY (usuario_id, permiso_id)
                );";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static List<UsuarioCambio> ObtenerUsuariosParaCambio()
        {
            List<UsuarioCambio> usuarios = new();

            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    SELECT id, username
                    FROM usuarios
                    ORDER BY username ASC;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                using (NpgsqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        usuarios.Add(new UsuarioCambio
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            Username = reader["username"].ToString() ?? string.Empty
                        });
                    }
                }
            }

            return usuarios;
        }

        private static bool ValidarCambioUsuario(int usuarioId, string password, out string username, out string rol)
        {
            username = string.Empty;
            rol = RolVendedor;

            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    SELECT username, rol, password_hash
                    FROM usuarios
                    WHERE id = @id
                    LIMIT 1;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@id", usuarioId);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return false;
                        }

                        string hash = reader["password_hash"].ToString() ?? string.Empty;
                        if (!PasswordHasher.Verify(password, hash))
                        {
                            return false;
                        }

                        username = reader["username"].ToString() ?? string.Empty;
                        rol = reader["rol"].ToString() ?? RolVendedor;
                        return true;
                    }
                }
            }
        }

        private void MostrarEnPanel(Window ventana)
        {
            UIElement? contenido = ventana.Content as UIElement;
            if (contenido == null)
            {
                return;
            }

            ventana.Content = null;
            contentHost.Content = null;

            if (contenido is FrameworkElement elemento)
            {
                elemento.HorizontalAlignment = HorizontalAlignment.Stretch;
                elemento.VerticalAlignment = VerticalAlignment.Stretch;
                elemento.Width = double.NaN;
                elemento.Height = double.NaN;
            }

            vistaEmbebidaActual = ventana;
            contentHost.Content = contenido;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ventana is VentaView venta)
                {
                    venta.ActivarDesdePanel();
                }
                else if (ventana is ArticulosComunesView articulosComunes)
                {
                    articulosComunes.ActivarDesdePanel();
                }
                else if (ventana is ClientesView clientes)
                {
                    clientes.ActivarDesdePanel();
                }
            }), DispatcherPriority.Loaded);
        }
    }

    public class InventarioAlerta
    {
        public string Tipo { get; set; } = string.Empty;
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public decimal StockActual { get; set; }
        public decimal StockMinimo { get; set; }
    }

    public class UsuarioCambio
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
    }
}

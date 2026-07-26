using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Security;
using RefaccionariaPOS.Services;

namespace RefaccionariaPOS.Views
{
    public partial class LoginView : Window
    {
        private bool iniciandoSesion;

        public LoginView()
        {
            InitializeComponent();
            CargarUsuarios();
            Loaded += (_, _) => txtPassword.Focus();
        }

        private void BtnEntrar_Click(object sender, RoutedEventArgs e)
        {
            IniciarSesion();
        }

        private void IniciarSesion()
        {
            if (cmbUsuarios.SelectedItem is not UsuarioLogin usuarioSeleccionado)
            {
                MessageBox.Show("Selecciona el usuario que entrara.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                cmbUsuarios.Focus();
                return;
            }

            string password = txtPassword.Password.Trim();

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Por favor, ingresa la contrasena.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPassword.Focus();
                return;
            }

            if (iniciandoSesion)
            {
                return;
            }

            iniciandoSesion = true;

            try
            {
                if (EstadoConexion.DebeIntentarOnline)
                {
                    try
                    {
                        IniciarSesionOnline(usuarioSeleccionado, password);
                        EstadoConexion.MarcarExito();
                        return;
                    }
                    catch (Exception ex) when (EstadoConexion.EsErrorDeConexion(ex))
                    {
                        EstadoConexion.MarcarFalla();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("No se pudo establecer comunicacion con el servidor de datos: " + ex.Message, "Error del Sistema", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                IniciarSesionOffline(usuarioSeleccionado, password);
            }
            finally
            {
                iniciandoSesion = false;
            }
        }

        private void IniciarSesionOnline(UsuarioLogin usuarioSeleccionado, string password)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = "SELECT id, username, rol, password_hash FROM usuarios WHERE id = @id LIMIT 1";
                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@id", usuarioSeleccionado.Id);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read() && PasswordHasher.Verify(password, reader["password_hash"].ToString() ?? string.Empty))
                        {
                            int idUsuario = Convert.ToInt32(reader["id"]);
                            string username = reader["username"].ToString() ?? usuarioSeleccionado.Username;
                            string rolObtenido = reader["rol"] != DBNull.Value
                                ? reader["rol"].ToString() ?? "Vendedor"
                                : "Vendedor";

                            AbrirPanelPrincipal(idUsuario, username, rolObtenido);
                        }
                        else
                        {
                            MessageBox.Show("Usuario o contrasena incorrectos.", "Error de Acceso", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private void IniciarSesionOffline(UsuarioLogin usuarioSeleccionado, string password)
        {
            UsuarioOffline? usuario = OfflineStore.CargarUsuarios()
                .FirstOrDefault(u => u.Id == usuarioSeleccionado.Id);

            if (usuario == null)
            {
                MessageBox.Show(
                    "No hay conexion con el servidor y esta computadora no tiene datos locales de ese usuario.\n" +
                    "Se necesita al menos un inicio de sesion con internet para habilitar el modo offline.",
                    "Sin conexion", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!PasswordHasher.Verify(password, usuario.PasswordHash))
            {
                MessageBox.Show("Usuario o contrasena incorrectos.", "Error de Acceso", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(
                "Sin conexion con el servidor. Entrando en modo offline:\n" +
                "puedes vender con el catalogo local y las ventas se sincronizaran al volver el internet.",
                "Modo offline", MessageBoxButton.OK, MessageBoxImage.Information);

            AbrirPanelPrincipal(usuario.Id, usuario.Username, usuario.Rol);
        }

        private void AbrirPanelPrincipal(int idUsuario, string username, string rol)
        {
            MainView mainWindow = new MainView(idUsuario, username, rol);
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }

        private void BtnVerPassword_Click(object sender, RoutedEventArgs e)
        {
            bool mostrar = txtPasswordVisible.Visibility != Visibility.Visible;

            if (mostrar)
            {
                txtPasswordVisible.Text = txtPassword.Password;
                txtPasswordVisible.Visibility = Visibility.Visible;
                txtPassword.Visibility = Visibility.Collapsed;
                iconoOjo.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
                btnVerPassword.ToolTip = "Ocultar contraseña";
                txtPasswordVisible.Focus();
                txtPasswordVisible.CaretIndex = txtPasswordVisible.Text.Length;
            }
            else
            {
                txtPassword.Password = txtPasswordVisible.Text;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
                txtPassword.Visibility = Visibility.Visible;
                iconoOjo.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x85));
                btnVerPassword.ToolTip = "Mostrar contraseña";
                txtPassword.Focus();
            }
        }

        private void TxtPasswordVisible_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Mantiene el PasswordBox al dia mientras se escribe con la contrasena visible,
            // porque IniciarSesion siempre lee txtPassword.Password.
            if (txtPassword.Password != txtPasswordVisible.Text)
            {
                txtPassword.Password = txtPasswordVisible.Text;
            }
        }

        private void Credentials_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            IniciarSesion();
        }

        private void CargarUsuarios()
        {
            List<UsuarioLogin> usuarios = new List<UsuarioLogin>();

            try
            {
                List<UsuarioOffline> usuariosCompletos = new List<UsuarioOffline>();
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        SELECT id, username, rol, password_hash
                        FROM usuarios
                        ORDER BY username ASC;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            usuarios.Add(new UsuarioLogin
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Username = reader["username"].ToString() ?? string.Empty
                            });

                            usuariosCompletos.Add(new UsuarioOffline
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Username = reader["username"].ToString() ?? string.Empty,
                                Rol = reader["rol"] != DBNull.Value ? reader["rol"].ToString() ?? "Vendedor" : "Vendedor",
                                PasswordHash = reader["password_hash"].ToString() ?? string.Empty
                            });
                        }
                    }
                }

                // Se guarda la lista local para poder iniciar sesion sin internet.
                OfflineStore.GuardarUsuarios(usuariosCompletos);
                EstadoConexion.MarcarExito();
            }
            catch (Exception ex)
            {
                if (EstadoConexion.EsErrorDeConexion(ex))
                {
                    EstadoConexion.MarcarFalla();
                }

                usuarios = OfflineStore.CargarUsuarios()
                    .OrderBy(u => u.Username, StringComparer.CurrentCultureIgnoreCase)
                    .Select(u => new UsuarioLogin { Id = u.Id, Username = u.Username })
                    .ToList();

                if (usuarios.Count == 0)
                {
                    MessageBox.Show("No se pudieron cargar los usuarios: " + ex.Message, "Error del Sistema", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            cmbUsuarios.ItemsSource = usuarios;
            if (usuarios.Count > 0)
            {
                cmbUsuarios.SelectedIndex = 0;
            }
        }
    }

    public class UsuarioLogin
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
    }
}

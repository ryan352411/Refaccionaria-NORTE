using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Security;

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

                                MainView mainWindow = new MainView(idUsuario, username, rolObtenido);
                                Application.Current.MainWindow = mainWindow;
                                mainWindow.Show();
                                Close();
                            }
                            else
                            {
                                MessageBox.Show("Usuario o contrasena incorrectos.", "Error de Acceso", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo establecer comunicacion con el servidor de datos: " + ex.Message, "Error del Sistema", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                iniciandoSesion = false;
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
                            usuarios.Add(new UsuarioLogin
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Username = reader["username"].ToString() ?? string.Empty
                            });
                        }
                    }
                }

                cmbUsuarios.ItemsSource = usuarios;
                if (usuarios.Count > 0)
                {
                    cmbUsuarios.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudieron cargar los usuarios: " + ex.Message, "Error del Sistema", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class UsuarioLogin
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
    }
}

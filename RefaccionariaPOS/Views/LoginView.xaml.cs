using System;
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
        }

        private void BtnEntrar_Click(object sender, RoutedEventArgs e)
        {
            IniciarSesion();
        }

        private void IniciarSesion()
        {
            string usuario = txtUsuario.Text.Trim();
            string password = txtPassword.Password.Trim();

            if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Por favor, ingresa tu usuario y contrasena.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                    const string query = "SELECT id, username, rol, password_hash FROM usuarios WHERE username ILIKE @user LIMIT 1";
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@user", usuario);

                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read() && PasswordHasher.Verify(password, reader["password_hash"].ToString() ?? string.Empty))
                            {
                                int idUsuario = Convert.ToInt32(reader["id"]);
                                string username = reader["username"].ToString() ?? usuario;
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
    }
}

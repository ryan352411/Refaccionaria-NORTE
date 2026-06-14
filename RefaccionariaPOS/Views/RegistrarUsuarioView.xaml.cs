using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Security;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class RegistrarUsuarioView : Window
    {
        public RegistrarUsuarioView()
        {
            InitializeComponent();
            CargarUsuarios();
        }

        private void BtnGuardarUsuario_Click(object sender, RoutedEventArgs e)
        {
            string usuario = txtNuevoUsuario.Text.Trim();
            string password = txtNuevaPassword.Password.Trim();
            string? rolSeleccionado = (cmbRol.SelectedItem as ComboBoxItem)?.Content.ToString();

            if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(rolSeleccionado))
            {
                MessageBox.Show("Por favor, llena todos los campos para continuar.", "Campos vacíos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string passwordEncriptada = PasswordHasher.Hash(password);

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    string queryInsertar = @"INSERT INTO usuarios (username, password_hash, rol)
                                            VALUES (@user, @pass, @rol)
                                            ON CONFLICT (username) DO NOTHING;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(queryInsertar, conexion))
                    {
                        cmd.Parameters.AddWithValue("@user", usuario);
                        cmd.Parameters.AddWithValue("@pass", passwordEncriptada);
                        cmd.Parameters.AddWithValue("@rol", rolSeleccionado);

                        int filasAfectadas = cmd.ExecuteNonQuery();

                        if (filasAfectadas > 0)
                        {
                            MessageBox.Show($"Usuario '{usuario}' registrado como {rolSeleccionado}.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                            txtNuevoUsuario.Clear();
                            txtNuevaPassword.Clear();
                            cmbRol.SelectedIndex = 0;
                            CargarUsuarios();
                        }
                        else
                        {
                            MessageBox.Show("Ese nombre de usuario ya existe.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            CargarUsuarios();
        }

        private void BtnEliminarUsuario_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsuarios.SelectedItem is not UsuarioSistema usuario)
            {
                MessageBox.Show("Selecciona un usuario de la tabla.", "Sin seleccion", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult confirmacion = MessageBox.Show(
                $"Seguro que deseas eliminar al usuario '{usuario.Username}'?",
                "Eliminar usuario",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmacion != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();
                    using (NpgsqlTransaction transaction = conexion.BeginTransaction())
                    {
                        if (usuario.Rol.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) && ObtenerTotalSuperAdmins(conexion, transaction) <= 1)
                        {
                            MessageBox.Show("No puedes eliminar el ultimo usuario SuperAdmin.", "Accion no permitida", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        if (UsuarioTieneHistorial(conexion, transaction, usuario.Id))
                        {
                            MessageBox.Show("No se puede eliminar porque el usuario ya tiene ventas o movimientos registrados.", "Historial protegido", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        using (NpgsqlCommand cmd = new NpgsqlCommand("DELETE FROM usuarios WHERE id = @id", conexion, transaction))
                        {
                            cmd.Parameters.AddWithValue("@id", usuario.Id);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                }

                MessageBox.Show("Usuario eliminado correctamente.", "Listo", MessageBoxButton.OK, MessageBoxImage.Information);
                CargarUsuarios();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al eliminar usuario: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static int ObtenerTotalSuperAdmins(NpgsqlConnection conexion, NpgsqlTransaction transaction)
        {
            using (NpgsqlCommand cmd = new NpgsqlCommand("SELECT COUNT(*) FROM usuarios WHERE rol ILIKE 'SuperAdmin'", conexion, transaction))
            {
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static bool UsuarioTieneHistorial(NpgsqlConnection conexion, NpgsqlTransaction transaction, int usuarioId)
        {
            const string query = @"
                SELECT
                    (SELECT COUNT(*) FROM ventas WHERE usuario_id = @id) +
                    (SELECT COUNT(*) FROM movimientos_inventario WHERE usuario_id = @id);";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion, transaction))
            {
                cmd.Parameters.AddWithValue("@id", usuarioId);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private void CargarUsuarios()
        {
            List<UsuarioSistema> usuarios = new List<UsuarioSistema>();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();
                    string query = @"SELECT id, username, COALESCE(rol, 'Vendedor') AS rol, COALESCE(fecha_alta, CURRENT_TIMESTAMP) AS fecha_alta
                                     FROM usuarios
                                     ORDER BY fecha_alta DESC, username ASC;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            usuarios.Add(new UsuarioSistema
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Username = reader["username"].ToString() ?? string.Empty,
                                Rol = reader["rol"].ToString() ?? "Vendedor",
                                FechaAlta = Convert.ToDateTime(reader["fecha_alta"])
                            });
                        }
                    }
                }

                dgUsuarios.ItemsSource = usuarios;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar usuarios: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class UsuarioSistema
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public DateTime FechaAlta { get; set; }
    }
}

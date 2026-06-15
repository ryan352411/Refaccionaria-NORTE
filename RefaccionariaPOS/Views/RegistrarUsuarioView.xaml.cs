using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Security;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class RegistrarUsuarioView : Window
    {
        private readonly ObservableCollection<PermisoUsuario> permisosUsuario = new();
        private int? usuarioPermisosId;

        public RegistrarUsuarioView()
        {
            InitializeComponent();
            AsegurarTablasPermisos();
            lstPermisos.ItemsSource = permisosUsuario;
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

        private void BtnGuardarPermisos_Click(object sender, RoutedEventArgs e)
        {
            if (!usuarioPermisosId.HasValue)
            {
                MessageBox.Show("Selecciona un usuario para asignar permisos.", "Permisos", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        using (NpgsqlCommand cmd = new NpgsqlCommand("DELETE FROM usuario_permisos WHERE usuario_id = @usuarioId;", conexion, transaction))
                        {
                            cmd.Parameters.AddWithValue("@usuarioId", usuarioPermisosId.Value);
                            cmd.ExecuteNonQuery();
                        }

                        const string insert = @"
                            INSERT INTO usuario_permisos (usuario_id, permiso_id, habilitado)
                            VALUES (@usuarioId, @permisoId, @habilitado);";

                        foreach (PermisoUsuario permiso in permisosUsuario)
                        {
                            using (NpgsqlCommand cmd = new NpgsqlCommand(insert, conexion, transaction))
                            {
                                cmd.Parameters.AddWithValue("@usuarioId", usuarioPermisosId.Value);
                                cmd.Parameters.AddWithValue("@permisoId", permiso.Id);
                                cmd.Parameters.AddWithValue("@habilitado", permiso.Habilitado);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                }

                MessageBox.Show("Permisos guardados.", "Permisos", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudieron guardar los permisos: " + ex.Message, "Permisos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void DgUsuarios_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgUsuarios.SelectedItem is not UsuarioSistema usuario)
            {
                return;
            }

            usuarioPermisosId = usuario.Id;
            lblUsuarioPermisos.Text = $"{usuario.Username} ({usuario.Rol})";
            CargarPermisosUsuario(usuario);
        }

        private void CargarPermisosUsuario(UsuarioSistema usuario)
        {
            permisosUsuario.Clear();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        SELECT p.id, p.clave, p.descripcion,
                               COALESCE(up.habilitado,
                                   CASE WHEN @rol ILIKE 'SuperAdmin' THEN true
                                        WHEN p.clave IN ('ventas.abrir', 'inventario.ver') THEN true
                                        ELSE false
                                   END) AS habilitado
                        FROM permisos p
                        LEFT JOIN usuario_permisos up
                          ON up.permiso_id = p.id AND up.usuario_id = @usuarioId
                        ORDER BY p.clave;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@usuarioId", usuario.Id);
                        cmd.Parameters.AddWithValue("@rol", usuario.Rol);

                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                permisosUsuario.Add(new PermisoUsuario
                                {
                                    Id = Convert.ToInt32(reader["id"]),
                                    Clave = reader["clave"].ToString() ?? string.Empty,
                                    Descripcion = reader["descripcion"].ToString() ?? string.Empty,
                                    Habilitado = Convert.ToBoolean(reader["habilitado"])
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudieron cargar los permisos: " + ex.Message, "Permisos", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void AsegurarTablasPermisos()
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

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
                    );

                    INSERT INTO permisos (clave, descripcion)
                    VALUES
                        ('ventas.abrir', 'Abrir punto de venta'),
                        ('ventas.devoluciones', 'Registrar devoluciones'),
                        ('ventas.reimprimir_ticket', 'Reimprimir tickets'),
                        ('inventario.ver', 'Ver inventario'),
                        ('inventario.editar', 'Registrar productos y modificar stock'),
                        ('clientes.ver', 'Ver clientes frecuentes'),
                        ('clientes.editar', 'Crear y editar clientes frecuentes'),
                        ('corte.ver', 'Ver corte de caja'),
                        ('usuarios.permisos', 'Administrar usuarios y permisos')
                    ON CONFLICT (clave) DO UPDATE
                    SET descripcion = EXCLUDED.descripcion;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.ExecuteNonQuery();
                }
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

    public class PermisoUsuario
    {
        public int Id { get; set; }
        public string Clave { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public bool Habilitado { get; set; }
    }
}

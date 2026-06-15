using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class ClientesView : Window
    {
        private int? clienteSeleccionadoId;

        public ClientesView()
        {
            InitializeComponent();
            AsegurarTablaClientes();
            CargarClientes();
            Loaded += (_, _) => txtNombre.Focus();
        }

        public void ActivarDesdePanel()
        {
            txtNombre.Focus();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            string nombre = txtNombre.Text.Trim();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                MessageBox.Show("Escribe el nombre del cliente.", "Clientes", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNombre.Focus();
                return;
            }

            if (!int.TryParse(txtPuntos.Text, out int puntos) || puntos < 0)
            {
                MessageBox.Show("Ingresa puntos validos.", "Clientes", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPuntos.Focus();
                return;
            }

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    string query = clienteSeleccionadoId.HasValue
                        ? @"UPDATE clientes
                            SET nombre = @nombre, telefono = @telefono, correo = @correo, notas = @notas, puntos = @puntos
                            WHERE id = @id;"
                        : @"INSERT INTO clientes (nombre, telefono, correo, notas, puntos)
                            VALUES (@nombre, @telefono, @correo, @notas, @puntos);";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@nombre", nombre);
                        cmd.Parameters.AddWithValue("@telefono", txtTelefono.Text.Trim());
                        cmd.Parameters.AddWithValue("@correo", txtCorreo.Text.Trim());
                        cmd.Parameters.AddWithValue("@notas", txtNotas.Text.Trim());
                        cmd.Parameters.AddWithValue("@puntos", puntos);
                        if (clienteSeleccionadoId.HasValue)
                        {
                            cmd.Parameters.AddWithValue("@id", clienteSeleccionadoId.Value);
                        }

                        cmd.ExecuteNonQuery();
                    }
                }

                LimpiarFormulario();
                CargarClientes(txtBuscar.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo guardar el cliente: " + ex.Message, "Clientes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormulario();
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (dgClientes.SelectedItem is not ClienteFrecuente cliente)
            {
                MessageBox.Show("Selecciona un cliente.", "Clientes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult confirmacion = MessageBox.Show(
                $"Eliminar a '{cliente.Nombre}'?",
                "Clientes",
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
                    using (NpgsqlCommand cmd = new NpgsqlCommand("DELETE FROM clientes WHERE id = @id;", conexion))
                    {
                        cmd.Parameters.AddWithValue("@id", cliente.Id);
                        cmd.ExecuteNonQuery();
                    }
                }

                LimpiarFormulario();
                CargarClientes(txtBuscar.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo eliminar el cliente: " + ex.Message, "Clientes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            CargarClientes(txtBuscar.Text.Trim());
        }

        private void DgClientes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgClientes.SelectedItem is not ClienteFrecuente cliente)
            {
                return;
            }

            clienteSeleccionadoId = cliente.Id;
            lblModo.Text = "Editar cliente";
            txtNombre.Text = cliente.Nombre;
            txtTelefono.Text = cliente.Telefono;
            txtCorreo.Text = cliente.Correo;
            txtPuntos.Text = cliente.Puntos.ToString();
            txtNotas.Text = cliente.Notas;
        }

        private void CargarClientes(string busqueda = "")
        {
            List<ClienteFrecuente> clientes = new();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        SELECT id, nombre, telefono, correo, notas, puntos, fecha_alta
                        FROM clientes
                        WHERE nombre ILIKE @busqueda OR telefono ILIKE @busqueda OR correo ILIKE @busqueda
                        ORDER BY nombre ASC;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@busqueda", "%" + busqueda + "%");

                        using (NpgsqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                clientes.Add(new ClienteFrecuente
                                {
                                    Id = Convert.ToInt32(reader["id"]),
                                    Nombre = reader["nombre"].ToString() ?? string.Empty,
                                    Telefono = reader["telefono"].ToString() ?? string.Empty,
                                    Correo = reader["correo"].ToString() ?? string.Empty,
                                    Notas = reader["notas"].ToString() ?? string.Empty,
                                    Puntos = Convert.ToInt32(reader["puntos"]),
                                    FechaAlta = Convert.ToDateTime(reader["fecha_alta"])
                                });
                            }
                        }
                    }
                }

                dgClientes.ItemsSource = clientes;
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudieron cargar los clientes: " + ex.Message, "Clientes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LimpiarFormulario()
        {
            clienteSeleccionadoId = null;
            lblModo.Text = "Nuevo cliente";
            txtNombre.Clear();
            txtTelefono.Clear();
            txtCorreo.Clear();
            txtNotas.Clear();
            txtPuntos.Text = "0";
            dgClientes.SelectedItem = null;
            txtNombre.Focus();
        }

        private static void AsegurarTablaClientes()
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    CREATE TABLE IF NOT EXISTS clientes (
                        id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        nombre varchar(180) NOT NULL,
                        telefono varchar(40) NOT NULL DEFAULT '',
                        correo varchar(160) NOT NULL DEFAULT '',
                        notas text NOT NULL DEFAULT '',
                        puntos integer NOT NULL DEFAULT 0,
                        fecha_alta timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );

                    CREATE INDEX IF NOT EXISTS idx_clientes_nombre ON clientes (nombre);";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

    public class ClienteFrecuente
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Notas { get; set; } = string.Empty;
        public int Puntos { get; set; }
        public DateTime FechaAlta { get; set; }
    }
}

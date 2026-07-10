using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class WhatsAppDestinatariosView : Window
    {
        private int? destinatarioSeleccionadoId;

        public WhatsAppDestinatariosView()
        {
            InitializeComponent();
            AsegurarTablaDestinatarios();
            CargarDestinatarios();
            Loaded += (_, _) => txtNumero.Focus();
        }

        public void ActivarDesdePanel()
        {
            txtNumero.Focus();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (!TryNormalizarNumero(txtNumero.Text, out string numero, out string error))
            {
                MessageBox.Show(error, "Avisos por WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNumero.Focus();
                return;
            }

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    if (NumeroDuplicado(conexion, numero, destinatarioSeleccionadoId))
                    {
                        MessageBox.Show("Ese numero ya esta registrado.", "Avisos por WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string query = destinatarioSeleccionadoId.HasValue
                        ? @"UPDATE whatsapp_destinatarios
                            SET numero = @numero, nombre = @nombre, activo = @activo
                            WHERE id = @id;"
                        : @"INSERT INTO whatsapp_destinatarios (numero, nombre, activo)
                            VALUES (@numero, @nombre, @activo);";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@numero", numero);
                        cmd.Parameters.AddWithValue("@nombre", txtNombre.Text.Trim());
                        cmd.Parameters.AddWithValue("@activo", chkActivo.IsChecked == true);
                        if (destinatarioSeleccionadoId.HasValue)
                        {
                            cmd.Parameters.AddWithValue("@id", destinatarioSeleccionadoId.Value);
                        }

                        cmd.ExecuteNonQuery();
                    }
                }

                LimpiarFormulario();
                CargarDestinatarios();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo guardar el destinatario: " + ex.Message, "Avisos por WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormulario();
        }

        private void BtnAlternar_Click(object sender, RoutedEventArgs e)
        {
            if (dgDestinatarios.SelectedItem is not WhatsAppDestinatario destinatario)
            {
                MessageBox.Show("Selecciona un destinatario.", "Avisos por WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand("UPDATE whatsapp_destinatarios SET activo = NOT activo WHERE id = @id;", conexion))
                    {
                        cmd.Parameters.AddWithValue("@id", destinatario.Id);
                        cmd.ExecuteNonQuery();
                    }
                }

                CargarDestinatarios();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo cambiar el estado: " + ex.Message, "Avisos por WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (dgDestinatarios.SelectedItem is not WhatsAppDestinatario destinatario)
            {
                MessageBox.Show("Selecciona un destinatario.", "Avisos por WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string etiqueta = string.IsNullOrWhiteSpace(destinatario.Nombre) ? destinatario.Numero : destinatario.Nombre;
            MessageBoxResult confirmacion = MessageBox.Show(
                $"Eliminar el destinatario '{etiqueta}'?",
                "Avisos por WhatsApp",
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
                    using (NpgsqlCommand cmd = new NpgsqlCommand("DELETE FROM whatsapp_destinatarios WHERE id = @id;", conexion))
                    {
                        cmd.Parameters.AddWithValue("@id", destinatario.Id);
                        cmd.ExecuteNonQuery();
                    }
                }

                LimpiarFormulario();
                CargarDestinatarios();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo eliminar el destinatario: " + ex.Message, "Avisos por WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DgDestinatarios_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgDestinatarios.SelectedItem is not WhatsAppDestinatario destinatario)
            {
                return;
            }

            destinatarioSeleccionadoId = destinatario.Id;
            lblModo.Text = "Editar destinatario";
            txtNumero.Text = destinatario.Numero;
            txtNombre.Text = destinatario.Nombre;
            chkActivo.IsChecked = destinatario.Activo;
        }

        private void CargarDestinatarios()
        {
            List<WhatsAppDestinatario> destinatarios = new();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        SELECT id, numero, nombre, activo, fecha_alta
                        FROM whatsapp_destinatarios
                        ORDER BY activo DESC, nombre ASC, id ASC;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            destinatarios.Add(new WhatsAppDestinatario
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Numero = reader["numero"].ToString() ?? string.Empty,
                                Nombre = reader["nombre"].ToString() ?? string.Empty,
                                Activo = Convert.ToBoolean(reader["activo"]),
                                FechaAlta = Convert.ToDateTime(reader["fecha_alta"])
                            });
                        }
                    }
                }

                dgDestinatarios.ItemsSource = destinatarios;
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudieron cargar los destinatarios: " + ex.Message, "Avisos por WhatsApp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LimpiarFormulario()
        {
            destinatarioSeleccionadoId = null;
            lblModo.Text = "Nuevo destinatario";
            txtNumero.Clear();
            txtNombre.Clear();
            chkActivo.IsChecked = true;
            dgDestinatarios.SelectedItem = null;
            txtNumero.Focus();
        }

        private static bool NumeroDuplicado(NpgsqlConnection conexion, string numero, int? idActual)
        {
            string query = idActual.HasValue
                ? "SELECT 1 FROM whatsapp_destinatarios WHERE numero = @numero AND id <> @id LIMIT 1;"
                : "SELECT 1 FROM whatsapp_destinatarios WHERE numero = @numero LIMIT 1;";

            using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            cmd.Parameters.AddWithValue("@numero", numero);
            if (idActual.HasValue)
            {
                cmd.Parameters.AddWithValue("@id", idActual.Value);
            }

            return cmd.ExecuteScalar() != null;
        }

        private static bool TryNormalizarNumero(string entrada, out string numero, out string error)
        {
            numero = string.Empty;
            error = string.Empty;

            string soloDigitos = new string((entrada ?? string.Empty).Where(char.IsDigit).ToArray());

            if (soloDigitos.Length == 0)
            {
                error = "Escribe el numero de WhatsApp.";
                return false;
            }

            if (soloDigitos.Length < 10 || soloDigitos.Length > 15)
            {
                error = "El numero debe tener entre 10 y 15 digitos, en formato internacional sin +. Ej: 5212381234567";
                return false;
            }

            numero = soloDigitos;
            return true;
        }

        private static void AsegurarTablaDestinatarios()
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                const string query = @"
                    CREATE TABLE IF NOT EXISTS whatsapp_destinatarios (
                        id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        numero varchar(20) NOT NULL,
                        nombre varchar(120) NOT NULL DEFAULT '',
                        activo boolean NOT NULL DEFAULT true,
                        fecha_alta timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

    public class WhatsAppDestinatario
    {
        public int Id { get; set; }
        public string Numero { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public DateTime FechaAlta { get; set; }
        public string EstadoTexto => Activo ? "Activo" : "Inactivo";
    }
}

using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class TelegramDestinatariosView : Window
    {
        private int? destinatarioSeleccionadoId;

        public TelegramDestinatariosView()
        {
            InitializeComponent();
            AsegurarTablaDestinatarios();
            CargarDestinatarios();
            Loaded += (_, _) => txtChatId.Focus();
        }

        public void ActivarDesdePanel()
        {
            txtChatId.Focus();
        }

        private async void BtnDetectar_Click(object sender, RoutedEventArgs e)
        {
            btnDetectar.IsEnabled = false;
            try
            {
                List<TelegramChatDetectado> detectados = await TelegramNotificationService.ObtenerChatsRecientesAsync();
                HashSet<string> registrados = ObtenerChatIdsRegistrados();
                List<TelegramChatDetectado> nuevos = detectados
                    .Where(chat => !registrados.Contains(chat.ChatId))
                    .ToList();

                if (nuevos.Count == 0)
                {
                    MessageBox.Show(
                        detectados.Count == 0
                            ? "No hay mensajes recientes al bot. Pide a la persona que le escriba cualquier mensaje al bot en Telegram y vuelve a intentar."
                            : "Todos los chats que le han escrito al bot ya estan registrados.",
                        "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                TelegramChatDetectado chatNuevo = nuevos[nuevos.Count - 1];
                destinatarioSeleccionadoId = null;
                lblModo.Text = "Nuevo destinatario";
                txtChatId.Text = chatNuevo.ChatId;
                txtNombre.Text = chatNuevo.Nombre;
                chkActivo.IsChecked = true;

                MessageBox.Show(
                    $"Chat detectado: {chatNuevo.Nombre}\nChat ID: {chatNuevo.ChatId}\n\nRevisa los datos y presiona Guardar." +
                    (nuevos.Count > 1 ? $"\n\nHay {nuevos.Count - 1} chat(s) mas sin registrar; guarda este y vuelve a detectar." : string.Empty),
                    "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudieron consultar los chats del bot: " + ex.Message, "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btnDetectar.IsEnabled = true;
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (!TryNormalizarChatId(txtChatId.Text, out string chatId, out string error))
            {
                MessageBox.Show(error, "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtChatId.Focus();
                return;
            }

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    if (ChatDuplicado(conexion, chatId, destinatarioSeleccionadoId))
                    {
                        MessageBox.Show("Ese chat ya esta registrado.", "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string query = destinatarioSeleccionadoId.HasValue
                        ? @"UPDATE telegram_destinatarios
                            SET chat_id = @chatId, nombre = @nombre, activo = @activo
                            WHERE id = @id;"
                        : @"INSERT INTO telegram_destinatarios (chat_id, nombre, activo)
                            VALUES (@chatId, @nombre, @activo);";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.Parameters.AddWithValue("@chatId", chatId);
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
                MessageBox.Show("No se pudo guardar el destinatario: " + ex.Message, "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormulario();
        }

        private void BtnAlternar_Click(object sender, RoutedEventArgs e)
        {
            if (dgDestinatarios.SelectedItem is not TelegramDestinatario destinatario)
            {
                MessageBox.Show("Selecciona un destinatario.", "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand("UPDATE telegram_destinatarios SET activo = NOT activo WHERE id = @id;", conexion))
                    {
                        cmd.Parameters.AddWithValue("@id", destinatario.Id);
                        cmd.ExecuteNonQuery();
                    }
                }

                CargarDestinatarios();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo cambiar el estado: " + ex.Message, "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (dgDestinatarios.SelectedItem is not TelegramDestinatario destinatario)
            {
                MessageBox.Show("Selecciona un destinatario.", "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string etiqueta = string.IsNullOrWhiteSpace(destinatario.Nombre) ? destinatario.ChatId : destinatario.Nombre;
            MessageBoxResult confirmacion = MessageBox.Show(
                $"Eliminar el destinatario '{etiqueta}'?",
                "Avisos por Telegram",
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
                    using (NpgsqlCommand cmd = new NpgsqlCommand("DELETE FROM telegram_destinatarios WHERE id = @id;", conexion))
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
                MessageBox.Show("No se pudo eliminar el destinatario: " + ex.Message, "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DgDestinatarios_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgDestinatarios.SelectedItem is not TelegramDestinatario destinatario)
            {
                return;
            }

            destinatarioSeleccionadoId = destinatario.Id;
            lblModo.Text = "Editar destinatario";
            txtChatId.Text = destinatario.ChatId;
            txtNombre.Text = destinatario.Nombre;
            chkActivo.IsChecked = destinatario.Activo;
        }

        private void CargarDestinatarios()
        {
            List<TelegramDestinatario> destinatarios = new();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        SELECT id, chat_id, nombre, activo, fecha_alta
                        FROM telegram_destinatarios
                        ORDER BY activo DESC, nombre ASC, id ASC;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            destinatarios.Add(new TelegramDestinatario
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                ChatId = reader["chat_id"].ToString() ?? string.Empty,
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
                MessageBox.Show("No se pudieron cargar los destinatarios: " + ex.Message, "Avisos por Telegram", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private HashSet<string> ObtenerChatIdsRegistrados()
        {
            HashSet<string> registrados = new();
            if (dgDestinatarios.ItemsSource is IEnumerable<TelegramDestinatario> destinatarios)
            {
                foreach (TelegramDestinatario destinatario in destinatarios)
                {
                    registrados.Add(destinatario.ChatId);
                }
            }

            return registrados;
        }

        private void LimpiarFormulario()
        {
            destinatarioSeleccionadoId = null;
            lblModo.Text = "Nuevo destinatario";
            txtChatId.Clear();
            txtNombre.Clear();
            chkActivo.IsChecked = true;
            dgDestinatarios.SelectedItem = null;
            txtChatId.Focus();
        }

        private static bool ChatDuplicado(NpgsqlConnection conexion, string chatId, int? idActual)
        {
            string query = idActual.HasValue
                ? "SELECT 1 FROM telegram_destinatarios WHERE chat_id = @chatId AND id <> @id LIMIT 1;"
                : "SELECT 1 FROM telegram_destinatarios WHERE chat_id = @chatId LIMIT 1;";

            using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            cmd.Parameters.AddWithValue("@chatId", chatId);
            if (idActual.HasValue)
            {
                cmd.Parameters.AddWithValue("@id", idActual.Value);
            }

            return cmd.ExecuteScalar() != null;
        }

        private static bool TryNormalizarChatId(string entrada, out string chatId, out string error)
        {
            chatId = (entrada ?? string.Empty).Trim();
            error = string.Empty;

            if (chatId.Length == 0)
            {
                error = "Escribe el Chat ID o usa el boton 'Detectar chat nuevo'.";
                return false;
            }

            // Chat IDs de Telegram: numeros, posiblemente negativos en grupos (ej. -100123456789).
            string resto = chatId.StartsWith("-") ? chatId.Substring(1) : chatId;
            if (resto.Length == 0 || !resto.All(char.IsDigit))
            {
                error = "El Chat ID debe ser numerico (ej. 123456789). Usa el boton 'Detectar chat nuevo' para obtenerlo automaticamente.";
                return false;
            }

            return true;
        }

        private static void AsegurarTablaDestinatarios()
        {
            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();
                    TelegramNotificationService.AsegurarTablaDestinatariosAsync(conexion).GetAwaiter().GetResult();
                }
            }
            catch
            {
            }
        }
    }

    public class TelegramDestinatario
    {
        public int Id { get; set; }
        public string ChatId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public DateTime FechaAlta { get; set; }
        public string EstadoTexto => Activo ? "Activo" : "Inactivo";
    }
}

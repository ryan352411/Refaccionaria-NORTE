using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RefaccionariaPOS.Services
{
    /// <summary>
    /// Envia un aviso por Telegram (Bot API) cuando se registra una venta.
    /// El envio es "fire and forget": cualquier fallo se ignora para no afectar la venta.
    /// Requiere la variable de entorno REFAX_TELEGRAM_TOKEN (token del bot de @BotFather).
    /// Los chats destino se configuran en la tabla telegram_destinatarios (sin recompilar).
    /// </summary>
    public static class TelegramNotificationService
    {
        private const string TokenVariable = "REFAX_TELEGRAM_TOKEN";

        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly CultureInfo CulturaMexico = CultureInfo.GetCultureInfo("es-MX");

        /// <summary>
        /// Lanza el aviso de la venta indicada sin bloquear a quien lo invoca.
        /// Nunca propaga excepciones.
        /// </summary>
        public static void NotificarVentaEnSegundoPlano(int ventaId)
        {
            _ = NotificarVentaAsync(ventaId);
        }

        public static bool EstaConfigurado()
        {
            return !string.IsNullOrWhiteSpace(LeerToken());
        }

        public static async Task NotificarVentaAsync(int ventaId)
        {
            try
            {
                Log($"--- Inicio aviso venta_id={ventaId} ---");
                string? token = LeerToken();

                if (string.IsNullOrWhiteSpace(token))
                {
                    // Telegram no esta configurado en esta instalacion: no se envia nada.
                    Log("ABORTA: falta la variable REFAX_TELEGRAM_TOKEN.");
                    return;
                }

                VentaNotificacion? venta = await ObtenerDatosVentaAsync(ventaId).ConfigureAwait(false);
                if (venta == null)
                {
                    Log("ABORTA: no se encontro la venta en la base de datos.");
                    return;
                }

                List<string> chats = await ObtenerChatsActivosAsync().ConfigureAwait(false);
                Log($"Chats activos: {chats.Count} [{string.Join(", ", chats)}]");
                if (chats.Count == 0)
                {
                    Log("ABORTA: no hay chats activos en telegram_destinatarios.");
                    return;
                }

                string vendedor = string.IsNullOrWhiteSpace(venta.Vendedor) ? "N/D" : venta.Vendedor;
                string mensaje =
                    "🛒 Nueva venta registrada\n" +
                    $"Folio: {venta.Folio.ToString(CultureInfo.InvariantCulture)}\n" +
                    $"Total: {venta.Total.ToString("C", CulturaMexico)}\n" +
                    $"Vendedor: {vendedor}\n" +
                    $"Fecha: {venta.Fecha.ToString("dd/MM/yyyy HH:mm", CulturaMexico)}";

                foreach (string chatId in chats)
                {
                    await EnviarMensajeAsync(token, chatId, mensaje).ConfigureAwait(false);
                }

                Log("--- Fin aviso ---");
            }
            catch (Exception ex)
            {
                // El aviso de Telegram nunca debe afectar la operacion de la caja.
                Log("EXCEPCION general: " + ex.Message);
            }
        }

        /// <summary>
        /// Lanza el aviso de productos con bajo stock sin bloquear a quien lo invoca.
        /// Nunca propaga excepciones.
        /// </summary>
        public static void NotificarBajoStockEnSegundoPlano(IReadOnlyList<ProductoBajoStock> productos)
        {
            _ = NotificarBajoStockAsync(productos);
        }

        public static async Task NotificarBajoStockAsync(IReadOnlyList<ProductoBajoStock> productos)
        {
            try
            {
                if (productos == null || productos.Count == 0)
                {
                    return;
                }

                Log($"--- Inicio aviso bajo stock ({productos.Count} productos) ---");
                string? token = LeerToken();
                if (string.IsNullOrWhiteSpace(token))
                {
                    Log("ABORTA: falta la variable REFAX_TELEGRAM_TOKEN.");
                    return;
                }

                List<string> chats = await ObtenerChatsActivosAsync().ConfigureAwait(false);
                Log($"Chats activos: {chats.Count} [{string.Join(", ", chats)}]");
                if (chats.Count == 0)
                {
                    Log("ABORTA: no hay chats activos en telegram_destinatarios.");
                    return;
                }

                StringBuilder mensaje = new StringBuilder("⚠️ Alerta de bajo stock\n");
                foreach (ProductoBajoStock producto in productos)
                {
                    mensaje.Append($"\n• {producto.Nombre}");
                    if (!string.IsNullOrWhiteSpace(producto.Codigo))
                    {
                        mensaje.Append($" (código {producto.Codigo})");
                    }
                    mensaje.Append($": quedan {FormatearCantidad(producto.StockActual)}, mínimo {FormatearCantidad(producto.StockMinimo)}");
                }

                string texto = mensaje.ToString();
                foreach (string chatId in chats)
                {
                    await EnviarMensajeAsync(token, chatId, texto).ConfigureAwait(false);
                }

                Log("--- Fin aviso bajo stock ---");
            }
            catch (Exception ex)
            {
                // El aviso de Telegram nunca debe afectar la operacion de la caja.
                Log("EXCEPCION bajo stock: " + ex.Message);
            }
        }

        private static string FormatearCantidad(decimal valor)
        {
            return valor == Math.Truncate(valor)
                ? valor.ToString("0", CulturaMexico)
                : valor.ToString("0.###", CulturaMexico);
        }

        private static async Task EnviarMensajeAsync(string token, string chatId, string texto)
        {
            try
            {
                var cuerpo = new
                {
                    chat_id = chatId,
                    text = texto
                };

                string json = JsonSerializer.Serialize(cuerpo);
                string url = $"https://api.telegram.org/bot{token}/sendMessage";

                using StringContent contenidoSolicitud = new StringContent(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage respuesta = await httpClient.PostAsync(url, contenidoSolicitud).ConfigureAwait(false);
                string contenido = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (respuesta.IsSuccessStatusCode)
                {
                    Log($"OK -> {chatId}: {(int)respuesta.StatusCode}");
                }
                else
                {
                    Log($"ERROR -> {chatId}: {(int)respuesta.StatusCode} {contenido}");
                }
            }
            catch (Exception ex)
            {
                // Ignorar fallos por destinatario, pero registrarlos.
                Log($"EXCEPCION -> {chatId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Consulta getUpdates del bot y regresa los chats que le han escrito recientemente
        /// (Telegram conserva los mensajes ~24 horas). Sirve para dar de alta destinatarios
        /// sin conocer el chat_id: la persona le escribe al bot y aqui aparece.
        /// </summary>
        public static async Task<List<TelegramChatDetectado>> ObtenerChatsRecientesAsync()
        {
            List<TelegramChatDetectado> chats = new();
            string? token = LeerToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Falta configurar la variable REFAX_TELEGRAM_TOKEN (token del bot).");
            }

            string url = $"https://api.telegram.org/bot{token}/getUpdates";
            using HttpResponseMessage respuesta = await httpClient.GetAsync(url).ConfigureAwait(false);
            string contenido = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Telegram respondio {(int)respuesta.StatusCode}: {contenido}");
            }

            using JsonDocument doc = JsonDocument.Parse(contenido);
            if (!doc.RootElement.TryGetProperty("result", out JsonElement resultados))
            {
                return chats;
            }

            HashSet<string> vistos = new();
            foreach (JsonElement actualizacion in resultados.EnumerateArray())
            {
                if (!actualizacion.TryGetProperty("message", out JsonElement mensaje)
                    || !mensaje.TryGetProperty("chat", out JsonElement chat)
                    || !chat.TryGetProperty("id", out JsonElement idElemento))
                {
                    continue;
                }

                string chatId = idElemento.GetRawText();
                if (!vistos.Add(chatId))
                {
                    continue;
                }

                string nombre = LeerTexto(chat, "first_name");
                string apellido = LeerTexto(chat, "last_name");
                string usuario = LeerTexto(chat, "username");
                string titulo = LeerTexto(chat, "title");

                string etiqueta = $"{nombre} {apellido}".Trim();
                if (string.IsNullOrWhiteSpace(etiqueta))
                {
                    etiqueta = titulo;
                }

                if (!string.IsNullOrWhiteSpace(usuario))
                {
                    etiqueta = string.IsNullOrWhiteSpace(etiqueta) ? "@" + usuario : $"{etiqueta} (@{usuario})";
                }

                chats.Add(new TelegramChatDetectado
                {
                    ChatId = chatId,
                    Nombre = etiqueta
                });
            }

            return chats;
        }

        private static string LeerTexto(JsonElement elemento, string propiedad)
        {
            return elemento.TryGetProperty(propiedad, out JsonElement valor) && valor.ValueKind == JsonValueKind.String
                ? valor.GetString() ?? string.Empty
                : string.Empty;
        }

        private static void Log(string mensaje)
        {
            try
            {
                string carpeta = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RefaxManager");
                Directory.CreateDirectory(carpeta);
                string ruta = Path.Combine(carpeta, "telegram.log");
                File.AppendAllText(ruta, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {mensaje}{Environment.NewLine}");
            }
            catch
            {
                // El log nunca debe romper nada.
            }
        }

        private static async Task<VentaNotificacion?> ObtenerDatosVentaAsync(int ventaId)
        {
            DatabaseConnection db = new DatabaseConnection();
            await using NpgsqlConnection conexion = db.GetConnection();
            await conexion.OpenAsync().ConfigureAwait(false);

            const string query = @"
                SELECT v.folio, v.total, v.fecha_venta, COALESCE(u.username, '') AS vendedor
                FROM ventas v
                LEFT JOIN usuarios u ON u.id = v.usuario_id
                WHERE v.id = @ventaId
                LIMIT 1;";

            await using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            cmd.Parameters.AddWithValue("@ventaId", ventaId);

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false))
            {
                return null;
            }

            return new VentaNotificacion
            {
                Folio = Convert.ToInt32(reader["folio"]),
                Total = Convert.ToDecimal(reader["total"]),
                Fecha = Convert.ToDateTime(reader["fecha_venta"]),
                Vendedor = reader["vendedor"].ToString() ?? string.Empty
            };
        }

        private static async Task<List<string>> ObtenerChatsActivosAsync()
        {
            List<string> chats = new List<string>();

            DatabaseConnection db = new DatabaseConnection();
            await using NpgsqlConnection conexion = db.GetConnection();
            await conexion.OpenAsync().ConfigureAwait(false);

            await AsegurarTablaDestinatariosAsync(conexion).ConfigureAwait(false);

            const string query = @"
                SELECT chat_id
                FROM telegram_destinatarios
                WHERE activo = true
                ORDER BY id;";

            await using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                string chatId = (reader["chat_id"].ToString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(chatId))
                {
                    chats.Add(chatId);
                }
            }

            return chats;
        }

        internal static async Task AsegurarTablaDestinatariosAsync(NpgsqlConnection conexion)
        {
            const string query = @"
                CREATE TABLE IF NOT EXISTS telegram_destinatarios (
                    id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    chat_id varchar(30) NOT NULL,
                    nombre varchar(120) NOT NULL DEFAULT '',
                    activo boolean NOT NULL DEFAULT true,
                    fecha_alta timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                );";

            await using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static string? LeerToken()
        {
            return Environment.GetEnvironmentVariable(TokenVariable, EnvironmentVariableTarget.Process)
                ?? Environment.GetEnvironmentVariable(TokenVariable, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(TokenVariable, EnvironmentVariableTarget.Machine);
        }

        private sealed class VentaNotificacion
        {
            public int Folio { get; set; }
            public decimal Total { get; set; }
            public DateTime Fecha { get; set; }
            public string Vendedor { get; set; } = string.Empty;
        }
    }

    public class TelegramChatDetectado
    {
        public string ChatId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }

    /// <summary>
    /// Producto que quedo en o por debajo de su stock minimo tras una venta.
    /// </summary>
    public class ProductoBajoStock
    {
        public string Nombre { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public decimal StockActual { get; set; }
        public decimal StockMinimo { get; set; }
    }
}

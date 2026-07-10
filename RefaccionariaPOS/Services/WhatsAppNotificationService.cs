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
    /// Envia un aviso por WhatsApp (Cloud API de Meta) cuando se registra una venta.
    /// El envio es "fire and forget": cualquier fallo se ignora para no afectar la venta.
    /// Requiere las variables de entorno REFAX_WHATSAPP_TOKEN y REFAX_WHATSAPP_PHONE_ID.
    /// Los numeros destino se configuran en la tabla whatsapp_destinatarios (sin recompilar).
    /// </summary>
    public static class WhatsAppNotificationService
    {
        private const string TokenVariable = "REFAX_WHATSAPP_TOKEN";
        private const string PhoneIdVariable = "REFAX_WHATSAPP_PHONE_ID";
        private const string TemplateVariable = "REFAX_WHATSAPP_TEMPLATE";
        private const string LanguageVariable = "REFAX_WHATSAPP_LANG";
        private const string GraphApiVersion = "v21.0";
        private const string PlantillaPorDefecto = "nueva_venta";
        private const string IdiomaPorDefecto = "es_MX";

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

        public static async Task NotificarVentaAsync(int ventaId)
        {
            try
            {
                Log($"--- Inicio aviso venta_id={ventaId} ---");
                string? token = LeerVariable(TokenVariable);
                string? phoneId = LeerVariable(PhoneIdVariable);

                if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(phoneId))
                {
                    // WhatsApp no esta configurado en esta instalacion: no se envia nada.
                    Log($"ABORTA: falta configuracion. token={(string.IsNullOrWhiteSpace(token) ? "FALTA" : "ok")}, phoneId={(string.IsNullOrWhiteSpace(phoneId) ? "FALTA" : "ok")}");
                    return;
                }

                VentaNotificacion? venta = await ObtenerDatosVentaAsync(ventaId).ConfigureAwait(false);
                if (venta == null)
                {
                    Log("ABORTA: no se encontro la venta en la base de datos.");
                    return;
                }

                List<string> destinatarios = await ObtenerDestinatariosActivosAsync().ConfigureAwait(false);
                Log($"Destinatarios activos: {destinatarios.Count} [{string.Join(", ", destinatarios)}]");
                if (destinatarios.Count == 0)
                {
                    Log("ABORTA: no hay numeros activos en whatsapp_destinatarios.");
                    return;
                }

                string plantilla = LeerVariable(TemplateVariable) ?? PlantillaPorDefecto;
                string idioma = LeerVariable(LanguageVariable) ?? IdiomaPorDefecto;

                string folio = venta.Folio.ToString(CultureInfo.InvariantCulture);
                string total = venta.Total.ToString("C", CulturaMexico);
                string vendedor = string.IsNullOrWhiteSpace(venta.Vendedor) ? "N/D" : venta.Vendedor;
                string fecha = venta.Fecha.ToString("dd/MM/yyyy HH:mm", CulturaMexico);

                string url = $"https://graph.facebook.com/{GraphApiVersion}/{phoneId}/messages";

                Log($"Plantilla='{plantilla}' idioma='{idioma}' folio={folio} total='{total}' vendedor='{vendedor}'");

                foreach (string numero in destinatarios)
                {
                    await EnviarPlantillaAsync(url, token, numero, plantilla, idioma, folio, total, vendedor, fecha)
                        .ConfigureAwait(false);
                }

                Log("--- Fin aviso ---");
            }
            catch (Exception ex)
            {
                // El aviso de WhatsApp nunca debe afectar la operacion de la caja.
                Log("EXCEPCION general: " + ex.Message);
            }
        }

        private static async Task EnviarPlantillaAsync(
            string url,
            string token,
            string numero,
            string plantilla,
            string idioma,
            string folio,
            string total,
            string vendedor,
            string fecha)
        {
            try
            {
                var cuerpo = new
                {
                    messaging_product = "whatsapp",
                    to = numero,
                    type = "template",
                    template = new
                    {
                        name = plantilla,
                        language = new { code = idioma },
                        components = new[]
                        {
                            new
                            {
                                type = "body",
                                parameters = new[]
                                {
                                    new { type = "text", text = folio },
                                    new { type = "text", text = total },
                                    new { type = "text", text = vendedor },
                                    new { type = "text", text = fecha }
                                }
                            }
                        }
                    }
                };

                string json = JsonSerializer.Serialize(cuerpo);

                using HttpRequestMessage solicitud = new HttpRequestMessage(HttpMethod.Post, url);
                solicitud.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                solicitud.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using HttpResponseMessage respuesta = await httpClient.SendAsync(solicitud).ConfigureAwait(false);
                string contenido = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (respuesta.IsSuccessStatusCode)
                {
                    Log($"OK -> {numero}: {(int)respuesta.StatusCode} {contenido}");
                }
                else
                {
                    Log($"ERROR -> {numero}: {(int)respuesta.StatusCode} {contenido}");
                }
            }
            catch (Exception ex)
            {
                // Ignorar fallos por destinatario, pero registrarlos.
                Log($"EXCEPCION -> {numero}: {ex.Message}");
            }
        }

        private static void Log(string mensaje)
        {
            try
            {
                string carpeta = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RefaxManager");
                Directory.CreateDirectory(carpeta);
                string ruta = Path.Combine(carpeta, "whatsapp.log");
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

        private static async Task<List<string>> ObtenerDestinatariosActivosAsync()
        {
            List<string> numeros = new List<string>();

            DatabaseConnection db = new DatabaseConnection();
            await using NpgsqlConnection conexion = db.GetConnection();
            await conexion.OpenAsync().ConfigureAwait(false);

            await AsegurarTablaDestinatariosAsync(conexion).ConfigureAwait(false);

            const string query = @"
                SELECT numero
                FROM whatsapp_destinatarios
                WHERE activo = true
                ORDER BY id;";

            await using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                string numero = (reader["numero"].ToString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(numero))
                {
                    numeros.Add(numero);
                }
            }

            return numeros;
        }

        private static async Task AsegurarTablaDestinatariosAsync(NpgsqlConnection conexion)
        {
            const string query = @"
                CREATE TABLE IF NOT EXISTS whatsapp_destinatarios (
                    id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    numero varchar(20) NOT NULL,
                    nombre varchar(120) NOT NULL DEFAULT '',
                    activo boolean NOT NULL DEFAULT true,
                    fecha_alta timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                );";

            await using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static string? LeerVariable(string nombre)
        {
            return Environment.GetEnvironmentVariable(nombre, EnvironmentVariableTarget.Process)
                ?? Environment.GetEnvironmentVariable(nombre, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(nombre, EnvironmentVariableTarget.Machine);
        }

        private sealed class VentaNotificacion
        {
            public int Folio { get; set; }
            public decimal Total { get; set; }
            public DateTime Fecha { get; set; }
            public string Vendedor { get; set; } = string.Empty;
        }
    }
}

using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RefaccionariaPOS.Services
{
    /// <summary>
    /// Plantilla editable de un ticket: encabezado (datos del negocio), pie
    /// (mensajes finales) y ancho del papel en caracteres. El cuerpo es una
    /// estructura interna fija con marcadores {dato} que el sistema rellena
    /// con la venta o el corte, ajustada al ancho elegido.
    /// </summary>
    public class TicketPlantilla
    {
        public string Tipo { get; set; } = TicketPlantillaService.TipoVenta;
        public string Encabezado { get; set; } = string.Empty;
        public string Cuerpo { get; set; } = string.Empty;
        public string Pie { get; set; } = string.Empty;
        public int AnchoCaracteres { get; set; } = TicketPlantillaService.AnchoPredeterminado;

        public List<string> LineasEncabezado => PartirLineas(Encabezado);
        public List<string> LineasPie => PartirLineas(Pie);

        private static List<string> PartirLineas(string texto)
        {
            return (texto ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(linea => linea.TrimEnd())
                .ToList();
        }
    }

    /// <summary>
    /// Guarda y recupera las plantillas de los tickets. La fuente principal es la
    /// tabla ticket_plantillas en la base (para que todas las cajas impriman igual)
    /// con una copia local en LocalAppData para poder imprimir sin internet.
    /// La lectura nunca lanza excepciones: si todo falla regresa la plantilla
    /// predeterminada para no afectar la impresion.
    /// </summary>
    public static class TicketPlantillaService
    {
        public const string TipoVenta = "venta";
        public const string TipoCorte = "corte";

        public const int AnchoPredeterminado = 40;
        public const int AnchoMinimo = 24;
        public const int AnchoMaximo = 64;

        public static int LimitarAncho(int ancho)
        {
            return ancho <= 0 ? AnchoPredeterminado : Math.Clamp(ancho, AnchoMinimo, AnchoMaximo);
        }

        private static readonly object SyncRoot = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        private static string RutaCache
        {
            get
            {
                string carpeta = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RefaxManager",
                    "Offline");
                Directory.CreateDirectory(carpeta);
                return Path.Combine(carpeta, "ticket_plantillas.json");
            }
        }

        public static string CuerpoPredeterminado(string tipo)
        {
            if (tipo == TipoCorte)
            {
                return string.Join("\n",
                    "{linea}",
                    "",
                    "TIPO DE CORTE: {tipocorte}",
                    "FECHA: {fecha}",
                    "USUARIO: {usuario}",
                    "CAJA: {caja}",
                    "PERIODO: {periodo}",
                    "",
                    "VENTAS TOTALES: {ventas}",
                    "TOTAL VENDIDO: {totalvendido}",
                    "PAGOS PROV.: {pagosprov}",
                    "",
                    "PAGOS REALIZADOS",
                    "{linea}",
                    "{pagos}",
                    "{linea}",
                    "",
                    "VENTA POR CATEGORIA",
                    "{linea}",
                    "{categorias}",
                    "{linea}");
            }

            return string.Join("\n",
                "^{fecha}",
                "CAJERO:~{cajero}",
                "FOLIO:~{folio}",
                "",
                "CANT. DESCRIPCION~IMPORTE",
                "{lineadoble}",
                "{partidas}",
                "",
                "^NO. DE ARTICULOS: {articulos}",
                "^TOTAL: {total}",
                "^PAGO CON: {pagocon}",
                "^SU CAMBIO: {cambio}");
        }

        public static TicketPlantilla ObtenerPredeterminada(string tipo)
        {
            if (tipo == TipoCorte)
            {
                return new TicketPlantilla
                {
                    Tipo = TipoCorte,
                    Encabezado = "REFACCIONARIA NORTE\nAV. DIVICION DEL NORTE N.63 COL. CENTRO",
                    Cuerpo = CuerpoPredeterminado(TipoCorte),
                    Pie = "Fin del corte",
                    AnchoCaracteres = AnchoPredeterminado
                };
            }

            return new TicketPlantilla
            {
                Tipo = TipoVenta,
                Encabezado = "REFACCIONARIA\nNORTE\nAV.DIVICION DEL NORTE N.63\nCOL. CENTRO\n271 104 3233",
                Cuerpo = CuerpoPredeterminado(TipoVenta),
                Pie = "GRACIAS POR SU COMPRA\nVUELVA PRONTO",
                AnchoCaracteres = AnchoPredeterminado
            };
        }

        /// <summary>
        /// El cuerpo no se guarda: siempre se usa la estructura del sistema.
        /// Tambien completa el ancho en plantillas guardadas antes de que fuera
        /// configurable.
        /// </summary>
        private static TicketPlantilla Normalizar(TicketPlantilla plantilla)
        {
            plantilla.Cuerpo = CuerpoPredeterminado(plantilla.Tipo);
            plantilla.AnchoCaracteres = LimitarAncho(plantilla.AnchoCaracteres);
            return plantilla;
        }

        public static TicketPlantilla ObtenerPlantilla(string tipo)
        {
            try
            {
                if (EstadoConexion.DebeIntentarOnline)
                {
                    try
                    {
                        TicketPlantilla? deBase = LeerDeBase(tipo);
                        EstadoConexion.MarcarExito();
                        if (deBase != null)
                        {
                            Normalizar(deBase);
                            GuardarEnCache(deBase);
                            return deBase;
                        }

                        // La base respondio pero no hay plantilla guardada: se usa la predeterminada.
                        return ObtenerPredeterminada(tipo);
                    }
                    catch (Exception ex) when (EstadoConexion.EsErrorDeConexion(ex))
                    {
                        EstadoConexion.MarcarFalla();
                    }
                }

                TicketPlantilla? deCache = LeerDeCache(tipo);
                return deCache != null ? Normalizar(deCache) : ObtenerPredeterminada(tipo);
            }
            catch
            {
                return ObtenerPredeterminada(tipo);
            }
        }

        /// <summary>
        /// Guarda la plantilla en la base y en la copia local. Regresa true si quedo
        /// guardada en la base; false si solo quedo en esta computadora (sin conexion).
        /// </summary>
        public static bool GuardarPlantilla(TicketPlantilla plantilla)
        {
            GuardarEnCache(plantilla);

            try
            {
                GuardarEnBase(plantilla);
                EstadoConexion.MarcarExito();
                return true;
            }
            catch (Exception ex) when (EstadoConexion.EsErrorDeConexion(ex))
            {
                EstadoConexion.MarcarFalla();
                return false;
            }
        }

        private static TicketPlantilla? LeerDeBase(string tipo)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();
                AsegurarTabla(conexion);

                const string query = @"
                    SELECT tipo, encabezado, pie, ancho_caracteres
                    FROM ticket_plantillas
                    WHERE tipo = @tipo
                    LIMIT 1;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@tipo", tipo);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return null;
                        }

                        return new TicketPlantilla
                        {
                            Tipo = reader["tipo"].ToString() ?? tipo,
                            Encabezado = reader["encabezado"].ToString() ?? string.Empty,
                            Pie = reader["pie"].ToString() ?? string.Empty,
                            AnchoCaracteres = Convert.ToInt32(reader["ancho_caracteres"])
                        };
                    }
                }
            }
        }

        private static void GuardarEnBase(TicketPlantilla plantilla)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();
                AsegurarTabla(conexion);

                const string query = @"
                    INSERT INTO ticket_plantillas (tipo, encabezado, pie, ancho_caracteres, fecha_actualizacion)
                    VALUES (@tipo, @encabezado, @pie, @ancho, CURRENT_TIMESTAMP)
                    ON CONFLICT (tipo) DO UPDATE
                    SET encabezado = EXCLUDED.encabezado,
                        pie = EXCLUDED.pie,
                        ancho_caracteres = EXCLUDED.ancho_caracteres,
                        fecha_actualizacion = CURRENT_TIMESTAMP;";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@tipo", plantilla.Tipo);
                    cmd.Parameters.AddWithValue("@encabezado", plantilla.Encabezado);
                    cmd.Parameters.AddWithValue("@pie", plantilla.Pie);
                    cmd.Parameters.AddWithValue("@ancho", LimitarAncho(plantilla.AnchoCaracteres));
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void AsegurarTabla(NpgsqlConnection conexion)
        {
            const string query = @"
                CREATE TABLE IF NOT EXISTS ticket_plantillas (
                    tipo varchar(20) PRIMARY KEY,
                    encabezado text NOT NULL DEFAULT '',
                    pie text NOT NULL DEFAULT '',
                    fecha_actualizacion timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                ALTER TABLE ticket_plantillas
                    ADD COLUMN IF NOT EXISTS ancho_caracteres integer NOT NULL DEFAULT 40;";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static TicketPlantilla? LeerDeCache(string tipo)
        {
            lock (SyncRoot)
            {
                if (!File.Exists(RutaCache))
                {
                    return null;
                }

                Dictionary<string, TicketPlantilla>? plantillas =
                    JsonSerializer.Deserialize<Dictionary<string, TicketPlantilla>>(File.ReadAllText(RutaCache));
                return plantillas != null && plantillas.TryGetValue(tipo, out TicketPlantilla? plantilla)
                    ? plantilla
                    : null;
            }
        }

        private static void GuardarEnCache(TicketPlantilla plantilla)
        {
            try
            {
                lock (SyncRoot)
                {
                    Dictionary<string, TicketPlantilla> plantillas = new();
                    if (File.Exists(RutaCache))
                    {
                        plantillas = JsonSerializer.Deserialize<Dictionary<string, TicketPlantilla>>(File.ReadAllText(RutaCache))
                            ?? new Dictionary<string, TicketPlantilla>();
                    }

                    plantillas[plantilla.Tipo] = plantilla;
                    File.WriteAllText(RutaCache, JsonSerializer.Serialize(plantillas, JsonOptions));
                }
            }
            catch
            {
                // La copia local nunca debe romper el guardado ni la impresion.
            }
        }
    }
}

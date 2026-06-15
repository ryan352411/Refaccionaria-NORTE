using System.Globalization;
using System.Text.Json;
using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DatabaseConnection>();
builder.Services.AddScoped<CorteCajaRepository>();

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

using (IServiceScope scope = app.Services.CreateScope())
{
    CorteCajaRepository repository = scope.ServiceProvider.GetRequiredService<CorteCajaRepository>();
    await repository.EnsureSchemaAsync();
}

app.MapGet("/api/health", () => Results.Ok(new { ok = true, app = "CorteCajaMovil" }));

app.MapGet("/api/corte", async (string? fecha, CorteCajaRepository repository) =>
{
    DateOnly date = ParseDateOrToday(fecha);
    CorteCajaDashboard dashboard = await repository.GetDashboardAsync(date);
    return Results.Ok(dashboard);
});

app.MapPost("/api/cortes", async (GuardarCorteRequest request, CorteCajaRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.Responsable))
    {
        return Results.BadRequest(new { message = "Escribe el nombre del responsable." });
    }

    DateOnly date = ParseDateOrToday(request.Fecha);
    CorteCajaDashboard dashboard = await repository.GetDashboardAsync(date);
    CorteGuardado saved = await repository.GuardarCorteAsync(date, request, dashboard.Dia);
    return Results.Created($"/api/cortes/{saved.Id}", saved);
});

app.MapFallbackToFile("index.html");

app.Run();

static DateOnly ParseDateOrToday(string? fecha)
{
    if (DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
    {
        return parsed;
    }

    return DateOnly.FromDateTime(DateTime.Today);
}

public sealed class DatabaseConnection
{
    private const string PrimaryConnectionEnvironmentVariable = "REFACCIONARIA_NUEVA_DB_CONNECTION";
    private const string ConnectionEnvironmentVariable = "REFACCIONARIA_DB_CONNECTION";

    private readonly string connectionString = BuildConnectionString(GetConfiguredConnectionString());

    public NpgsqlDataSource CreateDataSource()
    {
        return NpgsqlDataSource.Create(connectionString);
    }

    private static string? GetConfiguredConnectionString()
    {
        return Environment.GetEnvironmentVariable(PrimaryConnectionEnvironmentVariable, EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable(PrimaryConnectionEnvironmentVariable, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(PrimaryConnectionEnvironmentVariable, EnvironmentVariableTarget.Machine)
            ?? Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable, EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable, EnvironmentVariableTarget.Machine);
    }

    private static string BuildConnectionString(string? configuredConnectionString)
    {
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                $"Configura la variable de entorno {PrimaryConnectionEnvironmentVariable} con la cadena de conexion de Neon.");
        }

        if (!configuredConnectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            && !configuredConnectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            NpgsqlConnectionStringBuilder configuredBuilder = new NpgsqlConnectionStringBuilder(configuredConnectionString);
            ApplyNetworkDefaults(configuredBuilder);
            return configuredBuilder.ConnectionString;
        }

        Uri uri = new Uri(configuredConnectionString);
        string[] userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length != 2)
        {
            throw new InvalidOperationException("La cadena de conexion de Neon debe incluir usuario y contrasena.");
        }

        NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1]),
            SslMode = SslMode.Require
        };

        if (uri.Query.Contains("channel_binding=require", StringComparison.OrdinalIgnoreCase))
        {
            builder.ChannelBinding = ChannelBinding.Require;
        }

        ApplyNetworkDefaults(builder);
        return builder.ConnectionString;
    }

    private static void ApplyNetworkDefaults(NpgsqlConnectionStringBuilder builder)
    {
        builder.Pooling = true;
        builder.MinPoolSize = 0;
        builder.MaxPoolSize = 20;
        builder.ConnectionLifetime = 120;
        builder.ConnectionIdleLifetime = 30;
        builder.Timeout = 15;
        builder.CommandTimeout = 60;
        builder.KeepAlive = 30;
    }
}

public sealed class CorteCajaRepository(DatabaseConnection databaseConnection)
{
    private readonly DatabaseConnection databaseConnection = databaseConnection;

    public async Task EnsureSchemaAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS cortes_caja (
                id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                fecha_corte date NOT NULL,
                responsable varchar(120) NOT NULL,
                tickets integer NOT NULL DEFAULT 0,
                ventas_esperadas numeric(12, 2) NOT NULL DEFAULT 0,
                inversion numeric(12, 2) NOT NULL DEFAULT 0,
                utilidad numeric(12, 2) NOT NULL DEFAULT 0,
                fondo_inicial numeric(12, 2) NOT NULL DEFAULT 0,
                retiros numeric(12, 2) NOT NULL DEFAULT 0,
                efectivo_contado numeric(12, 2) NOT NULL DEFAULT 0,
                diferencia numeric(12, 2) NOT NULL DEFAULT 0,
                denominaciones jsonb NOT NULL DEFAULT '[]'::jsonb,
                notas text NOT NULL DEFAULT '',
                creado_en timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_cortes_caja_fecha ON cortes_caja (fecha_corte DESC, creado_en DESC);
            """;

        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<CorteCajaDashboard> GetDashboardAsync(DateOnly fecha)
    {
        DateTime inicioDia = fecha.ToDateTime(TimeOnly.MinValue);
        DateTime finDia = inicioDia.AddDays(1);
        DateTime inicioMes = new DateTime(fecha.Year, fecha.Month, 1);
        DateTime inicioAnio = new DateTime(fecha.Year, 1, 1);

        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();

        CortePeriodoResumen dia = await ObtenerResumenAsync(dataSource, inicioDia, finDia);
        CortePeriodoResumen mes = await ObtenerResumenAsync(dataSource, inicioMes, inicioMes.AddMonths(1));
        CortePeriodoResumen anio = await ObtenerResumenAsync(dataSource, inicioAnio, inicioAnio.AddYears(1));
        List<CorteOrigenResumen> origenes = await ObtenerResumenPorOrigenAsync(dataSource, inicioDia, finDia);
        CorteGuardado? ultimoCorte = await ObtenerUltimoCorteAsync(dataSource, fecha);

        return new CorteCajaDashboard(
            fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.Now,
            dia,
            mes,
            anio,
            origenes,
            ultimoCorte);
    }

    public async Task<CorteGuardado> GuardarCorteAsync(DateOnly fecha, GuardarCorteRequest request, CortePeriodoResumen resumenDia)
    {
        decimal efectivoContado = request.EfectivoContado > 0
            ? request.EfectivoContado
            : request.Denominaciones.Sum(item => item.Valor * item.Cantidad);
        decimal esperadoEnCaja = request.FondoInicial + resumenDia.Ventas - request.Retiros;
        decimal diferencia = efectivoContado - esperadoEnCaja;
        string denominacionesJson = JsonSerializer.Serialize(request.Denominaciones);

        const string sql = """
            INSERT INTO cortes_caja
                (fecha_corte, responsable, tickets, ventas_esperadas, inversion, utilidad,
                 fondo_inicial, retiros, efectivo_contado, diferencia, denominaciones, notas)
            VALUES
                (@fecha, @responsable, @tickets, @ventas, @inversion, @utilidad,
                 @fondo, @retiros, @contado, @diferencia, CAST(@denominaciones AS jsonb), @notas)
            RETURNING id, fecha_corte, responsable, tickets, ventas_esperadas, inversion, utilidad,
                      fondo_inicial, retiros, efectivo_contado, diferencia, notas, creado_en;
            """;

        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("@fecha", fecha);
        command.Parameters.AddWithValue("@responsable", request.Responsable.Trim());
        command.Parameters.AddWithValue("@tickets", resumenDia.Tickets);
        command.Parameters.AddWithValue("@ventas", resumenDia.Ventas);
        command.Parameters.AddWithValue("@inversion", resumenDia.Inversion);
        command.Parameters.AddWithValue("@utilidad", resumenDia.Utilidad);
        command.Parameters.AddWithValue("@fondo", request.FondoInicial);
        command.Parameters.AddWithValue("@retiros", request.Retiros);
        command.Parameters.AddWithValue("@contado", efectivoContado);
        command.Parameters.AddWithValue("@diferencia", diferencia);
        command.Parameters.AddWithValue("@denominaciones", denominacionesJson);
        command.Parameters.AddWithValue("@notas", request.Notas?.Trim() ?? string.Empty);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("No se pudo guardar el corte de caja.");
        }

        return MapCorte(reader);
    }

    private static async Task<CortePeriodoResumen> ObtenerResumenAsync(NpgsqlDataSource dataSource, DateTime inicio, DateTime fin)
    {
        const string sql = """
            WITH costo_por_venta AS (
                SELECT dv.venta_id, SUM(dv.cantidad * p.costo_proveedor) AS inversion
                FROM detalles_venta dv
                INNER JOIN productos p ON p.id = dv.producto_id
                GROUP BY dv.venta_id
            )
            SELECT COUNT(v.id) AS tickets,
                   COALESCE(SUM(v.total), 0) AS ventas,
                   COALESCE(SUM(c.inversion), 0) AS inversion
            FROM ventas v
            LEFT JOIN costo_por_venta c ON c.venta_id = v.id
            WHERE v.fecha_venta >= @inicio
              AND v.fecha_venta < @fin
              AND v.estado = 'Completada';
            """;

        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("@inicio", inicio);
        command.Parameters.AddWithValue("@fin", fin);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new CortePeriodoResumen(0, 0, 0);
        }

        return new CortePeriodoResumen(
            Convert.ToInt32(reader["tickets"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["ventas"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["inversion"], CultureInfo.InvariantCulture));
    }

    private static async Task<List<CorteOrigenResumen>> ObtenerResumenPorOrigenAsync(NpgsqlDataSource dataSource, DateTime inicio, DateTime fin)
    {
        const string sql = """
            WITH costo_por_venta AS (
                SELECT dv.venta_id, SUM(dv.cantidad * p.costo_proveedor) AS inversion
                FROM detalles_venta dv
                INNER JOIN productos p ON p.id = dv.producto_id
                GROUP BY dv.venta_id
            ),
            ventas_clasificadas AS (
                SELECT v.id,
                       v.total,
                       COALESCE(c.inversion, 0) AS inversion,
                       COALESCE(NULLIF(v.metodo_pago, ''), 'Mostrador') AS origen
                FROM ventas v
                LEFT JOIN costo_por_venta c ON c.venta_id = v.id
                WHERE v.fecha_venta >= @inicio
                  AND v.fecha_venta < @fin
                  AND v.estado = 'Completada'
            )
            SELECT origen,
                   COUNT(*) AS tickets,
                   COALESCE(SUM(total), 0) AS ventas,
                   COALESCE(SUM(inversion), 0) AS inversion
            FROM ventas_clasificadas
            GROUP BY origen
            ORDER BY ventas DESC, origen;
            """;

        List<CorteOrigenResumen> resumenes = [];
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("@inicio", inicio);
        command.Parameters.AddWithValue("@fin", fin);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            decimal ventas = Convert.ToDecimal(reader["ventas"], CultureInfo.InvariantCulture);
            decimal inversion = Convert.ToDecimal(reader["inversion"], CultureInfo.InvariantCulture);
            resumenes.Add(new CorteOrigenResumen(
                reader["origen"].ToString() ?? "Sin clasificar",
                Convert.ToInt32(reader["tickets"], CultureInfo.InvariantCulture),
                ventas,
                inversion,
                ventas - inversion));
        }

        return resumenes;
    }

    private static async Task<CorteGuardado?> ObtenerUltimoCorteAsync(NpgsqlDataSource dataSource, DateOnly fecha)
    {
        const string sql = """
            SELECT id, fecha_corte, responsable, tickets, ventas_esperadas, inversion, utilidad,
                   fondo_inicial, retiros, efectivo_contado, diferencia, notas, creado_en
            FROM cortes_caja
            WHERE fecha_corte = @fecha
            ORDER BY creado_en DESC
            LIMIT 1;
            """;

        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("@fecha", fecha);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapCorte(reader) : null;
    }

    private static CorteGuardado MapCorte(NpgsqlDataReader reader)
    {
        DateOnly fecha = reader["fecha_corte"] switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            object value => DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture))
        };
        return new CorteGuardado(
            Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
            fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader["responsable"].ToString() ?? string.Empty,
            Convert.ToInt32(reader["tickets"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["ventas_esperadas"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["inversion"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["utilidad"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["fondo_inicial"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["retiros"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["efectivo_contado"], CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader["diferencia"], CultureInfo.InvariantCulture),
            reader["notas"].ToString() ?? string.Empty,
            Convert.ToDateTime(reader["creado_en"], CultureInfo.InvariantCulture));
    }
}

public sealed record CorteCajaDashboard(
    string Fecha,
    DateTime GeneradoEn,
    CortePeriodoResumen Dia,
    CortePeriodoResumen Mes,
    CortePeriodoResumen Anio,
    IReadOnlyList<CorteOrigenResumen> Origenes,
    CorteGuardado? UltimoCorte);

public sealed record CortePeriodoResumen(int Tickets, decimal Ventas, decimal Inversion)
{
    public decimal Utilidad => Ventas - Inversion;
}

public sealed record CorteOrigenResumen(string Origen, int Tickets, decimal Ventas, decimal Inversion, decimal Utilidad);

public sealed record GuardarCorteRequest(
    string? Fecha,
    string Responsable,
    decimal FondoInicial,
    decimal Retiros,
    decimal EfectivoContado,
    IReadOnlyList<DenominacionContada> Denominaciones,
    string? Notas);

public sealed record DenominacionContada(decimal Valor, int Cantidad);

public sealed record CorteGuardado(
    int Id,
    string Fecha,
    string Responsable,
    int Tickets,
    decimal VentasEsperadas,
    decimal Inversion,
    decimal Utilidad,
    decimal FondoInicial,
    decimal Retiros,
    decimal EfectivoContado,
    decimal Diferencia,
    string Notas,
    DateTime CreadoEn);

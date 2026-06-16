using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("catalogo-publico", limiterOptions =>
    {
        limiterOptions.PermitLimit = 240;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
        limiterOptions.AutoReplenishment = true;
    });
});
builder.Services.AddSingleton<DatabaseConnection>();
builder.Services.AddScoped<CatalogoRepository>();

WebApplication app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

using (IServiceScope scope = app.Services.CreateScope())
{
    CatalogoRepository repository = scope.ServiceProvider.GetRequiredService<CatalogoRepository>();
    await repository.EnsureSchemaAsync();
}

app.MapGet("/api/health", () => Results.Ok(new { ok = true, app = "CatalogoWeb" }))
    .RequireRateLimiting("catalogo-publico");

app.MapGet("/api/catalogo/resumen", async (CatalogoRepository repository) =>
{
    CatalogoResumen resumen = await repository.GetResumenAsync();
    return Results.Ok(resumen);
}).RequireRateLimiting("catalogo-publico");

app.MapGet("/api/catalogo/categorias", async (CatalogoRepository repository) =>
{
    IReadOnlyList<CategoriaCatalogo> categorias = await repository.GetCategoriasAsync();
    return Results.Ok(categorias);
}).RequireRateLimiting("catalogo-publico");

app.MapGet("/api/catalogo/productos", async (
    string? buscar,
    string? categoria,
    bool soloDisponibles,
    int pagina,
    int limite,
    CatalogoRepository repository) =>
{
    CatalogoFiltro filtro = new(
        buscar?.Trim() ?? string.Empty,
        categoria?.Trim() ?? string.Empty,
        soloDisponibles,
        Math.Clamp(pagina, 1, 500),
        Math.Clamp(limite, 12, 96));

    CatalogoResultado resultado = await repository.GetProductosAsync(filtro);
    return Results.Ok(resultado);
}).RequireRateLimiting("catalogo-publico");

app.MapGet("/api/catalogo/imagenes/{id:int}", async Task<IResult> (int id, CatalogoRepository repository) =>
{
    ImagenProducto? imagen = await repository.GetImagenProductoAsync(id);
    if (imagen == null)
    {
        return Results.NotFound();
    }

    return Results.File(imagen.RutaArchivo, imagen.ContentType);
}).RequireRateLimiting("catalogo-publico");

app.MapFallbackToFile("index.html");

app.Run();

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

        Uri uri = new(configuredConnectionString);
        string[] userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length != 2)
        {
            throw new InvalidOperationException("La cadena de conexion de Neon debe incluir usuario y contrasena.");
        }

        NpgsqlConnectionStringBuilder builder = new()
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

public sealed class CatalogoRepository(DatabaseConnection databaseConnection)
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private readonly DatabaseConnection databaseConnection = databaseConnection;

    public async Task EnsureSchemaAsync()
    {
        const string sql = """
            ALTER TABLE productos
                ADD COLUMN IF NOT EXISTS imagen_url text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS tipo_venta varchar(20) NOT NULL DEFAULT 'Unidad';

            CREATE INDEX IF NOT EXISTS idx_productos_tipo_venta ON productos (tipo_venta);
            CREATE INDEX IF NOT EXISTS idx_productos_catalogo_busqueda ON productos USING btree (categoria, nombre);
            """;

        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<CatalogoResumen> GetResumenAsync()
    {
        const string sql = """
            SELECT COUNT(*) AS total,
                   COUNT(*) FILTER (WHERE stock_actual > 0) AS disponibles,
                   COUNT(DISTINCT categoria) AS categorias
            FROM productos;
            """;

        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new CatalogoResumen(0, 0, 0);
        }

        return new CatalogoResumen(
            Convert.ToInt32(reader["total"], CultureInfo.InvariantCulture),
            Convert.ToInt32(reader["disponibles"], CultureInfo.InvariantCulture),
            Convert.ToInt32(reader["categorias"], CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<CategoriaCatalogo>> GetCategoriasAsync()
    {
        const string sql = """
            SELECT categoria,
                   COUNT(*) AS total,
                   COUNT(*) FILTER (WHERE stock_actual > 0) AS disponibles
            FROM productos
            GROUP BY categoria
            ORDER BY categoria;
            """;

        List<CategoriaCatalogo> categorias = [];
        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            categorias.Add(new CategoriaCatalogo(
                reader["categoria"].ToString() ?? "General",
                Convert.ToInt32(reader["total"], CultureInfo.InvariantCulture),
                Convert.ToInt32(reader["disponibles"], CultureInfo.InvariantCulture)));
        }

        return categorias;
    }

    public async Task<CatalogoResultado> GetProductosAsync(CatalogoFiltro filtro)
    {
        int offset = (filtro.Pagina - 1) * filtro.Limite;
        string searchPattern = $"%{filtro.Buscar}%";

        const string countSql = """
            SELECT COUNT(*)
            FROM productos
            WHERE (@buscar = ''
                   OR nombre ILIKE @buscarPattern
                   OR descripcion ILIKE @buscarPattern
                   OR codigo_barras ILIKE @buscarPattern)
              AND (@categoria = '' OR categoria = @categoria)
              AND (@soloDisponibles = false OR stock_actual > 0);
            """;

        const string productsSql = """
            SELECT id, nombre, descripcion, precio_venta, stock_actual,
                   stock_minimo, categoria, imagen_url, tipo_venta
            FROM productos
            WHERE (@buscar = ''
                   OR nombre ILIKE @buscarPattern
                   OR descripcion ILIKE @buscarPattern
                   OR codigo_barras ILIKE @buscarPattern)
              AND (@categoria = '' OR categoria = @categoria)
              AND (@soloDisponibles = false OR stock_actual > 0)
            ORDER BY
                CASE WHEN stock_actual > 0 THEN 0 ELSE 1 END,
                CASE WHEN NULLIF(TRIM(imagen_url), '') IS NULL THEN 1 ELSE 0 END,
                categoria,
                nombre
            LIMIT @limite OFFSET @offset;
            """;

        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();

        int total = 0;
        await using (NpgsqlCommand countCommand = dataSource.CreateCommand(countSql))
        {
            AddFilterParameters(countCommand, filtro, searchPattern);
            object? countResult = await countCommand.ExecuteScalarAsync();
            total = Convert.ToInt32(countResult, CultureInfo.InvariantCulture);
        }

        List<ProductoCatalogo> productos = [];
        await using (NpgsqlCommand productsCommand = dataSource.CreateCommand(productsSql))
        {
            AddFilterParameters(productsCommand, filtro, searchPattern);
            productsCommand.Parameters.AddWithValue("@limite", filtro.Limite);
            productsCommand.Parameters.AddWithValue("@offset", offset);

            await using NpgsqlDataReader reader = await productsCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                productos.Add(MapProducto(reader));
            }
        }

        int totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)filtro.Limite);
        return new CatalogoResultado(productos, total, filtro.Pagina, totalPaginas);
    }

    public async Task<ImagenProducto?> GetImagenProductoAsync(int id)
    {
        const string sql = """
            SELECT imagen_url
            FROM productos
            WHERE id = @id
            LIMIT 1;
            """;

        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("@id", id);

        object? result = await command.ExecuteScalarAsync();
        string imagenUrl = result?.ToString() ?? string.Empty;
        string? localPath = ResolveLocalImagePath(imagenUrl);
        if (localPath == null)
        {
            return null;
        }

        string extension = Path.GetExtension(localPath);
        if (!ContentTypeProvider.TryGetContentType(localPath, out string? contentType))
        {
            contentType = extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                ? "image/webp"
                : "application/octet-stream";
        }

        return new ImagenProducto(localPath, contentType);
    }

    private static void AddFilterParameters(NpgsqlCommand command, CatalogoFiltro filtro, string searchPattern)
    {
        command.Parameters.AddWithValue("@buscar", filtro.Buscar);
        command.Parameters.AddWithValue("@buscarPattern", searchPattern);
        command.Parameters.AddWithValue("@categoria", filtro.Categoria);
        command.Parameters.AddWithValue("@soloDisponibles", filtro.SoloDisponibles);
    }

    private static ProductoCatalogo MapProducto(NpgsqlDataReader reader)
    {
        int id = Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture);
        decimal stockActual = Convert.ToDecimal(reader["stock_actual"], CultureInfo.InvariantCulture);
        decimal stockMinimo = Convert.ToDecimal(reader["stock_minimo"], CultureInfo.InvariantCulture);
        string imagenUrl = reader["imagen_url"].ToString() ?? string.Empty;
        string estado = stockActual <= 0
            ? "Agotado"
            : stockActual <= stockMinimo
                ? "Pocas piezas"
                : "Disponible";

        return new ProductoCatalogo(
            id,
            reader["nombre"].ToString() ?? string.Empty,
            reader["descripcion"].ToString() ?? string.Empty,
            reader["categoria"].ToString() ?? "General",
            BuildPublicImageUrl(id, imagenUrl),
            reader["tipo_venta"].ToString() ?? "Unidad",
            Convert.ToDecimal(reader["precio_venta"], CultureInfo.InvariantCulture),
            stockActual,
            estado);
    }

    private static string BuildPublicImageUrl(int id, string imageValue)
    {
        if (string.IsNullOrWhiteSpace(imageValue))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(imageValue.Trim(), UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return imageValue.Trim();
        }

        return $"/api/catalogo/imagenes/{id}";
    }

    private static string? ResolveLocalImagePath(string imageValue)
    {
        if (string.IsNullOrWhiteSpace(imageValue))
        {
            return null;
        }

        string value = imageValue.Trim().Trim('"');
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            if (!uri.IsFile)
            {
                return null;
            }

            value = uri.LocalPath;
        }

        string imageFolder = GetProductImageFolder();
        string candidate = Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(imageFolder, value));

        string folder = Path.GetFullPath(imageFolder);
        if (!candidate.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !candidate.Equals(folder, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string extension = Path.GetExtension(candidate);
        if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            return null;
        }

        return candidate;
    }

    private static string GetProductImageFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RefaxManager",
            "ImagenesProductos");
    }
}

public sealed record CatalogoFiltro(
    string Buscar,
    string Categoria,
    bool SoloDisponibles,
    int Pagina,
    int Limite);

public sealed record CatalogoResumen(int Total, int Disponibles, int Categorias);

public sealed record CategoriaCatalogo(string Nombre, int Total, int Disponibles);

public sealed record CatalogoResultado(
    IReadOnlyList<ProductoCatalogo> Productos,
    int Total,
    int Pagina,
    int TotalPaginas);

public sealed record ProductoCatalogo(
    int Id,
    string Nombre,
    string Descripcion,
    string Categoria,
    string ImagenUrl,
    string TipoVenta,
    decimal PrecioVenta,
    decimal StockActual,
    string Estado);

public sealed record ImagenProducto(string RutaArchivo, string ContentType);

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = BuildContentSecurityPolicy(context.Request);
            headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            await next();
        });
    }

    private static string BuildContentSecurityPolicy(HttpRequest request)
    {
        string policy = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' https: data:";
        return request.IsHttps ? $"{policy}; upgrade-insecure-requests" : policy;
    }
}

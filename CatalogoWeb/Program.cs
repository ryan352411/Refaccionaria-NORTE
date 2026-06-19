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

    return imagen.Data is { Length: > 0 }
        ? Results.File(imagen.Data, imagen.ContentType)
        : Results.File(imagen.RutaArchivo!, imagen.ContentType);
}).RequireRateLimiting("catalogo-publico");

app.MapGet("/api/catalogo/imagenes/{id:int}/{indice:int}", async Task<IResult> (int id, int indice, CatalogoRepository repository) =>
{
    ImagenProducto? imagen = await repository.GetImagenProductoAsync(id, indice);
    if (imagen == null)
    {
        return Results.NotFound();
    }

    return imagen.Data is { Length: > 0 }
        ? Results.File(imagen.Data, imagen.ContentType)
        : Results.File(imagen.RutaArchivo!, imagen.ContentType);
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

            CREATE TABLE IF NOT EXISTS producto_imagenes (
                id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                producto_id integer NOT NULL REFERENCES productos(id) ON DELETE CASCADE,
                orden integer NOT NULL DEFAULT 0,
                imagen_url text NOT NULL DEFAULT '',
                imagen_data bytea NULL,
                content_type varchar(100) NOT NULL DEFAULT '',
                file_name varchar(255) NOT NULL DEFAULT '',
                fecha_alta timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                fecha_actualizacion timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE producto_imagenes
                ADD COLUMN IF NOT EXISTS orden integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS imagen_url text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS imagen_data bytea NULL,
                ADD COLUMN IF NOT EXISTS content_type varchar(100) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS file_name varchar(255) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS fecha_alta timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ADD COLUMN IF NOT EXISTS fecha_actualizacion timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP;

            DO $$
            DECLARE
                constraint_name text;
            BEGIN
                FOR constraint_name IN
                    SELECT con.conname
                    FROM pg_constraint con
                    JOIN pg_class rel ON rel.oid = con.conrelid
                    JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
                    WHERE nsp.nspname = 'public'
                      AND rel.relname = 'producto_imagenes'
                      AND con.contype = 'u'
                      AND pg_get_constraintdef(con.oid) = 'UNIQUE (producto_id)'
                LOOP
                    EXECUTE format('ALTER TABLE producto_imagenes DROP CONSTRAINT IF EXISTS %I', constraint_name);
                END LOOP;
            END $$;

            INSERT INTO producto_imagenes (producto_id, imagen_url, orden)
            SELECT id, imagen_url
            FROM productos
            WHERE COALESCE(imagen_url, '') <> ''
              AND NOT EXISTS (
                  SELECT 1
                  FROM producto_imagenes pi
                  WHERE pi.producto_id = productos.id
              );

            CREATE INDEX IF NOT EXISTS idx_productos_tipo_venta ON productos (tipo_venta);
            CREATE INDEX IF NOT EXISTS idx_productos_catalogo_busqueda ON productos USING btree (categoria, nombre);
            CREATE INDEX IF NOT EXISTS idx_producto_imagenes_producto_id ON producto_imagenes (producto_id);
            CREATE INDEX IF NOT EXISTS idx_producto_imagenes_producto_orden ON producto_imagenes (producto_id, orden, id);
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
            SELECT p.id, p.nombre, p.descripcion, p.precio_venta, p.stock_actual,
                   p.stock_minimo, p.categoria,
                   COALESCE(p.imagen_url, '') AS imagen_url,
                   p.tipo_venta
            FROM productos p
            WHERE (@buscar = ''
                   OR p.nombre ILIKE @buscarPattern
                   OR p.descripcion ILIKE @buscarPattern
                   OR p.codigo_barras ILIKE @buscarPattern)
              AND (@categoria = '' OR p.categoria = @categoria)
              AND (@soloDisponibles = false OR p.stock_actual > 0)
            ORDER BY
                CASE WHEN p.stock_actual > 0 THEN 0 ELSE 1 END,
                CASE WHEN EXISTS (
                          SELECT 1
                          FROM producto_imagenes pi
                          WHERE pi.producto_id = p.id
                            AND (COALESCE(octet_length(pi.imagen_data), 0) > 0
                                 OR NULLIF(TRIM(pi.imagen_url), '') IS NOT NULL)
                      )
                          OR NULLIF(TRIM(COALESCE(p.imagen_url, '')), '') IS NOT NULL
                     THEN 0 ELSE 1 END,
                p.categoria,
                p.nombre
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

        for (int index = 0; index < productos.Count; index++)
        {
            ProductoCatalogo producto = productos[index];
            IReadOnlyList<string> imagenes = await GetImagenUrlsAsync(dataSource, producto.Id, producto.ImagenUrl);
            productos[index] = producto with
            {
                ImagenUrl = imagenes.FirstOrDefault() ?? string.Empty,
                Imagenes = imagenes
            };
        }

        int totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)filtro.Limite);
        return new CatalogoResultado(productos, total, filtro.Pagina, totalPaginas);
    }

    public async Task<ImagenProducto?> GetImagenProductoAsync(int id, int indice = 0)
    {
        const string sql = """
            SELECT imagen_url, imagen_data, content_type
            FROM (
                SELECT pi.imagen_url, pi.imagen_data, COALESCE(pi.content_type, '') AS content_type,
                       pi.orden, pi.id, 0 AS source_order
                FROM producto_imagenes pi
                WHERE pi.producto_id = @id

                UNION ALL

                SELECT COALESCE(p.imagen_url, '') AS imagen_url, NULL::bytea AS imagen_data,
                       '' AS content_type, 0 AS orden, 0 AS id, 1 AS source_order
                FROM productos p
                WHERE p.id = @id
                  AND COALESCE(p.imagen_url, '') <> ''
                  AND NOT EXISTS (
                      SELECT 1
                      FROM producto_imagenes pi
                      WHERE pi.producto_id = p.id
                  )
            ) imagenes
            ORDER BY source_order, orden, id
            OFFSET @indice
            LIMIT 1;
            """;

        await using NpgsqlDataSource dataSource = databaseConnection.CreateDataSource();
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@indice", Math.Max(0, indice));

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        byte[]? imagenData = reader["imagen_data"] is DBNull ? null : (byte[])reader["imagen_data"];
        string contentType = reader["content_type"].ToString() ?? string.Empty;
        if (imagenData is { Length: > 0 })
        {
            return new ImagenProducto(null, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, imagenData);
        }

        string imagenUrl = reader["imagen_url"].ToString() ?? string.Empty;
        string? localPath = ResolveLocalImagePath(imagenUrl);
        if (localPath == null)
        {
            return null;
        }

        string extension = Path.GetExtension(localPath);
        if (!ContentTypeProvider.TryGetContentType(localPath, out string? localContentType))
        {
            localContentType = extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                ? "image/webp"
                : "application/octet-stream";
        }

        return new ImagenProducto(localPath, localContentType, null);
    }

    private async Task<IReadOnlyList<string>> GetImagenUrlsAsync(NpgsqlDataSource dataSource, int id, string fallbackImageValue)
    {
        const string sql = """
            SELECT imagen_url, COALESCE(octet_length(imagen_data), 0) > 0 AS tiene_imagen_data
            FROM producto_imagenes
            WHERE producto_id = @id
            ORDER BY orden, id;
            """;

        List<string> imagenes = [];
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("@id", id);

        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            int indice = 0;
            while (await reader.ReadAsync())
            {
                string imagenUrl = reader["imagen_url"].ToString() ?? string.Empty;
                bool tieneImagenData = reader["tiene_imagen_data"] is bool valorTieneImagen && valorTieneImagen;
                string urlPublica = BuildPublicImageUrl(id, indice, imagenUrl, tieneImagenData);
                if (!string.IsNullOrWhiteSpace(urlPublica))
                {
                    imagenes.Add(urlPublica);
                }

                indice++;
            }
        }

        if (imagenes.Count == 0)
        {
            string urlFallback = BuildPublicImageUrl(id, 0, fallbackImageValue, hasImageData: false);
            if (!string.IsNullOrWhiteSpace(urlFallback))
            {
                imagenes.Add(urlFallback);
            }
        }

        return imagenes;
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
            imagenUrl,
            [],
            reader["tipo_venta"].ToString() ?? "Unidad",
            Convert.ToDecimal(reader["precio_venta"], CultureInfo.InvariantCulture),
            stockActual,
            estado);
    }

    private static string BuildPublicImageUrl(int id, int indice, string imageValue, bool hasImageData)
    {
        if (hasImageData)
        {
            return $"/api/catalogo/imagenes/{id}/{indice}";
        }

        if (string.IsNullOrWhiteSpace(imageValue))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(imageValue.Trim(), UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return imageValue.Trim();
        }

        return $"/api/catalogo/imagenes/{id}/{indice}";
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
    IReadOnlyList<string> Imagenes,
    string TipoVenta,
    decimal PrecioVenta,
    decimal StockActual,
    string Estado);

public sealed record ImagenProducto(string? RutaArchivo, string ContentType, byte[]? Data);

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

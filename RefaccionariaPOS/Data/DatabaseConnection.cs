using Npgsql;
using System;

namespace RefaccionariaPOS.Data
{
    public class DatabaseConnection
    {
        private const string ConnectionEnvironmentVariable = "REFACCIONARIA_DB_CONNECTION";

        private readonly string connectionString =
            BuildConnectionString(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable));

        public NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(connectionString);
        }

        private static string BuildConnectionString(string? configuredConnectionString)
        {
            if (string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                throw new InvalidOperationException(
                    $"Configura la variable de entorno {ConnectionEnvironmentVariable} con la cadena de conexion de Neon.");
            }

            if (!configuredConnectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
                && !configuredConnectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
            {
                return configuredConnectionString;
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
                SslMode = SslMode.Require,
                Pooling = false
            };

            if (uri.Query.Contains("channel_binding=require", StringComparison.OrdinalIgnoreCase))
            {
                builder.ChannelBinding = ChannelBinding.Require;
            }

            return builder.ConnectionString;
        }
    }
}

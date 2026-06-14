using Npgsql;
using System;

namespace RefaccionariaPOS.Data
{
    public class DatabaseConnection
    {
        private const string PrimaryConnectionEnvironmentVariable = "REFACCIONARIA_NUEVA_DB_CONNECTION";
        private const string ConnectionEnvironmentVariable = "REFACCIONARIA_DB_CONNECTION";

        private readonly string connectionString =
            BuildConnectionString(GetConfiguredConnectionString());

        public NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(connectionString);
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
                SslMode = SslMode.Require,
                Pooling = false
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
            builder.Pooling = false;
            builder.Timeout = 15;
            builder.CommandTimeout = 60;
            builder.KeepAlive = 30;
        }
    }
}

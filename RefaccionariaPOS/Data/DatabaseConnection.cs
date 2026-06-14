using Npgsql;
using System;

namespace RefaccionariaPOS.Data
{
    public class DatabaseConnection
    {
        private const string ConnectionEnvironmentVariable = "REFACCIONARIA_DB_CONNECTION";

        private readonly string connectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Configura la variable de entorno {ConnectionEnvironmentVariable} con la cadena de conexion de Neon.");

        public NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(connectionString);
        }
    }
}

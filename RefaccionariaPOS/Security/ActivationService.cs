using Microsoft.Win32;
using Npgsql;
using System;
using System.Reflection;

namespace RefaccionariaPOS.Security
{
    public static class ActivationService
    {
        private const string AppCode = "REFACCIONARIA_NUEVA";
        private const string AppName = "RefaxManager";
        private const string RegistryPath = @"Software\ServicioAutomotrizLopezSuite\Licensing\RefaxManager";
        private const string InstallationIdValue = "InstallationId";
        private const string LastKnownActiveValue = "LastKnownActive";
        private const string LastValidationValue = "LastValidationUtc";
        private const string LicensingConnectionEnvironmentVariable = "REFACCIONARIA_LICENSE_DB_CONNECTION";
        private const string SharedConnectionEnvironmentVariable = "REFACCIONARIA_DB_CONNECTION";
        private const string AppConnectionEnvironmentVariable = "REFACCIONARIA_NUEVA_DB_CONNECTION";
        private static readonly TimeSpan ActiveLicenseCacheDuration = TimeSpan.FromDays(7);

        public static bool Validate(out string message)
        {
            if (HasRecentActiveValidation())
            {
                message = "Instalacion activa.";
                return true;
            }

            try
            {
                Guid installationId = GetOrCreateInstallationId();
                bool isActive = RegisterInstallationAndReadActivation(installationId);

                if (!isActive)
                {
                    SaveActivationState(isActive);
                    message = "Esta instalacion de RefaxManager fue desactivada desde el panel web. Contacta al proveedor.";
                    return false;
                }

                SaveActivationState(isActive);
                message = "Instalacion activa.";
                return true;
            }
            catch (Exception ex)
            {
                if (WasLastKnownInactive() && !WasLastKnownActive())
                {
                    message = "Esta instalacion de RefaxManager fue desactivada desde el panel web. Contacta al proveedor.";
                    return false;
                }

                if (WasLastKnownActive())
                {
                    message = "Instalacion activa temporalmente.";
                    return true;
                }

                message = "No se pudo contactar el panel de licencias. Se permite el acceso temporalmente: " + ex.Message;
                return true;
            }
        }

        private static bool RegisterInstallationAndReadActivation(Guid installationId)
        {
            string machineName = Environment.MachineName;
            string windowsUser = Environment.UserName;
            string appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

            using NpgsqlConnection connection = new NpgsqlConnection(BuildConnectionString(GetConfiguredConnectionString()));
            connection.Open();
            using NpgsqlTransaction transaction = connection.BeginTransaction();

            const string upsertSql = @"
                INSERT INTO app_installations
                    (installation_id, app_code, app_name, machine_name, windows_user, app_version, first_seen_at, last_seen_at, launch_count, is_active)
                VALUES
                    (@installationId, @appCode, @appName, @machineName, @windowsUser, @appVersion, now(), now(), 1, true)
                ON CONFLICT (installation_id) DO UPDATE
                SET app_code = EXCLUDED.app_code,
                    app_name = EXCLUDED.app_name,
                    machine_name = EXCLUDED.machine_name,
                    windows_user = EXCLUDED.windows_user,
                    app_version = EXCLUDED.app_version,
                    last_seen_at = now(),
                    launch_count = app_installations.launch_count + 1
                RETURNING is_active;";

            bool isActive;
            using (NpgsqlCommand command = new NpgsqlCommand(upsertSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@installationId", installationId);
                command.Parameters.AddWithValue("@appCode", AppCode);
                command.Parameters.AddWithValue("@appName", AppName);
                command.Parameters.AddWithValue("@machineName", machineName);
                command.Parameters.AddWithValue("@windowsUser", windowsUser);
                command.Parameters.AddWithValue("@appVersion", appVersion);
                isActive = Convert.ToBoolean(command.ExecuteScalar());
            }

            const string eventSql = @"
                INSERT INTO app_installation_events
                    (installation_id, app_code, event_type, machine_name, windows_user)
                VALUES
                    (@installationId, @appCode, 'launch', @machineName, @windowsUser);";

            using (NpgsqlCommand command = new NpgsqlCommand(eventSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@installationId", installationId);
                command.Parameters.AddWithValue("@appCode", AppCode);
                command.Parameters.AddWithValue("@machineName", machineName);
                command.Parameters.AddWithValue("@windowsUser", windowsUser);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return isActive;
        }

        private static Guid GetOrCreateInstallationId()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            string? storedValue = key.GetValue(InstallationIdValue)?.ToString();

            if (Guid.TryParse(storedValue, out Guid installationId))
            {
                return installationId;
            }

            installationId = Guid.NewGuid();
            key.SetValue(InstallationIdValue, installationId.ToString(), RegistryValueKind.String);
            return installationId;
        }

        private static void SaveActivationState(bool isActive)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue(LastKnownActiveValue, isActive ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(LastValidationValue, DateTime.UtcNow.ToString("O"), RegistryValueKind.String);
        }

        private static bool WasLastKnownInactive()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            object? storedValue = key.GetValue(LastKnownActiveValue);
            return storedValue is int activeValue && activeValue == 0;
        }

        private static bool WasLastKnownActive()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            object? storedValue = key.GetValue(LastKnownActiveValue);
            return storedValue is int activeValue && activeValue == 1;
        }

        private static bool HasRecentActiveValidation()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            object? activeValue = key.GetValue(LastKnownActiveValue);
            string? lastValidationText = key.GetValue(LastValidationValue)?.ToString();

            if (activeValue is not int active || active != 1)
            {
                return false;
            }

            if (!DateTime.TryParse(lastValidationText, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime lastValidationUtc))
            {
                return false;
            }

            TimeSpan elapsed = DateTime.UtcNow - lastValidationUtc.ToUniversalTime();
            return elapsed >= TimeSpan.Zero && elapsed <= ActiveLicenseCacheDuration;
        }

        private static string? GetConfiguredConnectionString()
        {
            return GetEnvironmentVariable(LicensingConnectionEnvironmentVariable)
                ?? GetEnvironmentVariable(SharedConnectionEnvironmentVariable)
                ?? GetEnvironmentVariable(AppConnectionEnvironmentVariable);
        }

        private static string? GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
                ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
        }

        private static string BuildConnectionString(string? configuredConnectionString)
        {
            if (string.IsNullOrWhiteSpace(configuredConnectionString))
            {
                throw new InvalidOperationException(
                    $"Configura {LicensingConnectionEnvironmentVariable} o {SharedConnectionEnvironmentVariable} con la conexion del panel de licencias.");
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
                throw new InvalidOperationException("La cadena de conexion debe incluir usuario y contrasena.");
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
            builder.Timeout = 5;
            builder.CommandTimeout = 15;
            builder.KeepAlive = 30;
        }
    }
}

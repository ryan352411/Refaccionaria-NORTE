# RefaxManager

Aplicacion WPF de punto de venta para refaccionaria, basada en el diseno de la app original y separada de integraciones externas.

## Base de datos Neon

1. Crea un proyecto en Neon.
2. En el SQL Editor de Neon, ejecuta `database/001_initial_neon.sql`.
3. Configura la variable de entorno de Windows:

```powershell
[Environment]::SetEnvironmentVariable(
  "REFACCIONARIA_NUEVA_DB_CONNECTION",
  "Host=TU_HOST;Database=TU_DB;Username=TU_USUARIO;Password=TU_PASSWORD;SSL Mode=Require;Trust Server Certificate=true",
  "User"
)
```

Tambien puedes usar una cadena URI de Neon compatible con Npgsql, por ejemplo:

```powershell
[Environment]::SetEnvironmentVariable(
  "REFACCIONARIA_NUEVA_DB_CONNECTION",
  "postgresql://usuario:password@host.neon.tech/dbname?sslmode=require",
  "User"
)
```

Reinicia la aplicacion despues de configurar la variable.

La conexion no depende de si la computadora usa WiFi o cable Ethernet. Mientras tenga acceso a internet y Neon permita esa red, `RefaxManager` usa la misma configuracion sin pedir datos nuevos. Si cambias de red y Neon tiene una lista de IPs permitidas, agrega tambien la IP publica de esa red en Neon.

El instalador tambien configura `REFACCIONARIA_LICENSE_DB_CONNECTION` para que RefaxManager se registre en el panel de licencias con el codigo `REFACCIONARIA_NUEVA`.

## Acceso inicial

- Usuario: `admin`
- Contrasena: `admin123`

Cambia esa contrasena creando otro usuario SuperAdmin desde la app y eliminando el usuario inicial cuando ya no lo necesites.

## Desarrollo

```powershell
dotnet restore
dotnet build
```

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

## Corte de caja movil

La carpeta `CorteCajaMovil` contiene una PWA para consultar y registrar el corte desde un celular usando la misma base Neon configurada en `REFACCIONARIA_NUEVA_DB_CONNECTION`.

Para probarla en la computadora:

```powershell
dotnet run --project .\CorteCajaMovil\CorteCajaMovil.csproj
```

Para abrirla desde un celular en la misma red, levanta el servidor escuchando en todas las interfaces:

```powershell
dotnet run --project .\CorteCajaMovil\CorteCajaMovil.csproj --urls "http://0.0.0.0:5088"
```

Despues entra desde el celular a `http://IP-DE-LA-COMPUTADORA:5088`.

La app crea automaticamente la tabla `cortes_caja` si no existe. Tambien queda la migracion en `database/003_cortes_caja_movil.sql` por si prefieres ejecutarla manualmente en Neon.

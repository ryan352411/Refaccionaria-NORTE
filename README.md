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

## Modo offline del POS

RefaxManager puede seguir vendiendo aunque se caiga el internet:

- Mientras hay conexion, la app guarda en la computadora una copia del catalogo de productos y de los usuarios (`%LocalAppData%\RefaxManager\Offline`). Se refresca al abrir el panel y cada 10 minutos.
- Si la conexion falla, el punto de venta busca productos en la copia local, cobra normalmente e imprime un ticket provisional con la leyenda "VENTA SIN CONEXION" y folio "PENDIENTE".
- Las ventas offline quedan en una cola local y se suben solas a Neon (en orden, con su fecha original) cuando regresa el internet; el panel principal muestra cuantas ventas faltan por sincronizar.
- El inicio de sesion tambien funciona sin internet usando los usuarios guardados localmente. Se necesita haber entrado al menos una vez con conexion en esa computadora.

Limitaciones del modo offline: inventario (alta/edicion), historial, devoluciones, corte de caja, clientes frecuentes y articulos comunes requieren conexion. Los avisos de WhatsApp de las ventas offline se envian al momento de sincronizar.

## Catalogo web para clientes

La carpeta `CatalogoWeb` contiene una pagina web de solo lectura para que los clientes consulten piezas en existencia, precios de venta, categorias e imagenes de productos usando la misma base de datos del POS.

Para probarla en la computadora:

```powershell
dotnet run --project .\CatalogoWeb\CatalogoWeb.csproj
```

Para abrirla desde un celular en la misma red:

```powershell
dotnet run --project .\CatalogoWeb\CatalogoWeb.csproj --urls "http://0.0.0.0:5090"
```

Despues entra desde el celular a `http://IP-DE-LA-COMPUTADORA:5090`.

El catalogo usa la misma variable `REFACCIONARIA_NUEVA_DB_CONNECTION`. Al iniciar agrega automaticamente las columnas `imagen_url` y `tipo_venta` a `productos`, y prepara `producto_imagenes` para servir imagenes guardadas en PostgreSQL.

## Despliegue seguro del catalogo

El archivo `render.yaml` deja preparado el catalogo para Render como Web Service con Docker. No guardes credenciales en el repositorio.

En Render configura estas variables de entorno desde el panel del servicio:

```text
REFACCIONARIA_NUEVA_DB_CONNECTION=Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true
ASPNETCORE_ENVIRONMENT=Production
```

Recomendaciones:

- Usa un usuario de Neon con los permisos minimos necesarios para leer productos.
- No pegues la cadena de conexion en `appsettings.json`, `render.yaml`, `Dockerfile`, archivos `.env` ni commits.
- Las imagenes seleccionadas desde RefaxManager se guardan en `producto_imagenes.imagen_data`, por lo que el catalogo web puede mostrarlas desde la misma base. Tambien se siguen aceptando URLs HTTPS externas en `imagen_url`.
- Si cambias la contrasena de Neon, actualiza solo la variable de entorno en Render y redeploya el servicio.
- Render debe usar `/api/health` como health check.

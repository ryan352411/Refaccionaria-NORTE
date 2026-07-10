# Configuración de avisos por WhatsApp (Meta Cloud API)

RefaxManager envía un mensaje de WhatsApp a los números configurados cada vez que se
registra una venta. Usa la **API oficial de WhatsApp Cloud (Meta)**.

El código ya está listo. Solo falta el setup de Meta (una sola vez) y configurar
las variables de entorno.

---

## Resumen de lo que necesitas obtener

| Dato | Variable de entorno | Dónde se obtiene |
|------|---------------------|------------------|
| Token permanente | `REFAX_WHATSAPP_TOKEN` | Usuario del sistema (System User) en Meta Business |
| Phone Number ID | `REFAX_WHATSAPP_PHONE_ID` | Panel de WhatsApp > API Setup |
| Nombre de plantilla | `REFAX_WHATSAPP_TEMPLATE` (opcional, por defecto `nueva_venta`) | La que crees y se apruebe |
| Idioma de plantilla | `REFAX_WHATSAPP_LANG` (opcional, por defecto `es_MX`) | El idioma con que creaste la plantilla |

---

## Paso 1. Cuenta y app en Meta

1. Entra a https://developers.facebook.com/ con una cuenta de Facebook.
2. Menú **Mis aplicaciones** > **Crear app**.
3. Tipo de app: **Empresa / Business**.
4. Asóciala a un **portafolio comercial (Meta Business)**. Si no tienes uno, créalo
   en el proceso.

## Paso 2. Agregar el producto WhatsApp

1. Dentro de la app, en "Agregar productos", elige **WhatsApp** > **Configurar**.
2. Meta te da gratis un **número de prueba** y una **cuenta de WhatsApp Business (WABA)**.
3. En la pantalla **API Setup** verás:
   - **Phone number ID** -> este es tu `REFAX_WHATSAPP_PHONE_ID`.
   - Un **token temporal** (dura 24 h, solo para pruebas).
   - Una sección "To" para **agregar números de destino de prueba** (hasta 5).

> Con el número de prueba puedes mandar mensajes GRATIS a esos 5 números verificados.
> Para una refaccionaria que solo avisa al dueño/encargado, esto suele ser suficiente
> incluso de forma permanente. Si más adelante quieres tu propio número o más
> destinatarios, hay que verificar el negocio y registrar un número (Paso 6).

## Paso 3. Agregar tus números de destino de prueba

En **API Setup > To**, agrega los números que recibirán los avisos (el del dueño, etc.)
en formato internacional. Meta les enviará un código para verificarlos una vez.

Estos mismos números los registras también dentro del POS, en la pantalla
**"Avisos WhatsApp"**, en formato internacional **sin** el signo `+`
(México: `52` + 10 dígitos, ej. `5212381234567`).

## Paso 4. Crear la plantilla del mensaje

Los mensajes "iniciados por el negocio" (como este aviso) requieren una **plantilla
aprobada**. 

1. Ve a **WhatsApp Manager > Plantillas de mensajes** (o
   https://business.facebook.com/wa/manage/message-templates/).
2. **Crear plantilla**:
   - **Categoría:** Utility (Utilidad)
   - **Nombre:** `nueva_venta`
   - **Idioma:** Español (México) -> `es_MX`
   - **Cuerpo (Body)**, con 4 variables en este orden. Debe tener suficiente texto
     fijo y NINGUNA variable al inicio ni al final (regla de Meta):

     ```
     Hola, se registró una nueva venta en el sistema. El folio es {{1}} por un total de {{2}}, atendida por el vendedor {{3}} el día {{4}}. Ya puedes consultarla en el punto de venta.
     ```

   - En "ejemplos" para `{{1}}..{{4}}` pon algo como: `123`, `$450.00`, `Juan`, `27/06/2026 14:30`.
3. Envía a revisión. La aprobación de plantillas de utilidad suele tardar de minutos
   a unas horas.

> IMPORTANTE: el orden de las variables debe coincidir con el código. El POS envía:
> {{1}} = folio, {{2}} = total, {{3}} = vendedor, {{4}} = fecha. Si cambias el texto
> de la plantilla, conserva ese orden de variables.

## Paso 5. Token permanente (System User)

El token temporal de la pantalla API Setup expira en 24 h. Para producción crea un
token permanente:

1. Ve a **Meta Business Settings** (https://business.facebook.com/settings).
2. **Usuarios > Usuarios del sistema > Agregar**. Crea uno (rol Admin o Empleado).
3. **Asignar activos**: asígnale la **app** y la **cuenta de WhatsApp (WABA)** con
   permiso de control total / administrar.
4. Botón **Generar token nuevo**:
   - Elige la app.
   - Permisos: marca **`whatsapp_business_messaging`** y
     **`whatsapp_business_management`**.
   - Caducidad: **Nunca** (o lo más largo posible).
5. Copia el token (empieza con `EAA...`). Este es tu `REFAX_WHATSAPP_TOKEN`.
   Guárdalo bien: solo se muestra una vez.

## Paso 6 (opcional). Pasar a producción con tu propio número

Solo si quieres usar tu propio número de WhatsApp o enviar a muchos destinatarios:

1. Completa la **verificación del negocio** en Meta Business.
2. En WhatsApp > API Setup, **agrega un número de teléfono** y verifícalo.
   - Debe ser un número que NO esté activo en la app normal de WhatsApp / WhatsApp
     Business, o migrarlo.
3. Usa el `Phone Number ID` de ese número en `REFAX_WHATSAPP_PHONE_ID`.

---

## Configurar las variables de entorno

### Opcion recomendada: dejarlas embebidas en el instalador (MSI)

El instalador (`installer/RefaxManager.wxs`) escribe las variables de entorno
automaticamente al instalar, asi NO hay que configurar nada a mano en la PC del
cliente. Las cuatro variables (BD, licencias y WhatsApp) se pasan al compilar el MSI.

El token y el Phone ID son los mismos para todos los clientes (es tu unica cuenta de
Meta), por eso van embebidos. Los numeros que reciben los avisos NO van aqui: esos se
configuran por cliente desde la pantalla "Avisos WhatsApp" del POS.

Para generar el MSI ya configurado:

```powershell
# 1) Publicar la app (genera builds\RefaxManager\RefaxManager.exe)
dotnet publish .\RefaccionariaPOS\RefaccionariaPOS.csproj -c Release `
  -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -o .\builds\RefaxManager

# 2) Compilar el MSI pasando las 4 variables
wix build .\installer\RefaxManager.wxs `
  -d DbConnectionString="Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true" `
  -d LicenseDbConnectionString="Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true" `
  -d WhatsAppToken="EAAxxxxxxx" `
  -d WhatsAppPhoneId="123456789012345" `
  -o .\installers\RefaxManager-Setup-1.0.31.msi
```

Al instalar ese MSI en la PC del cliente, las cuatro variables quedan configuradas al
instante. Recuerda subir la version en `RefaccionariaPOS.csproj` y en
`installer/RefaxManager.wxs` en cada release para que la actualizacion reemplace a la
version anterior.

### Opcion manual (solo para pruebas en tu PC)

Si quieres probar sin reinstalar, configuralas a mano en PowerShell:

```powershell
[Environment]::SetEnvironmentVariable("REFAX_WHATSAPP_TOKEN", "EAAxxxxxxx", "User")
[Environment]::SetEnvironmentVariable("REFAX_WHATSAPP_PHONE_ID", "123456789012345", "User")
# Opcionales (ya tienen valor por defecto):
[Environment]::SetEnvironmentVariable("REFAX_WHATSAPP_TEMPLATE", "nueva_venta", "User")
[Environment]::SetEnvironmentVariable("REFAX_WHATSAPP_LANG", "es_MX", "User")
```

Cierra y vuelve a abrir RefaxManager (las variables se leen al iniciar el envío).

---

## Probar sin depender del POS

Antes de hacer una venta real, valida token + phone id + plantilla con este comando
de PowerShell (reemplaza TOKEN, PHONE_ID y el número destino):

```powershell
$token   = "EAAxxxxxxx"
$phoneId = "123456789012345"
$destino = "5212381234567"

$headers = @{ Authorization = "Bearer $token" }
$body = @{
  messaging_product = "whatsapp"
  to                = $destino
  type              = "template"
  template          = @{
    name       = "nueva_venta"
    language   = @{ code = "es_MX" }
    components = @(
      @{ type = "body"; parameters = @(
        @{ type = "text"; text = "123" },
        @{ type = "text"; text = "`$450.00" },
        @{ type = "text"; text = "Juan" },
        @{ type = "text"; text = "27/06/2026 14:30" }
      )}
    )
  }
} | ConvertTo-Json -Depth 8

Invoke-RestMethod -Method Post `
  -Uri "https://graph.facebook.com/v21.0/$phoneId/messages" `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body
```

Si el mensaje llega al WhatsApp del número destino, todo está correcto y el POS
enviará igual tras cada venta.

---

## Solución de problemas

- **No llega nada y el POS no marca error:** es el diseño (el aviso nunca detiene la
  venta). Revisa con el comando de prueba de arriba para ver el error real de Meta.
- **`(#132001) Template name does not exist` :** el nombre o idioma de la plantilla
  no coincide. Verifica `REFAX_WHATSAPP_TEMPLATE` y `REFAX_WHATSAPP_LANG`.
- **`(#131030) Recipient phone number not in allowed list`:** estás con número de
  prueba y el destino no está en los 5 verificados. Agrégalo en API Setup > To.
- **`(#190) ... access token` / 401:** el token expiró (usaste el temporal) o le
  faltan permisos. Genera el token permanente del Paso 5.
- **Llega al hacer la prueba pero no desde el POS:** confirma que configuraste las
  variables en el **mismo usuario de Windows** que abre el POS y que reiniciaste la app.
```

# Configuración de avisos por Telegram (Bot API)

RefaxManager envía un mensaje de Telegram a los chats configurados cada vez que se
registra una venta. **No requiere verificación de negocio ni tiene límite de
destinatarios**: solo se necesita crear un bot (gratis, 2 minutos). Si el token no
está configurado, el envío se salta silenciosamente y la venta no se ve afectada.

---

## Paso 1. Crear el bot (una sola vez)

1. En Telegram (celular o computadora), busca el contacto **@BotFather**.
2. Escríbele `/newbot`.
3. Te pedirá un nombre para mostrar (ej. `Avisos Refaccionaria`) y un nombre de
   usuario que termine en `bot` (ej. `AvisosRefaxBot`).
4. BotFather responde con el **token** del bot, algo como:
   `1234567890:AAF3xY9...`. Ese es tu `REFAX_TELEGRAM_TOKEN`. Guárdalo bien.

## Paso 2. Configurar el token en el POS

### Opción recomendada: embebido en el instalador (MSI)

El instalador escribe la variable de entorno automáticamente. Al compilar el MSI,
agrega `-d TelegramToken=...` junto a las demás variables:

```powershell
wix build .\installer\RefaxManager.wxs `
  -d DbConnectionString="..." `
  -d LicenseDbConnectionString="..." `
  -d TelegramToken="1234567890:AAF3xY9..." `
  -o .\installers\RefaxManager-Setup-X.Y.Z.msi
```

### Opción manual (para probar sin reinstalar)

```powershell
[Environment]::SetEnvironmentVariable("REFAX_TELEGRAM_TOKEN", "1234567890:AAF3xY9...", "User")
```

Cierra y vuelve a abrir RefaxManager después de configurarla.

## Paso 3. Dar de alta a cada persona que recibirá avisos

1. La persona instala Telegram en su celular (iPhone o Android).
2. Busca el bot por su nombre de usuario (ej. `@AvisosRefaxBot`) y le escribe
   cualquier mensaje (por ejemplo `hola`).
3. En el POS, abre **"Avisos Telegram"** y presiona **"Detectar chat nuevo"**:
   el Chat ID y el nombre se llenan solos. Presiona **Guardar**.

Eso es todo: desde ese momento le llegará una notificación por cada venta.

> Nota: la detección usa los mensajes recientes al bot (Telegram los conserva
> aproximadamente 24 horas). Si no aparece nada, pide a la persona que le vuelva
> a escribir al bot y presiona detectar de nuevo.

Puedes registrar todos los chats que quieras y activarlos/desactivarlos o
eliminarlos desde la misma pantalla.

---

## Solución de problemas

- El envío nunca detiene la venta; si algo falla queda registrado en
  `%LOCALAPPDATA%\RefaxManager\telegram.log`.
- **"Falta configurar la variable REFAX_TELEGRAM_TOKEN"** al detectar chats: la
  variable no está configurada en esa PC o no se reinició la app después de
  configurarla.
- **`403 Forbidden: bot was blocked by the user`** en el log: esa persona bloqueó
  al bot; pídele que lo desbloquee y le escriba de nuevo.
- **`400 Bad Request: chat not found`**: el Chat ID está mal escrito; usa el botón
  "Detectar chat nuevo" en lugar de teclearlo.

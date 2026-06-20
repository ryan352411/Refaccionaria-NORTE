# 🛠️ Guía de Instalación - Sistema de Auditoría

## Pasos para Implementar

### Paso 1: Aplicar Migración de Base de Datos

1. Abre **Neon Console** (https://console.neon.tech)
2. Ve a tu proyecto y abre **SQL Editor**
3. Copia todo el contenido de:
   ```
   database/008_auditoria_completa.sql
   ```
4. Pega en SQL Editor y ejecuta
5. Deberías ver mensajes de éxito para:
   - ✅ CREATE TABLE auditoria
   - ✅ CREATE INDEX (4 índices)
   - ✅ ALTER TABLE historial_inventario
   - ✅ ALTER TABLE historial_inventario (columnas nuevas)

### Paso 2: Compilar el Proyecto C#

```powershell
cd .\RefaccionariaPOS
dotnet clean
dotnet build
```

✅ **Verificar que compile sin errores**

### Paso 3: Ejecutar la Aplicación

```powershell
dotnet run --project .\RefaccionariaPOS\RefaccionariaPOS.csproj
```

---

## 🧪 Pruebas de Funcionamiento

### Test 1: Crear Producto
1. Abre **Inventario** → **Nuevo Producto**
2. Llena datos: Código, Nombre, Precio, Stock
3. Haz clic en **Guardar**
4. ✅ Debería aparecer en inventario

**Verificar auditoría:**
```sql
SELECT * FROM auditoria 
WHERE tabla = 'productos' ORDER BY fecha_operacion DESC LIMIT 1;
```

### Test 2: Actualizar Precio
1. Abre **Inventario**
2. Selecciona un producto existente → **Editar** (menú contextual)
3. Cambia el **Precio de Venta** (ej: 100 → 150)
4. Haz clic en **Actualizar Stock**
5. ✅ Debería actualizarse

**Verificar auditoría:**
```sql
SELECT * FROM auditoria 
WHERE tabla = 'productos' AND campo = 'Precio de Venta'
ORDER BY fecha_operacion DESC LIMIT 1;
```

### Test 3: Ajuste Manual de Stock
1. Abre **Inventario**
2. Selecciona un producto → **Actualizar Stock**
3. Cambia el valor (ej: 50 → 75)
4. ✅ Se ajusta inmediatamente

**Verificar auditoría:**
```sql
SELECT * FROM historial_inventario 
WHERE tipo = 'Ajuste Manual'
ORDER BY fecha_movimiento DESC LIMIT 1;
```

### Test 4: Venta
1. Abre **Ventas**
2. Escanea/busca un producto
3. Agrega cantidad
4. Haz clic en **Cobrar**
5. ✅ Se debe restar del stock

**Verificar auditoría:**
```sql
SELECT * FROM historial_inventario 
WHERE tipo = 'Venta'
ORDER BY fecha_movimiento DESC LIMIT 1;
```

---

## 📊 Ver Auditoría en la Aplicación

### Desde C#
Abre la vista de auditoría:
```csharp
// En MainView.xaml.cs, agregar botón que haga:
var auditoriaView = new AuditoriaView { Owner = this };
auditoriaView.ShowDialog();
```

### Desde Navegador SQL (Neon)
```sql
-- Ver todos los cambios
SELECT * FROM auditoria ORDER BY fecha_operacion DESC LIMIT 50;

-- Ver cambios de hoy
SELECT * FROM auditoria 
WHERE fecha_operacion >= CURRENT_DATE
ORDER BY fecha_operacion DESC;

-- Ver cambios de un usuario
SELECT * FROM auditoria 
WHERE usuario_id = 1
ORDER BY fecha_operacion DESC;

-- Ver auditoría de inventario
SELECT * FROM historial_inventario 
ORDER BY fecha_movimiento DESC LIMIT 50;
```

---

## ⚠️ Solución de Problemas

### Problema: "Table 'auditoria' does not exist"
**Causa:** La migración no se aplicó  
**Solución:** Verifica que ejecutaste `008_auditoria_completa.sql` en Neon SQL Editor

### Problema: "AuditService" no encuentra el namespace
**Causa:** Falta agregar `using RefaccionariaPOS.Services;` en los Views  
**Solución:** Verifica que tengas los `using` al inicio del archivo:
```csharp
using RefaccionariaPOS.Services;
```

### Problema: Los cambios se guardan pero no aparecen en auditoría
**Causa:** La auditoría está fallando silenciosamente (por diseño)  
**Solución:**
1. Verifica en Debug si hay errores (Output window)
2. Revisa que la transacción se complete exitosamente
3. Ejecuta directamente en SQL para verificar que las tablas existan

### Problema: "Usuario_id is NULL" en auditoría
**Causa:** `ObtenerUsuarioIdDelSistema()` retorna 0 (sin usuario)  
**Solución:** Implementar en `MainView` o `LoginView` para pasar el ID real:
```csharp
// En RegistrarProductoView, cambiar:
private int ObtenerUsuarioIdDelSistema()
{
    // Obtener del contexto de aplicación
    if (Application.Current.Resources["UsuarioId"] is int userId)
        return userId;
    return 0;
}

// En LoginView, después de login exitoso:
Application.Current.Resources["UsuarioId"] = usuarioIdDelLogin;
```

---

## 🔄 Actualizar la Aplicación

Si necesitas cambios futuros en auditoría:

### Agregar Auditoría a Otra Operación
1. Inyecta `AuditService` en el método
2. Envuelve en transacción
3. Registra antes/después de cambio

```csharp
using (NpgsqlTransaction transaccion = conexion.BeginTransaction())
{
    try
    {
        // Tu código
        var auditService = new AuditService(usuarioId);
        auditService.Registrar(conexion, transaccion, "tabla", 
            AuditService.TipoOperacion.UPDATE, registroId, "descripción");
        
        transaccion.Commit();
    }
    catch
    {
        transaccion.Rollback();
        throw;
    }
}
```

### Agregar Nuevos Campos a Auditoría
1. Agregar columnas en BD:
```sql
ALTER TABLE auditoria ADD COLUMN ip_address VARCHAR(45);
ALTER TABLE auditoria ADD COLUMN agente_usuario VARCHAR(255);
```

2. Actualizar `AuditService.cs`:
```csharp
public void Registrar(..., string? ipAddress = null, string? agente = null)
{
    cmd.Parameters.AddWithValue("@ipAddress", ipAddress ?? (object)DBNull.Value);
    cmd.Parameters.AddWithValue("@agente", agente ?? (object)DBNull.Value);
}
```

---

## 📋 Checklist de Implementación

- [ ] Aplicar migración `008_auditoria_completa.sql` en Neon
- [ ] Compilar proyecto `dotnet build`
- [ ] Ejecutar aplicación
- [ ] Test: Crear producto y verificar auditoría
- [ ] Test: Cambiar precio y verificar auditoría
- [ ] Test: Ajustar stock y verificar auditoría
- [ ] Test: Realizar venta y verificar auditoría
- [ ] (Opcional) Agregar botón "Ver Auditoría" en MainView
- [ ] (Opcional) Implementar `ObtenerUsuarioIdDelSistema()` con usuario real
- [ ] (Opcional) Documentar cambios en README.md

---

## 📞 Preguntas Frecuentes

### ¿Qué pasa si falla la auditoría?
La operación se completa, pero se registra el error en debug. Por diseño, no interrumpe operaciones.

### ¿Puedo ver quién hizo qué cambio?
Sí, la columna `usuario_id` en `auditoria` y `usuario_registrador_id` en `historial_inventario` rastrean esto.

### ¿Cuánto espacio toma la auditoría?
- Cada registro: ~200 bytes promedio
- 1 año de cambios (10/día): ~730MB
- Considera archivar registros antiguos anualmente

### ¿Puedo eliminar registros de auditoría?
Técnicamente sí, pero **NO se recomienda**. Si debes, archivar primero:
```sql
CREATE TABLE auditoria_archive AS SELECT * FROM auditoria WHERE fecha_operacion < '2024-01-01';
DELETE FROM auditoria WHERE fecha_operacion < '2024-01-01';
```

### ¿Cómo exporto la auditoría?
Desde `AuditoriaView.xaml`, haz clic en **Exportar CSV**  
O ejecuta SQL y copia/pega en Excel

### ¿Puedo auditar las búsquedas/consultas?
Actualmente no (solo cambios). Para auditar lecturas, necesitarías:
1. Triggers en PostgreSQL para cada SELECT
2. Tabla separada de "accesos"
3. Rendimiento impactado significativamente

---

**Implementación completada:** ✅  
**Siguiente paso:** Aplicar migración en Neon y compilar la app

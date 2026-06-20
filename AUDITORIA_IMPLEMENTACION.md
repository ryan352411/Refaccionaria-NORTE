# 📋 Implementación de Sistema de Auditoría Completo

## Resumen de Cambios

Se ha implementado un **sistema de auditoría integral** que registra todos los cambios en productos, stock y operaciones del sistema.

---

## 🗄️ Cambios en Base de Datos

### Nueva Migración: `008_auditoria_completa.sql`

#### Tabla `auditoria`
Registra **todos los cambios** en el sistema:
```sql
CREATE TABLE auditoria (
    id SERIAL PRIMARY KEY,
    usuario_id INTEGER NULL REFERENCES usuarios(id),
    tabla VARCHAR(50) NOT NULL,
    operacion VARCHAR(20) NOT NULL, -- INSERT, UPDATE, DELETE
    registro_id INTEGER NOT NULL,
    campo VARCHAR(100) NULL,        -- Campo específico modificado
    valor_anterior TEXT NULL,
    valor_nuevo TEXT NULL,
    descripcion TEXT NOT NULL,
    fecha_operacion TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

**Índices para rendimiento rápido:**
- `idx_auditoria_fecha` - Búsquedas por fechas
- `idx_auditoria_tabla` - Filtrar por tabla
- `idx_auditoria_usuario` - Historial por usuario
- `idx_auditoria_registro` - Cambios de un registro específico

#### Tabla `historial_inventario` (Mejorada)
Renombrada desde `movimientos_inventario` con nuevos campos:
```sql
ALTER TABLE movimientos_inventario RENAME TO historial_inventario;

ALTER TABLE historial_inventario ADD COLUMN usuario_registrador_id INTEGER;
ALTER TABLE historial_inventario ADD COLUMN stock_anterior numeric(12,3);
ALTER TABLE historial_inventario ADD COLUMN stock_nuevo numeric(12,3);
ALTER TABLE historial_inventario ADD COLUMN razon VARCHAR(255);
```

**Ahora registra:**
- ✅ Stock antes y después
- ✅ Razón del movimiento
- ✅ Usuario que registró el cambio
- ✅ Tipo de movimiento (Venta, Agregación, Ajuste Manual)

---

## 💻 Cambios en Código C#

### 1. Nuevo Servicio: `AuditService.cs`

**Responsabilidad:** Centraliza todo el registro de auditoría

```csharp
public class AuditService
{
    // Registra cambio general
    public void Registrar(
        NpgsqlConnection conexion,
        NpgsqlTransaction transaccion,
        string tabla,
        TipoOperacion operacion,
        int registroId,
        string descripcion,
        string? campo = null,
        string? valorAnterior = null,
        string? valorNuevo = null)

    // Registra movimiento de stock específico
    public void RegistrarStockHistorial(
        NpgsqlConnection conexion,
        NpgsqlTransaction transaccion,
        int productoId,
        string tipo,
        decimal cantidad,
        decimal stockAnterior,
        decimal stockNuevo,
        string razon = "")
}
```

**Constantes de campos:**
```csharp
public static class Cambios
{
    public const string PRECIO_VENTA = "Precio de Venta";
    public const string COSTO_PROVEEDOR = "Costo Proveedor";
    public const string NOMBRE = "Nombre";
    public const string DESCRIPCION = "Descripción";
    public const string CATEGORIA = "Categoría";
    public const string STOCK_MINIMO = "Stock Mínimo";
    public const string TIPO_VENTA = "Tipo de Venta";
}
```

---

### 2. Modificaciones: `RegistrarProductoView.xaml.cs`

#### Insertar Nuevo Producto
✅ Registra en auditoría al crear:
```csharp
auditService.Registrar(conexion, transaccion, "productos", 
    AuditService.TipoOperacion.INSERT,
    productoId, 
    $"Nuevo producto: {txtNombre.Text} (Stock: {stock})");
```

#### Actualizar Producto Existente
✅ Registra **cada cambio individual:**
- Cambios en precio de venta
- Cambios en costo proveedor
- Cambios en stock mínimo
- Agregación de stock

```csharp
if (precioVenta != precioPrevio)
{
    auditService.Registrar(conexion, transaccion, "productos", 
        AuditService.TipoOperacion.UPDATE,
        idProducto, 
        $"Actualización de {AuditService.Cambios.PRECIO_VENTA}",
        AuditService.Cambios.PRECIO_VENTA, 
        precioPrevio.ToString(), 
        precioVenta.ToString());
}

// Registra agregación de stock
auditService.RegistrarStockHistorial(conexion, transaccion, 
    idProducto, "Agregación",
    stockAgregar, stockActualPrevio, stockNuevo, 
    "Compra/Reposición de stock");
```

---

### 3. Modificaciones: `InventarioView.xaml.cs`

#### Actualización Manual de Stock
✅ Registra **auditoría completa** cuando el usuario ajusta stock:

```csharp
private void ActualizarStockEnBaseDeDatos(string codigo, decimal nuevoStock)
{
    // 1. Obtener stock anterior
    // 2. Actualizar stock
    // 3. Registrar en historial_inventario (con antes/después)
    // 4. Registrar en auditoria general
    
    auditService.RegistrarStockHistorial(conexion, transaccion, 
        productoId, "Ajuste Manual",
        Math.Abs(diferencia), stockAnterior, nuevoStock, 
        "Actualización manual del inventario");

    auditService.Registrar(conexion, transaccion, "productos", 
        AuditService.TipoOperacion.UPDATE,
        productoId, 
        $"Ajuste manual de stock: {stockAnterior} → {nuevoStock}",
        "stock_actual", stockAnterior.ToString(), nuevoStock.ToString());
}
```

---

### 4. Modificaciones: `VentaView.xaml.cs`

#### Descuento de Stock en Venta
✅ Registra movimiento de inventario cuando se realiza una venta:

```csharp
// 1. Obtener stock ANTES de la venta
decimal stockAntes = 0; // (query)

// 2. Descontar del inventory
UPDATE productos SET stock_actual = stock_actual - @cantidad

// 3. Registrar auditoría
decimal stockDespues = stockAntes - item.Cantidad;
auditService.RegistrarStockHistorial(conexion, transaccion, 
    productoId.Value, "Venta",
    item.Cantidad, stockAntes, stockDespues, 
    $"Venta Folio #{folioGeneradoBaseDatos}");
```

---

### 5. Nueva Vista: `AuditoriaView.xaml(.cs)`

**Permite al usuario consultar el historial de auditoría:**

#### Características:
- ✅ Filtrar por tabla (productos, ventas, usuarios, historial_inventario)
- ✅ Filtrar por operación (INSERT, UPDATE, DELETE, Venta, etc.)
- ✅ Filtrar por período (Hoy, 7 días, 30 días, 90 días, Todo)
- ✅ Ver valores antes/después de cambios
- ✅ Exportar a CSV para análisis

#### Colores de fila:
- 🔴 **Rojo:** DELETE
- 🟢 **Verde:** INSERT
- 🟡 **Amarillo:** UPDATE

---

## 📊 Ejemplos de Auditoría Registrada

### Ejemplo 1: Crear Nuevo Producto
```
tabla: productos
operacion: INSERT
registro_id: 123
descripcion: "Nuevo producto: Aceite 10W40 (Stock: 50)"
fecha: 2025-06-19 14:30:00
usuario: admin
```

### Ejemplo 2: Cambiar Precio
```
tabla: productos
operacion: UPDATE
registro_id: 123
campo: "Precio de Venta"
valor_anterior: "250.00"
valor_nuevo: "280.00"
fecha: 2025-06-19 15:45:00
usuario: vendedor1
```

### Ejemplo 3: Venta de Producto
```
tabla: historial_inventario
tipo: "Venta"
producto_id: 123
cantidad: 2
stock_anterior: 50
stock_nuevo: 48
razon: "Venta Folio #1005"
fecha: 2025-06-19 16:20:00
usuario: vendedor1
```

### Ejemplo 4: Ajuste Manual de Stock
```
tabla: historial_inventario
tipo: "Ajuste Manual"
producto_id: 123
cantidad: 5
stock_anterior: 48
stock_nuevo: 53
razon: "Actualización manual del inventario"
fecha: 2025-06-19 17:00:00
usuario: admin
```

---

## 🔒 Seguridad y Confiabilidad

### Transacciones ACID
- ✅ **Atomicidad:** Cambio + auditoría se registran juntos o nada
- ✅ **Consistencia:** Los datos siempre están en estado válido
- ✅ **Aislamiento:** Cambios concurrentes no interfieren
- ✅ **Durabilidad:** PostgreSQL garantiza persistencia

```csharp
using (NpgsqlTransaction transaccion = conexion.BeginTransaction())
{
    try
    {
        // Realizar cambio
        // Registrar auditoría
        transaccion.Commit();
    }
    catch
    {
        transaccion.Rollback();
        throw;
    }
}
```

### Manejo de Excepciones
- Si falla la auditoría, NO se detiene la operación principal
- Se registra en debug para diagnóstico
- Los cambios se persisten aunque la auditoría falle

---

## 🚀 Cómo Usar

### 1. Aplicar Migración de BD
```powershell
# Ejecutar en Neon SQL Editor
-- Copiar contenido de database/008_auditoria_completa.sql
```

### 2. Ver Historial de Auditoría
```csharp
// Desde MainView, agregar botón:
var auditoriaView = new AuditoriaView { Owner = this };
auditoriaView.ShowDialog();
```

### 3. Consultas SQL Útiles
```sql
-- Ver últimos cambios de un producto
SELECT * FROM auditoria 
WHERE tabla = 'productos' AND registro_id = 123
ORDER BY fecha_operacion DESC;

-- Ver historial de stock de un producto
SELECT * FROM historial_inventario 
WHERE producto_id = 123
ORDER BY fecha_movimiento DESC;

-- Ver cambios por usuario
SELECT * FROM auditoria 
WHERE usuario_id = 1
ORDER BY fecha_operacion DESC;

-- Ver modificaciones de precios
SELECT * FROM auditoria 
WHERE tabla = 'productos' AND campo = 'Precio de Venta'
ORDER BY fecha_operacion DESC;
```

---

## 📈 Beneficios

✅ **Trazabilidad completa:** Saber quién cambió qué y cuándo  
✅ **Detección de fraude:** Identificar cambios sospechosos  
✅ **Cumplimiento:** Auditoría para requisitos regulatorios  
✅ **Recuperación:** Entender qué pasó antes de un problema  
✅ **Reportes:** Exportar datos para análisis  

---

## 🔧 Mantenimiento Futuro

### Limpieza de Auditoría Vieja
```sql
-- Eliminar registros con más de 1 año
DELETE FROM auditoria 
WHERE fecha_operacion < CURRENT_TIMESTAMP - INTERVAL '1 year';

-- Archivar en tabla histórica si necesitas conservar
INSERT INTO auditoria_historica 
SELECT * FROM auditoria 
WHERE fecha_operacion < CURRENT_TIMESTAMP - INTERVAL '1 year';
```

### Optimización
Si la tabla `auditoria` crece mucho:
1. Crear particiones por fecha
2. Archivar registros antiguos regularmente
3. Considerar tabla de auditoría de "lectura" solo para reportes

---

## 📝 Próximos Pasos (Opcional)

1. **Roles y permisos:** Quién puede ver la auditoría
2. **Alertas:** Notificar si ciertos cambios ocurren
3. **Reportes automáticos:** Enviar resumen diario
4. **Integración SIEM:** Enviar eventos a sistema de seguridad
5. **Firma digital:** Firmar registros de auditoría

---

**Implementación completada:** 19 de junio de 2025

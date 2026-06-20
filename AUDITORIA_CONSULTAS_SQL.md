# 🔍 Consultas SQL Útiles - Análisis de Auditoría

## 📊 Dashboard de Auditoría

### Resumen de Actividad Hoy
```sql
SELECT 
    a.operacion,
    COALESCE(u.username, 'Sistema') AS usuario,
    COUNT(*) AS cantidad
FROM auditoria a
LEFT JOIN usuarios u ON u.id = a.usuario_id
WHERE DATE(a.fecha_operacion) = CURRENT_DATE
GROUP BY a.operacion, u.username
ORDER BY cantidad DESC;
```

### Top 10 Usuarios más Activos
```sql
SELECT 
    COALESCE(u.username, 'Sistema') AS usuario,
    COUNT(*) AS cambios_totales,
    MIN(a.fecha_operacion) AS primer_cambio,
    MAX(a.fecha_operacion) AS ultimo_cambio
FROM auditoria a
LEFT JOIN usuarios u ON u.id = a.usuario_id
GROUP BY a.usuario_id, u.username
ORDER BY cambios_totales DESC
LIMIT 10;
```

### Operaciones por Tabla
```sql
SELECT 
    tabla,
    operacion,
    COUNT(*) AS cantidad,
    MAX(fecha_operacion) AS ultima_operacion
FROM auditoria
GROUP BY tabla, operacion
ORDER BY tabla, cantidad DESC;
```

---

## 💰 Análisis de Cambios de Precios

### Productos con Cambios de Precio
```sql
SELECT 
    COUNT(DISTINCT a.registro_id) AS productos_modificados,
    MIN(a.fecha_operacion)::DATE AS fecha_inicio,
    MAX(a.fecha_operacion)::DATE AS fecha_fin
FROM auditoria a
WHERE a.tabla = 'productos' 
  AND a.campo = 'Precio de Venta'
  AND a.fecha_operacion >= CURRENT_DATE - INTERVAL '30 days';
```

### Aumentos de Precio en Último Mes
```sql
SELECT 
    a.registro_id AS producto_id,
    CAST(a.valor_anterior AS DECIMAL) AS precio_anterior,
    CAST(a.valor_nuevo AS DECIMAL) AS precio_nuevo,
    ROUND((CAST(a.valor_nuevo AS DECIMAL) - CAST(a.valor_anterior AS DECIMAL)) / CAST(a.valor_anterior AS DECIMAL) * 100, 2) AS incremento_pct,
    a.fecha_operacion,
    COALESCE(u.username, 'Sistema') AS modificado_por
FROM auditoria a
LEFT JOIN usuarios u ON u.id = a.usuario_id
WHERE a.tabla = 'productos' 
  AND a.campo = 'Precio de Venta'
  AND CAST(a.valor_nuevo AS DECIMAL) > CAST(a.valor_anterior AS DECIMAL)
  AND a.fecha_operacion >= CURRENT_DATE - INTERVAL '30 days'
ORDER BY a.fecha_operacion DESC;
```

### Descuentos de Precio
```sql
SELECT 
    a.registro_id AS producto_id,
    CAST(a.valor_anterior AS DECIMAL) AS precio_anterior,
    CAST(a.valor_nuevo AS DECIMAL) AS precio_nuevo,
    ROUND((CAST(a.valor_anterior AS DECIMAL) - CAST(a.valor_nuevo AS DECIMAL)) / CAST(a.valor_anterior AS DECIMAL) * 100, 2) AS descuento_pct,
    a.fecha_operacion,
    COALESCE(u.username, 'Sistema') AS modificado_por
FROM auditoria a
LEFT JOIN usuarios u ON u.id = a.usuario_id
WHERE a.tabla = 'productos' 
  AND a.campo = 'Precio de Venta'
  AND CAST(a.valor_nuevo AS DECIMAL) < CAST(a.valor_anterior AS DECIMAL)
ORDER BY descuento_pct DESC;
```

---

## 📦 Análisis de Movimientos de Stock

### Resumen de Entradas y Salidas (Último Mes)
```sql
SELECT 
    p.nombre,
    SUM(CASE WHEN h.tipo = 'Venta' THEN h.cantidad ELSE 0 END) AS cantidad_vendida,
    SUM(CASE WHEN h.tipo = 'Agregación' THEN h.cantidad ELSE 0 END) AS cantidad_agregada,
    SUM(CASE WHEN h.tipo = 'Ajuste Manual' AND h.cantidad > 0 THEN h.cantidad ELSE 0 END) AS ajustes_positivos,
    SUM(CASE WHEN h.tipo = 'Ajuste Manual' AND h.cantidad < 0 THEN h.cantidad ELSE 0 END) AS ajustes_negativos,
    p.stock_actual
FROM historial_inventario h
JOIN productos p ON p.id = h.producto_id
WHERE h.fecha_movimiento >= CURRENT_DATE - INTERVAL '30 days'
GROUP BY p.id, p.nombre, p.stock_actual
ORDER BY cantidad_vendida DESC;
```

### Productos con Más Movimiento
```sql
SELECT 
    p.nombre,
    COUNT(*) AS numero_movimientos,
    SUM(h.cantidad) AS cantidad_total,
    h.tipo AS tipo_movimiento_principal,
    MIN(h.fecha_movimiento) AS primer_movimiento,
    MAX(h.fecha_movimiento) AS ultimo_movimiento
FROM historial_inventario h
JOIN productos p ON p.id = h.producto_id
WHERE h.fecha_movimiento >= CURRENT_DATE - INTERVAL '30 days'
GROUP BY p.id, p.nombre, h.tipo
ORDER BY numero_movimientos DESC
LIMIT 20;
```

### Productos con Stock Discrepante
```sql
SELECT 
    p.id,
    p.nombre,
    p.stock_actual,
    (SELECT stock_nuevo FROM historial_inventario 
     WHERE producto_id = p.id 
     ORDER BY fecha_movimiento DESC 
     LIMIT 1) AS stock_segun_historial,
    p.stock_actual - (SELECT stock_nuevo FROM historial_inventario 
                      WHERE producto_id = p.id 
                      ORDER BY fecha_movimiento DESC 
                      LIMIT 1) AS diferencia
FROM productos p
WHERE p.stock_actual != (SELECT stock_nuevo FROM historial_inventario 
                         WHERE producto_id = p.id 
                         ORDER BY fecha_movimiento DESC 
                         LIMIT 1);
```

---

## 🧑‍💼 Análisis por Usuario

### Actividad de Usuario Específico
```sql
SELECT 
    a.fecha_operacion,
    a.tabla,
    a.operacion,
    a.registro_id,
    a.campo,
    a.valor_anterior,
    a.valor_nuevo,
    a.descripcion
FROM auditoria a
WHERE a.usuario_id = 1  -- Cambiar a ID del usuario
ORDER BY a.fecha_operacion DESC
LIMIT 100;
```

### Cambios de Stock por Usuario
```sql
SELECT 
    COALESCE(u.username, 'Sistema') AS usuario,
    COUNT(*) AS cambios_stock,
    SUM(h.cantidad) AS cantidad_total_movida,
    MIN(h.fecha_movimiento) AS primer_cambio,
    MAX(h.fecha_movimiento) AS ultimo_cambio
FROM historial_inventario h
LEFT JOIN usuarios u ON u.id = h.usuario_registrador_id
GROUP BY h.usuario_registrador_id, u.username
ORDER BY cambios_stock DESC;
```

### Auditoría de Admin (SuperAdmin)
```sql
SELECT 
    a.fecha_operacion,
    a.tabla,
    a.operacion,
    a.campo,
    a.descripcion
FROM auditoria a
JOIN usuarios u ON u.id = a.usuario_id
WHERE u.rol = 'SuperAdmin'
ORDER BY a.fecha_operacion DESC;
```

---

## 🚨 Detección de Anomalías

### Cambios de Precios Sospechosos (>50% en un día)
```sql
SELECT 
    a.fecha_operacion,
    a.registro_id,
    CAST(a.valor_anterior AS DECIMAL) AS precio_antes,
    CAST(a.valor_nuevo AS DECIMAL) AS precio_despues,
    ABS(ROUND((CAST(a.valor_nuevo AS DECIMAL) - CAST(a.valor_anterior AS DECIMAL)) / CAST(a.valor_anterior AS DECIMAL) * 100, 2)) AS cambio_pct,
    COALESCE(u.username, 'Sistema') AS quien_cambio
FROM auditoria a
LEFT JOIN usuarios u ON u.id = a.usuario_id
WHERE a.tabla = 'productos' 
  AND a.campo = 'Precio de Venta'
  AND ABS(CAST(a.valor_nuevo AS DECIMAL) - CAST(a.valor_anterior AS DECIMAL)) / CAST(a.valor_anterior AS DECIMAL) > 0.5
ORDER BY a.fecha_operacion DESC;
```

### Stock Negativo o Ajustes Grandes
```sql
SELECT 
    p.nombre,
    h.stock_anterior,
    h.stock_nuevo,
    h.cantidad,
    h.tipo,
    h.razon,
    h.fecha_movimiento,
    COALESCE(u.username, 'Sistema') AS usuario
FROM historial_inventario h
JOIN productos p ON p.id = h.producto_id
LEFT JOIN usuarios u ON u.id = h.usuario_registrador_id
WHERE h.stock_nuevo < 0 
   OR (h.tipo = 'Ajuste Manual' AND ABS(h.cantidad) > p.stock_actual * 0.5)
ORDER BY h.fecha_movimiento DESC;
```

### Cambios Fuera de Horario (noche/madrugada)
```sql
SELECT 
    a.fecha_operacion,
    EXTRACT(HOUR FROM a.fecha_operacion) AS hora,
    a.tabla,
    a.operacion,
    a.descripcion,
    COALESCE(u.username, 'Sistema') AS usuario
FROM auditoria a
LEFT JOIN usuarios u ON u.id = a.usuario_id
WHERE EXTRACT(HOUR FROM a.fecha_operacion) NOT BETWEEN 8 AND 20
ORDER BY a.fecha_operacion DESC;
```

---

## 📈 Reportes Gerenciales

### Resumen Semanal
```sql
SELECT 
    DATE_TRUNC('week', a.fecha_operacion)::DATE AS semana,
    COUNT(*) AS total_cambios,
    COUNT(DISTINCT a.usuario_id) AS usuarios_activos,
    COUNT(DISTINCT CASE WHEN a.tabla = 'productos' THEN a.tabla END) AS cambios_productos,
    COUNT(DISTINCT CASE WHEN a.operacion = 'INSERT' THEN 1 END) AS nuevos_insertos
FROM auditoria a
GROUP BY DATE_TRUNC('week', a.fecha_operacion)
ORDER BY semana DESC;
```

### Productos Nuevos Agregados Este Mes
```sql
SELECT 
    DATE(a.fecha_operacion) AS fecha,
    COUNT(*) AS productos_nuevos,
    COALESCE(u.username, 'Sistema') AS agregado_por
FROM auditoria a
LEFT JOIN usuarios u ON u.id = a.usuario_id
WHERE a.tabla = 'productos' 
  AND a.operacion = 'INSERT'
  AND a.fecha_operacion >= DATE_TRUNC('month', CURRENT_DATE)
GROUP BY DATE(a.fecha_operacion), u.username
ORDER BY fecha DESC;
```

### Movimiento de Inventario por Día
```sql
SELECT 
    DATE(h.fecha_movimiento) AS fecha,
    h.tipo,
    COUNT(*) AS numero_movimientos,
    ROUND(SUM(h.cantidad)::NUMERIC, 2) AS cantidad_total
FROM historial_inventario h
WHERE h.fecha_movimiento >= CURRENT_DATE - INTERVAL '30 days'
GROUP BY DATE(h.fecha_movimiento), h.tipo
ORDER BY fecha DESC, h.tipo;
```

---

## 🔧 Mantenimiento de Auditoría

### Listar Tablas de Auditoría
```sql
SELECT table_name FROM information_schema.tables 
WHERE table_name IN ('auditoria', 'historial_inventario');
```

### Tamaño de Tablas de Auditoría
```sql
SELECT 
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS tamaño
FROM pg_tables
WHERE tablename IN ('auditoria', 'historial_inventario');
```

### Limpiar Auditoría Antigua (CUIDADO)
```sql
-- Solo ejecutar si tienes backup
-- Mantener auditoría de los últimos 90 días
DELETE FROM auditoria 
WHERE fecha_operacion < CURRENT_DATE - INTERVAL '90 days';

DELETE FROM historial_inventario 
WHERE fecha_movimiento < CURRENT_DATE - INTERVAL '90 days';

-- Ejecutar VACUUM para recuperar espacio
VACUUM ANALYZE auditoria;
VACUUM ANALYZE historial_inventario;
```

---

## 💡 Tips

1. **Guardar consultas frecuentes:** Crea vistas en PostgreSQL para reutilizar
2. **Exportar regularmente:** Guarda reportes mensuales en CSV
3. **Alertas:** Configura crons que ejecuten consultas de anomalías
4. **Performance:** Usa índices si las consultas son lentas
5. **Privacidad:** Limpia auditoría según reglamentaciones locales

---

**Última actualización:** 19 de junio de 2025

CREATE INDEX IF NOT EXISTS idx_movimientos_inventario_producto_id
    ON movimientos_inventario (producto_id);

CREATE INDEX IF NOT EXISTS idx_movimientos_inventario_usuario_id
    ON movimientos_inventario (usuario_id);

CREATE INDEX IF NOT EXISTS idx_movimientos_inventario_fecha
    ON movimientos_inventario (fecha_movimiento DESC);

CREATE INDEX IF NOT EXISTS idx_ventas_usuario_id
    ON ventas (usuario_id);

CREATE INDEX IF NOT EXISTS idx_ventas_estado
    ON ventas (estado);

CREATE INDEX IF NOT EXISTS idx_ventas_metodo_pago
    ON ventas (metodo_pago);

CREATE INDEX IF NOT EXISTS idx_detalles_venta_producto_id
    ON detalles_venta (producto_id);

CREATE INDEX IF NOT EXISTS idx_devoluciones_usuario_id
    ON devoluciones (usuario_id);

CREATE INDEX IF NOT EXISTS idx_devoluciones_fecha_devolucion
    ON devoluciones (fecha_devolucion DESC);

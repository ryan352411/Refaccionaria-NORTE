ALTER TABLE productos
    ALTER COLUMN stock_actual TYPE numeric(12, 3) USING stock_actual::numeric,
    ALTER COLUMN stock_minimo TYPE numeric(12, 3) USING stock_minimo::numeric;

ALTER TABLE detalles_venta
    ALTER COLUMN cantidad TYPE numeric(12, 3) USING cantidad::numeric;

ALTER TABLE movimientos_inventario
    ALTER COLUMN cantidad TYPE numeric(12, 3) USING cantidad::numeric;

ALTER TABLE detalles_devolucion
    ALTER COLUMN cantidad TYPE numeric(12, 3) USING cantidad::numeric;

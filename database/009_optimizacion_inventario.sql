-- Optimiza las búsquedas del inventario de RefaxManager.
-- Debe aplicarse una vez en la base de datos de producción.

CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX IF NOT EXISTS idx_productos_nombre_trgm
    ON productos USING GIN (nombre gin_trgm_ops);

ANALYZE productos;

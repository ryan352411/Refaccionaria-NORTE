using Npgsql;
using System;

namespace RefaccionariaPOS.Services
{
    public class AuditService
    {
        public enum TipoOperacion
        {
            INSERT,
            UPDATE,
            DELETE
        }

        private readonly int usuarioId;

        public AuditService(int usuarioId)
        {
            this.usuarioId = usuarioId;
        }

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
        {
            try
            {
                const string query = @"
                    INSERT INTO auditoria
                    (usuario_id, tabla, operacion, registro_id, campo, valor_anterior, valor_nuevo, descripcion)
                    VALUES
                    (@usuarioId, @tabla, @operacion, @registroId, @campo, @valorAnterior, @valorNuevo, @descripcion);";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion, transaccion))
                {
                    cmd.Parameters.AddWithValue("@usuarioId", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                    cmd.Parameters.AddWithValue("@tabla", tabla);
                    cmd.Parameters.AddWithValue("@operacion", operacion.ToString());
                    cmd.Parameters.AddWithValue("@registroId", registroId);
                    cmd.Parameters.AddWithValue("@campo", campo ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@valorAnterior", valorAnterior ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@valorNuevo", valorNuevo ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@descripcion", descripcion);

                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registrando auditoría: {ex.Message}");
                // No lanzar excepción para no interrumpir la operación principal
            }
        }

        public void RegistrarStockHistorial(
            NpgsqlConnection conexion,
            NpgsqlTransaction transaccion,
            int productoId,
            string tipo,
            decimal cantidad,
            decimal stockAnterior,
            decimal stockNuevo,
            string razon = "")
        {
            try
            {
                const string query = @"
                    INSERT INTO historial_inventario
                    (producto_id, usuario_id, usuario_registrador_id, tipo, cantidad, stock_anterior, stock_nuevo, fecha_movimiento, razon)
                    VALUES
                    (@productoId, @usuarioId, @usuarioRegistrador, @tipo, @cantidad, @stockAnterior, @stockNuevo, @fecha, @razon);";

                using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion, transaccion))
                {
                    cmd.Parameters.AddWithValue("@productoId", productoId);
                    cmd.Parameters.AddWithValue("@usuarioId", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                    cmd.Parameters.AddWithValue("@usuarioRegistrador", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                    cmd.Parameters.AddWithValue("@tipo", tipo);
                    cmd.Parameters.AddWithValue("@cantidad", cantidad);
                    cmd.Parameters.AddWithValue("@stockAnterior", stockAnterior);
                    cmd.Parameters.AddWithValue("@stockNuevo", stockNuevo);
                    cmd.Parameters.AddWithValue("@fecha", DateTime.Now);
                    cmd.Parameters.AddWithValue("@razon", razon ?? string.Empty);

                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registrando historial inventario: {ex.Message}");
            }
        }

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
    }
}

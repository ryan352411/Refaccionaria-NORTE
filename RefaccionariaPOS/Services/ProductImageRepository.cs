using Npgsql;

namespace RefaccionariaPOS.Services
{
    public static class ProductImageRepository
    {
        public static void GuardarImagen(NpgsqlConnection conexion, int productoId, string imagenUrl)
        {
            if (string.IsNullOrWhiteSpace(imagenUrl))
            {
                using NpgsqlCommand delete = new NpgsqlCommand(
                    "DELETE FROM producto_imagenes WHERE producto_id = @productoId;",
                    conexion);
                delete.Parameters.AddWithValue("@productoId", productoId);
                delete.ExecuteNonQuery();
                return;
            }

            const string query = @"
                INSERT INTO producto_imagenes (producto_id, imagen_url)
                VALUES (@productoId, @imagenUrl)
                ON CONFLICT (producto_id) DO UPDATE
                SET imagen_url = EXCLUDED.imagen_url,
                    fecha_actualizacion = CURRENT_TIMESTAMP;";

            using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            cmd.Parameters.AddWithValue("@productoId", productoId);
            cmd.Parameters.AddWithValue("@imagenUrl", imagenUrl.Trim());
            cmd.ExecuteNonQuery();
        }
    }
}

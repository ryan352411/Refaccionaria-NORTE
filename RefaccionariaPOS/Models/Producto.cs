namespace RefaccionariaPOS.Models
{
    public class Producto
    {
        public int Id { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Categoria { get; set; } = "General";
        public decimal PrecioCompra { get; set; } // Mapeado a costo_proveedor
        public decimal PrecioVenta { get; set; }
        public int Stock { get; set; } // Se usa como cantidad en el carrito
        public int StockMinimo { get; set; } = 5;
        public string EstadoStock => Stock <= StockMinimo ? "Bajo stock" : "Disponible";

        // NUEVO: Propiedad automática para la vista del vendedor
        public decimal Subtotal => PrecioVenta * Stock;
    }
}

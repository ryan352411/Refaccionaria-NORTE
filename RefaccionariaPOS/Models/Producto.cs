namespace RefaccionariaPOS.Models
{
    public class Producto
    {
        public int Id { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Categoria { get; set; } = "General";
        public string ImagenUrl { get; set; } = string.Empty;
        public byte[]? ImagenData { get; set; }
        public bool TieneImagenes { get; set; }
        public string TipoVenta { get; set; } = "Unidad";
        public decimal PrecioCompra { get; set; } // Mapeado a costo_proveedor
        public decimal PrecioVenta { get; set; }
        public decimal Stock { get; set; } // Se usa como cantidad disponible o cantidad en vistas.
        public decimal StockMinimo { get; set; } = 5;
        public string EstadoStock
        {
            get
            {
                if (Stock == 0)
                {
                    return "Sin stock";
                }

                return Stock <= StockMinimo ? "Bajo stock" : "Disponible";
            }
        }

        // NUEVO: Propiedad automática para la vista del vendedor
        public decimal Subtotal => PrecioVenta * Stock;
    }
}

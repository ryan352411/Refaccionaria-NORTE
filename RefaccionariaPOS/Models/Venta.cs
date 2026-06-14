using System;

namespace RefaccionariaPOS.Models
{
    public class Venta
    {
        public int Id { get; set; }
        public int Folio { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string MetodoPago { get; set; } = string.Empty;
        public string Vendedor { get; set; } = string.Empty;
        public string Origen
        {
            get
            {
                return "Mostrador";
            }
        }
    }
}

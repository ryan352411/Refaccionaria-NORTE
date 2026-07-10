using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace RefaccionariaPOS.Views
{
    public partial class CalculadoraGranelWindow : Window
    {
        private readonly decimal precioPorUnidad;
        private readonly decimal stockDisponible;
        private readonly string unidad;

        public CalculadoraGranelWindow(string nombreProducto, string unidad, decimal precioPorUnidad, decimal stockDisponible)
        {
            InitializeComponent();
            this.unidad = unidad;
            this.precioPorUnidad = precioPorUnidad;
            this.stockDisponible = stockDisponible;

            string unidadMinuscula = unidad.ToLowerInvariant();
            lblProducto.Text = nombreProducto;
            lblPrecioCaption.Text = $"Precio por {unidadMinuscula}";
            lblPrecio.Text = precioPorUnidad.ToString("0.00", CultureInfo.InvariantCulture);
            lblStockCaption.Text = $"{unidad}s disponibles";
            lblStock.Text = $"{FormatearCantidad(stockDisponible)} {unidadMinuscula}s";
            lblCantidadCaption.Text = $"Cantidad a vender ({unidadMinuscula}s)";
            Loaded += (_, _) =>
            {
                ActualizarTotal();
                txtCantidad.Focus();
                txtCantidad.SelectAll();
            };
        }

        public decimal CantidadSeleccionada { get; private set; }

        private void TxtCantidad_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ActualizarTotal();
        }

        private void ActualizarTotal()
        {
            lblTotal.Text = TryLeerCantidad(out decimal cantidad) && cantidad > 0
                ? (precioPorUnidad * cantidad).ToString("0.00", CultureInfo.InvariantCulture)
                : "0.00";
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            if (!TryLeerCantidad(out decimal cantidad) || cantidad <= 0)
            {
                MessageBox.Show("Ingresa una cantidad válida.", "Venta a granel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cantidad > stockDisponible)
            {
                MessageBox.Show($"Solo hay {FormatearCantidad(stockDisponible)} {unidad.ToLowerInvariant()}s disponibles.", "Sin existencias", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CantidadSeleccionada = cantidad;
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool TryLeerCantidad(out decimal cantidad)
        {
            string valor = txtCantidad.Text.Trim();
            return decimal.TryParse(valor, NumberStyles.Number, CultureInfo.CurrentCulture, out cantidad)
                || decimal.TryParse(valor.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out cantidad);
        }

        private static string FormatearCantidad(decimal cantidad)
        {
            return cantidad % 1 == 0 ? cantidad.ToString("0") : cantidad.ToString("0.###");
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnAgregar_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}

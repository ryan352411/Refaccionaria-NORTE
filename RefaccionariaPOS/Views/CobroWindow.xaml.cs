using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class CobroWindow : Window
    {
        private readonly decimal total;

        public int? ClienteId { get; private set; }
        public string MetodoPago { get; private set; } = "Efectivo";
        public decimal EfectivoRecibido { get; private set; }
        public decimal CambioEntregado { get; private set; }

        // El ticket siempre se manda a la impresora fisica que resuelve TicketService.
        public bool Imprimir => true;
        public string? Impresora => null;

        public CobroWindow(decimal total, IEnumerable<ClienteVentaOpcion> clientes)
        {
            InitializeComponent();
            this.total = total;
            lblTotal.Text = total.ToString("C", CultureInfo.GetCultureInfo("es-MX"));

            cmbClientes.ItemsSource = new List<ClienteVentaOpcion>(clientes);
            cmbClientes.SelectedIndex = 0;

            ActualizarCambio();

            Loaded += (_, _) =>
            {
                txtEfectivoRecibido.Focus();
                txtEfectivoRecibido.SelectAll();
            };
        }

        private void CmbMetodoPago_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ActualizarCambio();
        }

        private void TxtEfectivoRecibido_TextChanged(object sender, TextChangedEventArgs e)
        {
            ActualizarCambio();
        }

        private void ActualizarCambio()
        {
            if (txtEfectivoRecibido == null || lblCambio == null || cmbMetodoPago == null)
            {
                return;
            }

            bool esEfectivo = ObtenerMetodoPago().Equals("Efectivo", StringComparison.OrdinalIgnoreCase);
            txtEfectivoRecibido.IsEnabled = esEfectivo;

            if (!esEfectivo)
            {
                lblCambio.Text = "$0.00";
                return;
            }

            decimal recibido = LeerEfectivoRecibido();
            decimal cambio = Math.Max(0, recibido - total);
            lblCambio.Text = cambio.ToString("C", CultureInfo.GetCultureInfo("es-MX"));
        }

        private void BtnConfirmar_Click(object sender, RoutedEventArgs e)
        {
            string metodoPago = ObtenerMetodoPago();
            bool esEfectivo = metodoPago.Equals("Efectivo", StringComparison.OrdinalIgnoreCase);
            decimal efectivoRecibido = esEfectivo ? LeerEfectivoRecibido() : 0;

            if (esEfectivo && efectivoRecibido < total)
            {
                MessageBox.Show("El efectivo recibido no cubre el total de la venta.", "Efectivo insuficiente", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtEfectivoRecibido.Focus();
                txtEfectivoRecibido.SelectAll();
                return;
            }

            MetodoPago = metodoPago;
            EfectivoRecibido = efectivoRecibido;
            CambioEntregado = esEfectivo ? efectivoRecibido - total : 0;
            ClienteId = cmbClientes.SelectedValue is int clienteId && clienteId > 0 ? clienteId : null;

            DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private string ObtenerMetodoPago()
        {
            return (cmbMetodoPago.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Efectivo";
        }

        private decimal LeerEfectivoRecibido()
        {
            return decimal.TryParse(txtEfectivoRecibido.Text, out decimal recibido) && recibido > 0
                ? recibido
                : 0;
        }
    }
}

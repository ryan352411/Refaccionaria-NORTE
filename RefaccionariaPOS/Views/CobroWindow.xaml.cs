using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
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
        public bool Imprimir { get; private set; }
        public string? Impresora { get; private set; }

        public CobroWindow(decimal total, IEnumerable<ClienteVentaOpcion> clientes)
        {
            InitializeComponent();
            this.total = total;
            lblTotal.Text = total.ToString("C", CultureInfo.GetCultureInfo("es-MX"));

            cmbClientes.ItemsSource = new List<ClienteVentaOpcion>(clientes);
            cmbClientes.SelectedIndex = 0;

            CargarImpresoras();
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
            Imprimir = chkImprimirTicket.IsChecked == true;
            Impresora = cmbImpresoras.SelectedItem?.ToString();

            DialogResult = true;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnProbarImpresora_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TicketService.ImprimirTicket(new List<string>
                {
                    "SERVICIO AUTOMOTRIZ LOPEZ",
                    "----------------------------------------",
                    "PRUEBA DE IMPRESORA TERMICA",
                    $"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                    "Impresora lista para tickets.",
                    "----------------------------------------",
                    string.Empty,
                    string.Empty
                }, cmbImpresoras.SelectedItem?.ToString());

                MessageBox.Show("Ticket de prueba enviado a la impresora.", "Impresora", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo imprimir la prueba: " + ex.Message, "Impresora", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CargarImpresoras()
        {
            cmbImpresoras.Items.Clear();

            foreach (string impresora in PrinterSettings.InstalledPrinters)
            {
                cmbImpresoras.Items.Add(impresora);
            }

            string impresoraDefault = new PrinterSettings().PrinterName;
            if (cmbImpresoras.Items.Contains(impresoraDefault))
            {
                cmbImpresoras.SelectedItem = impresoraDefault;
            }
            else if (cmbImpresoras.Items.Count > 0)
            {
                cmbImpresoras.SelectedIndex = 0;
            }
            else
            {
                chkImprimirTicket.IsChecked = false;
                chkImprimirTicket.IsEnabled = false;
                btnProbarImpresora.IsEnabled = false;
            }
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

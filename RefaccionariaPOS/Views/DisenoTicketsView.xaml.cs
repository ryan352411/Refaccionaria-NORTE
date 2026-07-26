using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RefaccionariaPOS.Services;

namespace RefaccionariaPOS.Views
{
    /// <summary>
    /// Editor del encabezado, el pie y el ancho de cada tipo de ticket, con
    /// vista previa en vivo. El ancho se cambia arrastrando los bordes del
    /// ticket en la vista previa.
    /// </summary>
    public partial class DisenoTicketsView : Window
    {
        private const double MargenBordeArrastre = 12;

        private readonly Dictionary<string, TicketPlantilla> plantillasEditadas = new();
        private string tipoActual = TicketPlantillaService.TipoVenta;
        private bool cargandoPlantilla;

        private double anchoCaracterPx;
        private bool arrastrandoAncho;
        private bool arrastreEnBordeDerecho;
        private double puntoInicialArrastreX;
        private int anchoInicialArrastre;

        public DisenoTicketsView()
        {
            InitializeComponent();

            plantillasEditadas[TicketPlantillaService.TipoVenta] =
                TicketPlantillaService.ObtenerPlantilla(TicketPlantillaService.TipoVenta);
            plantillasEditadas[TicketPlantillaService.TipoCorte] =
                TicketPlantillaService.ObtenerPlantilla(TicketPlantillaService.TipoCorte);

            MostrarPlantilla(TicketPlantillaService.TipoVenta);
        }

        private TicketPlantilla PlantillaActual => plantillasEditadas[tipoActual];

        // =====================================================================
        // Vista previa y edicion de la plantilla
        // =====================================================================
        private void MostrarPlantilla(string tipo)
        {
            tipoActual = tipo;
            TicketPlantilla plantilla = plantillasEditadas[tipo];

            cargandoPlantilla = true;
            txtEncabezado.Text = plantilla.Encabezado;
            txtPie.Text = plantilla.Pie;
            cargandoPlantilla = false;

            ActualizarAnchoVisual();
            ActualizarVistaPrevia();
        }

        private void ActualizarVistaPrevia()
        {
            List<string> lineas = tipoActual == TicketPlantillaService.TipoCorte
                ? TicketService.GenerarVistaPreviaCorte(PlantillaActual)
                : TicketService.GenerarVistaPreviaVenta(PlantillaActual);

            txtVistaPrevia.Text = string.Join(Environment.NewLine, lineas);
        }

        private void CmbTipo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTipo.SelectedItem is not ComboBoxItem seleccion || txtEncabezado == null)
            {
                return;
            }

            MostrarPlantilla(seleccion.Tag?.ToString() ?? TicketPlantillaService.TipoVenta);
        }

        private void Plantilla_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (cargandoPlantilla)
            {
                return;
            }

            PlantillaActual.Encabezado = txtEncabezado.Text;
            PlantillaActual.Pie = txtPie.Text;
            ActualizarVistaPrevia();
        }

        // =====================================================================
        // Cambio de ancho arrastrando los bordes del ticket en la vista previa
        // =====================================================================
        private double AnchoCaracterPx
        {
            get
            {
                if (anchoCaracterPx <= 0)
                {
                    FormattedText medida = new FormattedText(
                        "M",
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        new Typeface(txtVistaPrevia.FontFamily, txtVistaPrevia.FontStyle, txtVistaPrevia.FontWeight, txtVistaPrevia.FontStretch),
                        txtVistaPrevia.FontSize,
                        Brushes.Black,
                        VisualTreeHelper.GetDpi(bordeTicket).PixelsPerDip);
                    anchoCaracterPx = medida.WidthIncludingTrailingWhitespace;
                }

                return anchoCaracterPx;
            }
        }

        private void ActualizarAnchoVisual()
        {
            int ancho = TicketPlantillaService.LimitarAncho(PlantillaActual.AnchoCaracteres);
            bordeTicket.Width = ancho * AnchoCaracterPx + bordeTicket.Padding.Left + bordeTicket.Padding.Right;
            lblAncho.Text = $"Ancho: {ancho} caracteres · arrastra los bordes del ticket para cambiarlo";
        }

        private bool EstaSobreBordeLateral(Point posicion, out bool bordeDerecho)
        {
            bordeDerecho = posicion.X >= bordeTicket.ActualWidth - MargenBordeArrastre;
            return posicion.X <= MargenBordeArrastre || bordeDerecho;
        }

        private void BordeTicket_MouseMove(object sender, MouseEventArgs e)
        {
            if (arrastrandoAncho)
            {
                double delta = e.GetPosition(scrollVistaPrevia).X - puntoInicialArrastreX;
                if (!arrastreEnBordeDerecho)
                {
                    delta = -delta;
                }

                // El ticket esta centrado: al mover un borde, el otro se mueve igual.
                int nuevoAncho = anchoInicialArrastre + (int)Math.Round(2 * delta / AnchoCaracterPx);
                AplicarAncho(nuevoAncho);
                return;
            }

            bordeTicket.Cursor = EstaSobreBordeLateral(e.GetPosition(bordeTicket), out _)
                ? Cursors.SizeWE
                : null;
        }

        private void BordeTicket_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!EstaSobreBordeLateral(e.GetPosition(bordeTicket), out bool bordeDerecho))
            {
                return;
            }

            arrastrandoAncho = true;
            arrastreEnBordeDerecho = bordeDerecho;
            puntoInicialArrastreX = e.GetPosition(scrollVistaPrevia).X;
            anchoInicialArrastre = TicketPlantillaService.LimitarAncho(PlantillaActual.AnchoCaracteres);
            bordeTicket.CaptureMouse();
            e.Handled = true;
        }

        private void BordeTicket_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!arrastrandoAncho)
            {
                return;
            }

            arrastrandoAncho = false;
            bordeTicket.ReleaseMouseCapture();
        }

        private void BordeTicket_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!arrastrandoAncho)
            {
                bordeTicket.Cursor = null;
            }
        }

        private void AplicarAncho(int anchoCaracteres)
        {
            int anchoLimitado = TicketPlantillaService.LimitarAncho(anchoCaracteres);
            if (anchoLimitado == PlantillaActual.AnchoCaracteres)
            {
                return;
            }

            PlantillaActual.AnchoCaracteres = anchoLimitado;
            ActualizarAnchoVisual();
            ActualizarVistaPrevia();
        }

        // =====================================================================
        // Acciones
        // =====================================================================
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool guardadoEnBase = TicketPlantillaService.GuardarPlantilla(PlantillaActual);

                if (guardadoEnBase)
                {
                    MessageBox.Show(
                        "La plantilla se guardó. Los próximos tickets saldrán con el nuevo diseño en todas las cajas.",
                        "Diseño de Tickets", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Sin conexión con el servidor: la plantilla quedó guardada solo en esta computadora.\n" +
                        "Vuelve a guardar cuando regrese el internet para aplicarla en las demás cajas.",
                        "Diseño de Tickets", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo guardar la plantilla: " + ex.Message, "Diseño de Tickets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestaurar_Click(object sender, RoutedEventArgs e)
        {
            TicketPlantilla predeterminada = TicketPlantillaService.ObtenerPredeterminada(tipoActual);
            plantillasEditadas[tipoActual] = predeterminada;
            MostrarPlantilla(tipoActual);
        }

        private void BtnImprimirPrueba_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                List<string> lineas = tipoActual == TicketPlantillaService.TipoCorte
                    ? TicketService.GenerarVistaPreviaCorte(PlantillaActual)
                    : TicketService.GenerarVistaPreviaVenta(PlantillaActual);

                TicketService.ImprimirTicket(lineas, anchoCaracteres: PlantillaActual.AnchoCaracteres);
                MessageBox.Show("Ticket de prueba enviado a la impresora.", "Diseño de Tickets", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo imprimir la prueba: " + ex.Message, "Diseño de Tickets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

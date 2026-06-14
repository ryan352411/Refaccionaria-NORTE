using System;
using System.Windows;

namespace RefaccionariaPOS.Views
{
    public partial class MainView : Window
    {
        private const string RolVendedor = "Vendedor";

        private readonly int idUsuarioActual;
        private readonly string usuarioActual;
        private readonly string rolUsuarioActual;

        public MainView(int idUsuario, string usuario, string rol)
        {
            InitializeComponent();

            idUsuarioActual = idUsuario;
            usuarioActual = usuario;
            rolUsuarioActual = rol;

            ConfigurarPermisosPorRol();
        }

        private void ConfigurarPermisosPorRol()
        {
            lblRolVisual.Text = $"{usuarioActual} - {rolUsuarioActual}";

            if (!EsVendedorExacto())
            {
                return;
            }

            btnHistorial.IsEnabled = false;
            btnCorteCaja.IsEnabled = false;
            btnUsuarios.IsEnabled = false;
            lblSubtitulo.Text = "Terminal de cobro activa. Registra ventas y consulta inventario.";
        }

        private void BtnUsuarios_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new RegistrarUsuarioView());
        }

        private void BtnInventario_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new InventarioView(EsVendedorInventario()));
        }

        private void BtnVenta_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new VentaView(idUsuarioActual));
        }

        private void BtnHistorial_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new HistorialVentasView());
        }

        private void BtnCorteCaja_Click(object sender, RoutedEventArgs e)
        {
            MostrarDialogo(new CorteCajaView());
        }

        private bool EsVendedorExacto()
        {
            return rolUsuarioActual == RolVendedor;
        }

        private bool EsVendedorInventario()
        {
            return rolUsuarioActual.Equals(RolVendedor, StringComparison.OrdinalIgnoreCase);
        }

        private static void MostrarDialogo(Window ventana)
        {
            ventana.ShowDialog();
        }
    }
}

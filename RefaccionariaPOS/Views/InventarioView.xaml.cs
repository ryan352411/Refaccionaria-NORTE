using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace RefaccionariaPOS.Views
{
    public partial class InventarioView : Window
    {
        private const string CategoriaTodas = "Todas";
        private const string CategoriaGeneral = "General";

        private const string QueryProductosBase = @"
            SELECT p.id, p.codigo_barras, p.nombre, p.descripcion, p.costo_proveedor, p.precio_venta,
                   p.stock_actual, p.stock_minimo, p.categoria,
                   COALESCE(pi.imagen_url, p.imagen_url, '') AS imagen_url,
                   COALESCE(p.tipo_venta, 'Unidad') AS tipo_venta,
                   (pi.producto_id IS NOT NULL OR COALESCE(p.imagen_url, '') <> '') AS tiene_imagenes
            FROM productos p
            LEFT JOIN LATERAL (
                SELECT producto_id, imagen_url
                FROM producto_imagenes
                WHERE producto_id = p.id
                  AND (COALESCE(octet_length(imagen_data), 0) > 0 OR COALESCE(imagen_url, '') <> '')
                ORDER BY orden, id
                LIMIT 1
            ) pi ON true";

        private const string QueryCategorias = @"
            SELECT DISTINCT categoria
            FROM productos
            WHERE categoria IS NOT NULL AND categoria <> ''
            ORDER BY categoria;";

        private const string QueryVerificarColumnas = @"
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'productos'
              AND column_name IN ('stock_minimo', 'categoria', 'imagen_url', 'tipo_venta');";

        private readonly ObservableCollection<string> categorias = new();
        private static readonly Brush FilaDisponible = Brushes.White;
        private static readonly Brush FilaDisponibleAlterna = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        private static readonly Brush FilaBajoStock = new SolidColorBrush(Color.FromRgb(255, 237, 213));
        private static readonly Brush FilaSinStock = new SolidColorBrush(Color.FromRgb(254, 226, 226));
        private static readonly Brush TextoInventario = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        private readonly bool soloLectura;
        private readonly DispatcherTimer filtroTimer;
        private CancellationTokenSource? cargaProductosCts;
        private List<Producto>? productosEnMemoria;
        private Task? inicializacionTask;
        private bool filtrosListos;

        public InventarioView(bool soloLectura = false)
        {
            InitializeComponent();
            this.soloLectura = soloLectura;
            cmbCategoria.ItemsSource = categorias;

            filtroTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            filtroTimer.Tick += async (_, _) =>
            {
                filtroTimer.Stop();
                await CargarProductosAsync(txtBuscar.Text.Trim());
            };

            Loaded += async (_, _) => await InicializarAsync();
        }

        public async void ActivarDesdePanel()
        {
            await InicializarAsync();
        }

        private Task InicializarAsync()
        {
            return inicializacionTask ??= InicializarCoreAsync();
        }

        private async Task InicializarCoreAsync()
        {
            ConfigurarModoLectura();
            filtrosListos = true;
            Task categoriasTask = CargarCategoriasAsync();
            Task estructuraTask = VerificarColumnasInventarioAsync();
            await CargarProductosAsync();
            await Task.WhenAll(categoriasTask, estructuraTask);
        }

        private async Task CargarProductosAsync(string terminoBusqueda = "")
        {
            string categoria = CategoriaSeleccionada();
            bool soloBajoStock = chkBajoStock.IsChecked == true;

            if (productosEnMemoria is { Count: > 0 })
            {
                dgInventario.ItemsSource = FiltrarProductosEnMemoria(productosEnMemoria, terminoBusqueda, categoria, soloBajoStock);
                lblEstadoCarga.Visibility = Visibility.Collapsed;
                return;
            }

            cargaProductosCts?.Cancel();
            CancellationTokenSource currentCts = new();
            cargaProductosCts = currentCts;
            CancellationToken cancellationToken = currentCts.Token;

            try
            {
                lblEstadoCarga.Text = "Cargando inventario...";
                lblEstadoCarga.Visibility = Visibility.Visible;
                List<Producto> productos = await ObtenerProductosAsync(
                    terminoBusqueda,
                    categoria,
                    soloBajoStock,
                    cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    dgInventario.ItemsSource = productos;
                    if (string.IsNullOrWhiteSpace(terminoBusqueda)
                        && categoria == CategoriaTodas
                        && !soloBajoStock
                        && productos.Count > 0)
                    {
                        productosEnMemoria = productos;
                    }
                    lblEstadoCarga.Text = productos.Count == 0 ? "No se encontraron productos." : string.Empty;
                    lblEstadoCarga.Visibility = productos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                lblEstadoCarga.Text = "No se pudo cargar el inventario.";
                lblEstadoCarga.Visibility = Visibility.Visible;
                MessageBox.Show("Error al cargar el inventario: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (ReferenceEquals(cargaProductosCts, currentCts))
                {
                    cargaProductosCts = null;
                }

                currentCts.Dispose();
            }
        }

        private static List<Producto> FiltrarProductosEnMemoria(
            IEnumerable<Producto> productos,
            string terminoBusqueda,
            string categoria,
            bool soloBajoStock)
        {
            string busqueda = terminoBusqueda.Trim();
            return productos
                .Where(producto => string.IsNullOrEmpty(busqueda)
                    || producto.CodigoBarras.Equals(busqueda, StringComparison.OrdinalIgnoreCase)
                    || producto.Nombre.Contains(busqueda, StringComparison.OrdinalIgnoreCase))
                .Where(producto => categoria == CategoriaTodas || producto.Categoria.Equals(categoria, StringComparison.OrdinalIgnoreCase))
                .Where(producto => !soloBajoStock || producto.Stock <= producto.StockMinimo)
                .OrderBy(producto => producto.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .Take(200)
                .ToList();
        }

        private void InvalidarCacheProductos()
        {
            productosEnMemoria = null;
        }

        private static async Task<List<Producto>> ObtenerProductosAsync(
            string terminoBusqueda,
            string categoria,
            bool soloBajoStock,
            CancellationToken cancellationToken)
        {
            List<Producto> productos = new();
            DatabaseConnection db = new DatabaseConnection();

            using NpgsqlConnection conexion = db.GetConnection();
            await conexion.OpenAsync(cancellationToken);

            List<string> filtros = new();
            if (!string.IsNullOrWhiteSpace(terminoBusqueda))
            {
                filtros.Add("(p.codigo_barras = @busqueda OR p.nombre ILIKE @busquedaLike)");
            }

            if (categoria != CategoriaTodas)
            {
                filtros.Add("p.categoria = @categoria");
            }

            if (soloBajoStock)
            {
                filtros.Add("p.stock_actual <= p.stock_minimo");
            }

            string query = QueryProductosBase
                + (filtros.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", filtros))
                + " ORDER BY p.nombre ASC LIMIT 200;";

            using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            if (!string.IsNullOrWhiteSpace(terminoBusqueda))
            {
                cmd.Parameters.AddWithValue("@busqueda", terminoBusqueda);
                cmd.Parameters.AddWithValue("@busquedaLike", "%" + terminoBusqueda + "%");
            }

            if (categoria != CategoriaTodas)
            {
                cmd.Parameters.AddWithValue("@categoria", categoria);
            }
            cmd.CommandTimeout = 15;

            using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                productos.Add(CrearProducto(reader));
            }

            return productos;
        }

        private static Producto CrearProducto(NpgsqlDataReader reader)
        {
            return new Producto
            {
                Id = Convert.ToInt32(reader["id"]),
                CodigoBarras = reader["codigo_barras"].ToString() ?? string.Empty,
                Nombre = reader["nombre"].ToString() ?? string.Empty,
                Descripcion = reader["descripcion"].ToString() ?? string.Empty,
                Categoria = reader["categoria"].ToString() ?? CategoriaGeneral,
                ImagenUrl = reader["imagen_url"].ToString() ?? string.Empty,
                TieneImagenes = reader["tiene_imagenes"] is bool tieneImagenes && tieneImagenes,
                TipoVenta = reader["tipo_venta"].ToString() ?? "Unidad",
                PrecioCompra = Convert.ToDecimal(reader["costo_proveedor"]),
                PrecioVenta = Convert.ToDecimal(reader["precio_venta"]),
                Stock = Convert.ToDecimal(reader["stock_actual"]),
                StockMinimo = Convert.ToDecimal(reader["stock_minimo"])
            };
        }

        private async void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            if (soloLectura)
            {
                MessageBox.Show("Tu rol permite consultar inventario, pero no agregar productos.", "Permiso de lectura", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RegistrarProductoView frm = new RegistrarProductoView { Owner = Application.Current.MainWindow };
            if (frm.ShowDialog() != true)
            {
                return;
            }

            InvalidarCacheProductos();
            await CargarCategoriasAsync();
            await CargarProductosAsync(txtBuscar.Text.Trim());
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            ProgramarCargaProductos();
        }

        private void CmbCategoria_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (filtrosListos)
            {
                ProgramarCargaProductos();
            }
        }

        private async void ChkBajoStock_Click(object sender, RoutedEventArgs e)
        {
            await CargarProductosAsync(txtBuscar.Text.Trim());
        }

        private async void BtnLimpiarFiltros_Click(object sender, RoutedEventArgs e)
        {
            filtroTimer.Stop();
            txtBuscar.Clear();
            cmbCategoria.SelectedIndex = 0;
            chkBajoStock.IsChecked = false;
            await CargarProductosAsync();
        }

        private void ProgramarCargaProductos()
        {
            if (!filtrosListos)
            {
                return;
            }

            filtroTimer.Stop();
            filtroTimer.Start();
        }

        private void DgInventario_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(dgInventario, e.OriginalSource as DependencyObject) is not DataGridRow fila
                || fila.Item is not Producto producto)
            {
                return;
            }

            AbrirVisorImagenes(producto);
        }

        private void ImagenProducto_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elemento && elemento.DataContext is Producto producto)
            {
                e.Handled = true;
                AbrirVisorImagenes(producto);
            }
        }

        private void MenuVerImagenes_Click(object sender, RoutedEventArgs e)
        {
            if (dgInventario.SelectedItem is Producto producto)
            {
                AbrirVisorImagenes(producto);
            }
        }

        private void AbrirVisorImagenes(Producto producto)
        {
            ImagenesProductoWindow visor = new ImagenesProductoWindow(producto.Id, producto.Nombre)
            {
                Owner = this
            };
            visor.ShowDialog();
        }

        private void DgInventario_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is not Producto producto)
            {
                return;
            }

            e.Row.Foreground = TextoInventario;

            if (producto.Stock == 0)
            {
                e.Row.Background = FilaSinStock;
                return;
            }

            if (producto.Stock <= producto.StockMinimo)
            {
                e.Row.Background = FilaBajoStock;
                return;
            }

            e.Row.Background = e.Row.GetIndex() % 2 == 0 ? FilaDisponible : FilaDisponibleAlterna;
        }

        private async void MenuActualizarStock_Click(object sender, RoutedEventArgs e)
        {
            if (soloLectura)
            {
                MessageBox.Show("Tu rol permite consultar inventario, pero no modificar stock.", "Permiso de lectura", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (dgInventario.SelectedItem is not Producto productoSeleccionado)
            {
                return;
            }

            string? nuevoStockStr = PedirValor("Actualizar Stock",
                $"Ingresa el nuevo stock físico para:\n{productoSeleccionado.Nombre}",
                productoSeleccionado.Stock.ToString());

            if (decimal.TryParse(nuevoStockStr, out decimal nuevoStock) && nuevoStock >= 0)
            {
                await ActualizarStockEnBaseDeDatosAsync(productoSeleccionado.CodigoBarras, nuevoStock);
                InvalidarCacheProductos();
                await CargarProductosAsync(txtBuscar.Text.Trim());
                return;
            }

            if (nuevoStockStr != null)
            {
                MessageBox.Show("Por favor, ingresa un numero valido (no negativo).", "Dato invalido", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static async Task ActualizarStockEnBaseDeDatosAsync(string codigo, decimal nuevoStock)
        {
            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    await conexion.OpenAsync();

                    using (NpgsqlCommand cmd = new NpgsqlCommand("UPDATE productos SET stock_actual = @stock WHERE codigo_barras = @codigo;", conexion))
                    {
                        cmd.Parameters.AddWithValue("@stock", nuevoStock);
                        cmd.Parameters.AddWithValue("@codigo", codigo);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al actualizar el stock en la BD: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CargarCategoriasAsync()
        {
            string categoriaActual = CategoriaSeleccionada();
            List<string> categoriasDesdeBase = await ObtenerCategoriasAsync();
            categorias.Clear();
            categorias.Add(CategoriaTodas);

            foreach (string categoria in categoriasDesdeBase)
            {
                categorias.Add(categoria);
            }

            cmbCategoria.SelectedItem = categorias.Contains(categoriaActual) ? categoriaActual : CategoriaTodas;
        }

        private static async Task<List<string>> ObtenerCategoriasAsync()
        {
            List<string> resultado = new();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using NpgsqlConnection conexion = db.GetConnection();
                await conexion.OpenAsync();

                using NpgsqlCommand cmd = new NpgsqlCommand(QueryCategorias, conexion);
                using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    resultado.Add(reader["categoria"].ToString() ?? CategoriaGeneral);
                }
            }
            catch
            {
                resultado.Add(CategoriaGeneral);
            }

            return resultado;
        }

        private static async Task VerificarColumnasInventarioAsync()
        {
            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using NpgsqlConnection conexion = db.GetConnection();
                await conexion.OpenAsync();

                using NpgsqlCommand cmd = new NpgsqlCommand(QueryVerificarColumnas, conexion);
                int columnas = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (columnas < 4)
                {
                    await AsegurarColumnasProductoAsync(conexion);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo verificar la estructura de inventario: " + ex.Message, "Inventario", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ConfigurarModoLectura()
        {
            if (!soloLectura)
            {
                return;
            }

            Title = "Inventario de Refacciones - Solo lectura";
            btnNuevoProducto.Visibility = Visibility.Collapsed;
            menuActualizarStock.Visibility = Visibility.Collapsed;
        }

        private string CategoriaSeleccionada()
        {
            return cmbCategoria.SelectedItem?.ToString() ?? CategoriaTodas;
        }

        private string? PedirValor(string titulo, string mensaje, string valorActual)
        {
            Window ventana = CrearVentanaEntrada(titulo, mensaje, valorActual);

            if (ventana.Content is StackPanel panel && panel.Children[1] is TextBox input)
            {
                input.Focus();
            }

            return ventana.ShowDialog() == true
                ? ((TextBox)((StackPanel)ventana.Content).Children[1]).Text
                : null;
        }

        private static Window CrearVentanaEntrada(string titulo, string mensaje, string valorActual)
        {
            TextBox txtInput = new TextBox { Text = valorActual };
            txtInput.SelectAll();

            Button btnAceptar = new Button
            {
                Content = "Guardar Stock",
                Width = 110,
                Margin = new Thickness(0, 15, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true
            };

            Window ventana = new Window
            {
                Title = titulo,
                Width = 350,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            btnAceptar.Click += (_, _) => ventana.DialogResult = true;

            StackPanel panel = new StackPanel { Margin = new Thickness(15) };
            panel.Children.Add(new TextBlock { Text = mensaje, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(txtInput);
            panel.Children.Add(btnAceptar);
            ventana.Content = panel;

            return ventana;
        }

        private static async Task AsegurarColumnasProductoAsync(NpgsqlConnection conexion)
        {
            const string query = @"
                ALTER TABLE productos
                    ADD COLUMN IF NOT EXISTS imagen_url text NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS tipo_venta varchar(20) NOT NULL DEFAULT 'Unidad';

                ALTER TABLE productos
                    ALTER COLUMN stock_actual TYPE numeric(12, 3) USING stock_actual::numeric,
                    ALTER COLUMN stock_minimo TYPE numeric(12, 3) USING stock_minimo::numeric;";

            using NpgsqlCommand cmd = new NpgsqlCommand(query, conexion);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public class ImagenProductoConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Producto producto)
            {
                return ProductImageService.CargarImagen(producto.ImagenData, producto.ImagenUrl, 92);
            }

            return ProductImageService.CargarImagen(value?.ToString() ?? string.Empty, 92);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

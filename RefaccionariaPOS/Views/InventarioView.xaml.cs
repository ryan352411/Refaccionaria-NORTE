using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RefaccionariaPOS.Views
{
    public partial class InventarioView : Window
    {
        private const string CategoriaTodas = "Todas";
        private const string CategoriaGeneral = "General";

        private const string QueryProductos = @"
            SELECT p.id, p.codigo_barras, p.nombre, p.descripcion, p.costo_proveedor, p.precio_venta,
                   p.stock_actual, p.stock_minimo, p.categoria,
                   COALESCE(pi.imagen_url, p.imagen_url, '') AS imagen_url,
                   pi.imagen_data,
                   COALESCE(p.tipo_venta, 'Unidad') AS tipo_venta
            FROM productos p
            LEFT JOIN LATERAL (
                SELECT imagen_url, imagen_data
                FROM producto_imagenes
                WHERE producto_id = p.id
                ORDER BY orden, id
                LIMIT 1
            ) pi ON true
            WHERE (p.nombre ILIKE @busqueda OR p.codigo_barras ILIKE @busqueda OR p.descripcion ILIKE @busqueda)
              AND (@categoria = 'Todas' OR categoria = @categoria)
              AND (@soloBajoStock = false OR stock_actual <= stock_minimo)
            ORDER BY p.nombre ASC;";

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
        private static readonly Brush FilaDisponibleAlterna = new SolidColorBrush(Color.FromRgb(236, 240, 241));
        private static readonly Brush FilaBajoStock = new SolidColorBrush(Color.FromRgb(255, 237, 213));
        private static readonly Brush FilaSinStock = new SolidColorBrush(Color.FromRgb(254, 226, 226));
        private static readonly Brush TextoInventario = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        private readonly bool soloLectura;
        private bool filtrosListos;

        public InventarioView(bool soloLectura = false)
        {
            InitializeComponent();
            this.soloLectura = soloLectura;

            VerificarColumnasInventario();
            ConfigurarModoLectura();
            cmbCategoria.ItemsSource = categorias;
            CargarCategorias();
            CargarProductos();
            filtrosListos = true;
        }

        private void CargarProductos(string terminoBusqueda = "")
        {
            try
            {
                dgInventario.ItemsSource = ObtenerProductos(terminoBusqueda);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar el inventario: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<Producto> ObtenerProductos(string terminoBusqueda)
        {
            List<Producto> productos = new();
            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();
                ProductImageRepository.AsegurarTabla(conexion);

                using (NpgsqlCommand cmd = new NpgsqlCommand(QueryProductos, conexion))
                {
                    cmd.Parameters.AddWithValue("@busqueda", "%" + terminoBusqueda + "%");
                    cmd.Parameters.AddWithValue("@categoria", CategoriaSeleccionada());
                    cmd.Parameters.AddWithValue("@soloBajoStock", chkBajoStock.IsChecked == true);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            productos.Add(CrearProducto(reader));
                        }
                    }
                }
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
                ImagenData = reader["imagen_data"] is DBNull ? null : (byte[])reader["imagen_data"],
                TipoVenta = reader["tipo_venta"].ToString() ?? "Unidad",
                PrecioCompra = Convert.ToDecimal(reader["costo_proveedor"]),
                PrecioVenta = Convert.ToDecimal(reader["precio_venta"]),
                Stock = Convert.ToDecimal(reader["stock_actual"]),
                StockMinimo = Convert.ToDecimal(reader["stock_minimo"])
            };
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
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

            CargarCategorias();
            CargarProductos(txtBuscar.Text.Trim());
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            CargarProductos(txtBuscar.Text.Trim());
        }

        private void CmbCategoria_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (filtrosListos)
            {
                CargarProductos(txtBuscar.Text.Trim());
            }
        }

        private void ChkBajoStock_Click(object sender, RoutedEventArgs e)
        {
            CargarProductos(txtBuscar.Text.Trim());
        }

        private void BtnLimpiarFiltros_Click(object sender, RoutedEventArgs e)
        {
            txtBuscar.Clear();
            cmbCategoria.SelectedIndex = 0;
            chkBajoStock.IsChecked = false;
            CargarProductos();
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

        private void MenuActualizarStock_Click(object sender, RoutedEventArgs e)
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
                ActualizarStockEnBaseDeDatos(productoSeleccionado.CodigoBarras, nuevoStock);
                CargarProductos(txtBuscar.Text.Trim());
                return;
            }

            if (nuevoStockStr != null)
            {
                MessageBox.Show("Por favor, ingresa un numero valido (no negativo).", "Dato invalido", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ActualizarStockEnBaseDeDatos(string codigo, decimal nuevoStock)
        {
            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    using (NpgsqlCommand cmd = new NpgsqlCommand("UPDATE productos SET stock_actual = @stock WHERE codigo_barras = @codigo;", conexion))
                    {
                        cmd.Parameters.AddWithValue("@stock", nuevoStock);
                        cmd.Parameters.AddWithValue("@codigo", codigo);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al actualizar el stock en la BD: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CargarCategorias()
        {
            string categoriaActual = CategoriaSeleccionada();
            categorias.Clear();
            categorias.Add(CategoriaTodas);

            foreach (string categoria in ObtenerCategorias())
            {
                categorias.Add(categoria);
            }

            cmbCategoria.SelectedItem = categorias.Contains(categoriaActual) ? categoriaActual : CategoriaTodas;
        }

        private List<string> ObtenerCategorias()
        {
            List<string> resultado = new();

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    using (NpgsqlCommand cmd = new NpgsqlCommand(QueryCategorias, conexion))
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            resultado.Add(reader["categoria"].ToString() ?? CategoriaGeneral);
                        }
                    }
                }
            }
            catch
            {
                resultado.Add(CategoriaGeneral);
            }

            return resultado;
        }

        private void VerificarColumnasInventario()
        {
            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();
                    AsegurarColumnasProducto(conexion);
                    ProductImageRepository.AsegurarTabla(conexion);

                    using (NpgsqlCommand cmd = new NpgsqlCommand(QueryVerificarColumnas, conexion))
                    {
                        int columnas = Convert.ToInt32(cmd.ExecuteScalar());
                        if (columnas < 4)
                        {
                            AsegurarColumnasProducto(conexion);
                        }
                    }
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

        private static void AsegurarColumnasProducto(NpgsqlConnection conexion)
        {
            const string query = @"
                ALTER TABLE productos
                    ADD COLUMN IF NOT EXISTS imagen_url text NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS tipo_venta varchar(20) NOT NULL DEFAULT 'Unidad';

                ALTER TABLE productos
                    ALTER COLUMN stock_actual TYPE numeric(12, 3) USING stock_actual::numeric,
                    ALTER COLUMN stock_minimo TYPE numeric(12, 3) USING stock_minimo::numeric;";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
            {
                cmd.ExecuteNonQuery();
            }
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

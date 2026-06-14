using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public partial class InventarioView : Window
    {
        private const string CategoriaTodas = "Todas";
        private const string CategoriaGeneral = "General";

        private const string QueryProductos = @"
            SELECT id, codigo_barras, nombre, descripcion, costo_proveedor, precio_venta,
                   stock_actual, stock_minimo, categoria
            FROM productos
            WHERE (nombre ILIKE @busqueda OR codigo_barras ILIKE @busqueda OR descripcion ILIKE @busqueda)
              AND (@categoria = 'Todas' OR categoria = @categoria)
              AND (@soloBajoStock = false OR stock_actual <= stock_minimo)
            ORDER BY nombre ASC;";

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
              AND column_name IN ('stock_minimo', 'categoria');";

        private readonly ObservableCollection<string> categorias = new();
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
                PrecioCompra = Convert.ToDecimal(reader["costo_proveedor"]),
                PrecioVenta = Convert.ToDecimal(reader["precio_venta"]),
                Stock = Convert.ToInt32(reader["stock_actual"]),
                StockMinimo = Convert.ToInt32(reader["stock_minimo"])
            };
        }

        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            if (soloLectura)
            {
                MessageBox.Show("Tu rol permite consultar inventario, pero no agregar productos.", "Permiso de lectura", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RegistrarProductoView frm = new RegistrarProductoView { Owner = this };
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

            if (int.TryParse(nuevoStockStr, out int nuevoStock) && nuevoStock >= 0)
            {
                ActualizarStockEnBaseDeDatos(productoSeleccionado.CodigoBarras, nuevoStock);
                CargarProductos(txtBuscar.Text.Trim());
                return;
            }

            if (nuevoStockStr != null)
            {
                MessageBox.Show("Por favor, ingresa un número entero válido (no negativo).", "Dato inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ActualizarStockEnBaseDeDatos(string codigo, int nuevoStock)
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

                    using (NpgsqlCommand cmd = new NpgsqlCommand(QueryVerificarColumnas, conexion))
                    {
                        int columnas = Convert.ToInt32(cmd.ExecuteScalar());
                        if (columnas < 2)
                        {
                            MessageBox.Show("Faltan las columnas categoria o stock_minimo en productos. Ejecuta la migración de inventario antes de usar estos filtros.", "Inventario", MessageBoxButton.OK, MessageBoxImage.Warning);
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
    }
}

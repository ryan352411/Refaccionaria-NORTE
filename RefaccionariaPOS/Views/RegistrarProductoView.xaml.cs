using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RefaccionariaPOS.Views
{
    public partial class RegistrarProductoView : Window
    {
        private const string CategoriaGeneral = "General";
        private const string SqlStateUniqueViolation = "23505";

        private const string QueryInsertarProducto = @"
            INSERT INTO productos
            (codigo_barras, nombre, descripcion, costo_proveedor, precio_venta, stock_actual, stock_minimo, categoria, tipo_venta)
            VALUES (@codigo, @nombre, @desc, @costo, @venta, @stock, @stockMinimo, @categoria, @tipoVenta)
            RETURNING id;";

        private const string QueryActualizarProducto = @"
            UPDATE productos
            SET nombre = @nombre,
                descripcion = @desc,
                costo_proveedor = @costo,
                precio_venta = @venta,
                stock_minimo = @stockMinimo,
                categoria = @categoria,
                tipo_venta = @tipoVenta,
                stock_actual = stock_actual + @stockAgregar
            WHERE id = @id;";

        private const string QueryBuscarProducto = @"
            SELECT p.id, p.nombre, p.descripcion, p.costo_proveedor, p.precio_venta, p.stock_minimo, p.categoria,
                   COALESCE(pi.imagen_url, p.imagen_url, '') AS imagen_url,
                   COALESCE(p.tipo_venta, 'Unidad') AS tipo_venta
            FROM productos p
            LEFT JOIN producto_imagenes pi ON pi.producto_id = p.id
            WHERE p.codigo_barras = @codigo
            LIMIT 1;";

        private const string QueryCategorias = @"
            SELECT DISTINCT categoria
            FROM productos
            WHERE categoria IS NOT NULL AND categoria <> ''
            ORDER BY categoria;";

        private int? productoExistenteId;
        private string ultimoCodigoConsultado = string.Empty;

        public RegistrarProductoView()
        {
            InitializeComponent();
            CargarCategorias();
            Loaded += (_, _) => txtCodigo.Focus();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (!TryLeerFormulario(out decimal costo, out decimal precioVenta, out decimal stockMinimo, out decimal stock))
            {
                return;
            }

            try
            {
                GuardarProducto(costo, precioVenta, stockMinimo, stock);
                DialogResult = true;
                Close();
            }
            catch (PostgresException ex) when (ex.SqlState == SqlStateUniqueViolation)
            {
                MessageBox.Show("Ese código de barras ya existe. Escanéalo de nuevo para cargar sus datos y agregar stock.", "Producto existente", MessageBoxButton.OK, MessageBoxImage.Warning);
                BuscarProductoExistente(force: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar el producto: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool TryLeerFormulario(out decimal costo, out decimal precioVenta, out decimal stockMinimo, out decimal stock)
        {
            costo = 0;
            precioVenta = 0;
            stockMinimo = 0;
            stock = 0;

            if (string.IsNullOrWhiteSpace(txtCodigo.Text) || string.IsNullOrWhiteSpace(txtNombre.Text))
            {
                MessageBox.Show("Por favor, llena el código y el nombre.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(txtCosto.Text, out costo) || costo < 0)
            {
                MessageBox.Show("Ingresa un costo válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(txtPrecioVenta.Text, out precioVenta) || precioVenta < 0)
            {
                MessageBox.Show("Ingresa un precio de venta válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!decimal.TryParse(txtStockMinimo.Text, out stockMinimo) || stockMinimo < 0)
            {
                MessageBox.Show("Ingresa un stock mínimo válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (decimal.TryParse(txtStock.Text, out stock) && stock >= 0)
            {
                return true;
            }

            MessageBox.Show(productoExistenteId.HasValue
                ? "Ingresa cuántas piezas vas a agregar al inventario."
                : "Ingresa el stock inicial del producto.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void GuardarProducto(decimal costo, decimal precioVenta, decimal stockMinimo, decimal stock)
        {
            if (productoExistenteId.HasValue)
            {
                ActualizarProductoExistente(productoExistenteId.Value, costo, precioVenta, stockMinimo, stock);
                MessageBox.Show("Producto actualizado y stock agregado correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            InsertarProductoNuevo(costo, precioVenta, stockMinimo, stock);
            MessageBox.Show("Producto registrado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void InsertarProductoNuevo(decimal costo, decimal precioVenta, decimal stockMinimo, decimal stock)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                using (NpgsqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        int productoId = 0;
                        using (NpgsqlCommand cmd = new NpgsqlCommand(QueryInsertarProducto, conexion, transaccion))
                        {
                            string imagenLocal = PrepararImagenLocal();
                            AgregarParametrosProducto(cmd, costo, precioVenta, stockMinimo);
                            cmd.Parameters.AddWithValue("@stock", stock);
                            productoId = Convert.ToInt32(cmd.ExecuteScalar());
                            ProductImageRepository.GuardarImagen(conexion, productoId, imagenLocal);
                        }

                        transaccion.Commit();
                    }
                    catch
                    {
                        transaccion.Rollback();
                        throw;
                    }
                }
            }
        }

        private void ActualizarProductoExistente(int idProducto, decimal costo, decimal precioVenta, decimal stockMinimo, decimal stockAgregar)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                using (NpgsqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        // Obtener valores anteriores para auditoría
                        ObtenerValoresAnteriores(conexion, transaccion, idProducto, out decimal costoPrevio,
                            out decimal precioPrevio, out decimal stockMinimoPrevio, out decimal stockActualPrevio, out string categoriaPreviea);

                        using (NpgsqlCommand cmd = new NpgsqlCommand(QueryActualizarProducto, conexion, transaccion))
                        {
                            string imagenLocal = PrepararImagenLocal();
                            AgregarParametrosProducto(cmd, costo, precioVenta, stockMinimo);
                            cmd.Parameters.AddWithValue("@stockAgregar", stockAgregar);
                            cmd.Parameters.AddWithValue("@id", idProducto);
                            cmd.ExecuteNonQuery();
                            ProductImageRepository.GuardarImagen(conexion, idProducto, imagenLocal);
                        }

                        transaccion.Commit();
                    }
                    catch
                    {
                        transaccion.Rollback();
                        throw;
                    }
                }
            }
        }

        private string PrepararImagenLocal()
        {
            return ProductImageService.GuardarImagenLocal(txtImagenUrl.Text.Trim(), txtCodigo.Text.Trim());
        }

        private void AgregarParametrosProducto(NpgsqlCommand cmd, decimal costo, decimal precioVenta, decimal stockMinimo)
        {
            cmd.Parameters.AddWithValue("@codigo", txtCodigo.Text.Trim());
            cmd.Parameters.AddWithValue("@nombre", txtNombre.Text.Trim());
            cmd.Parameters.AddWithValue("@desc", txtDescripcion.Text.Trim());
            cmd.Parameters.AddWithValue("@costo", costo);
            cmd.Parameters.AddWithValue("@venta", precioVenta);
            cmd.Parameters.AddWithValue("@stockMinimo", stockMinimo);
            cmd.Parameters.AddWithValue("@categoria", ObtenerCategoria());
            cmd.Parameters.AddWithValue("@tipoVenta", ObtenerTipoVenta());
        }

        private void BtnSeleccionarImagen_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialogo = new OpenFileDialog
            {
                Title = "Seleccionar imagen del producto",
                Filter = "Imagenes|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Todos los archivos|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialogo.ShowDialog(this) == true)
            {
                txtImagenUrl.Text = dialogo.FileName;
            }
        }

        private void TxtCodigo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            BuscarProductoExistente(force: true);
        }

        private void TxtCodigo_LostFocus(object sender, RoutedEventArgs e)
        {
            BuscarProductoExistente(force: false);
        }

        private void BuscarProductoExistente(bool force)
        {
            string codigo = txtCodigo.Text.Trim();
            if (string.IsNullOrWhiteSpace(codigo))
            {
                ReiniciarModoNuevo();
                return;
            }

            if (!force && codigo == ultimoCodigoConsultado)
            {
                return;
            }

            ultimoCodigoConsultado = codigo;

            try
            {
                CargarProductoExistente(codigo);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo buscar el código de barras: " + ex.Message, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CargarProductoExistente(string codigo)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                using (NpgsqlCommand cmd = new NpgsqlCommand(QueryBuscarProducto, conexion))
                {
                    cmd.Parameters.AddWithValue("@codigo", codigo);

                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            ReiniciarModoNuevo();
                            return;
                        }

                        AplicarProductoExistente(reader);
                    }
                }
            }
        }

        private void AplicarProductoExistente(NpgsqlDataReader reader)
        {
            productoExistenteId = Convert.ToInt32(reader["id"]);
            txtNombre.Text = reader["nombre"].ToString() ?? string.Empty;
            txtDescripcion.Text = reader["descripcion"].ToString() ?? string.Empty;
            txtCosto.Text = Convert.ToDecimal(reader["costo_proveedor"]).ToString("0.##");
            txtPrecioVenta.Text = Convert.ToDecimal(reader["precio_venta"]).ToString("0.##");
            txtStockMinimo.Text = Convert.ToDecimal(reader["stock_minimo"]).ToString("0.###");
            cmbCategoria.Text = reader["categoria"].ToString() ?? CategoriaGeneral;
            txtImagenUrl.Text = reader["imagen_url"].ToString() ?? string.Empty;
            SeleccionarTipoVenta(reader["tipo_venta"].ToString() ?? "Unidad");
            txtStock.Clear();

            lblModo.Text = "PRODUCTO EXISTENTE";
            lblModo.Foreground = System.Windows.Media.Brushes.DarkOrange;
            lblStockCaption.Text = "Stock a Agregar:";
            btnGuardar.Content = "Actualizar Stock";
            txtStock.Focus();
        }

        private void ReiniciarModoNuevo()
        {
            productoExistenteId = null;
            lblModo.Text = "NUEVO PRODUCTO";
            lblModo.Foreground = System.Windows.Media.Brushes.DarkSlateGray;
            lblStockCaption.Text = "Stock Inicial:";
            btnGuardar.Content = "Guardar";
        }

        private void ObtenerValoresAnteriores(NpgsqlConnection conexion, NpgsqlTransaction transaccion,
            int idProducto, out decimal costo, out decimal precio, out decimal stockMinimo,
            out decimal stockActual, out string categoria)
        {
            costo = 0;
            precio = 0;
            stockMinimo = 0;
            stockActual = 0;
            categoria = "General";

            const string query = @"
                SELECT costo_proveedor, precio_venta, stock_minimo, stock_actual, categoria
                FROM productos
                WHERE id = @id;";

            using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion, transaccion))
            {
                cmd.Parameters.AddWithValue("@id", idProducto);

                using (NpgsqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        costo = Convert.ToDecimal(reader["costo_proveedor"]);
                        precio = Convert.ToDecimal(reader["precio_venta"]);
                        stockMinimo = Convert.ToDecimal(reader["stock_minimo"]);
                        stockActual = Convert.ToDecimal(reader["stock_actual"]);
                        categoria = reader["categoria"].ToString() ?? "General";
                    }
                }
            }
        }

        private void CargarCategorias()
        {
            List<string> categorias = CategoriasBase();

            try
            {
                AgregarCategoriasDesdeBase(categorias);
            }
            catch
            {
            }

            cmbCategoria.ItemsSource = categorias;
            cmbCategoria.Text = CategoriaGeneral;
        }

        private static List<string> CategoriasBase()
        {
            return new List<string>
            {
                CategoriaGeneral,
                "Motor",
                "Frenos",
                "Suspension",
                "Electrico",
                "Aceites",
                "Transmision",
                "Direccion"
            };
        }

        private void AgregarCategoriasDesdeBase(List<string> categorias)
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
                        string categoria = reader["categoria"].ToString() ?? CategoriaGeneral;
                        if (!categorias.Contains(categoria))
                        {
                            categorias.Add(categoria);
                        }
                    }
                }
            }
        }

        private string ObtenerCategoria()
        {
            string categoria = cmbCategoria.Text.Trim();
            return string.IsNullOrWhiteSpace(categoria) ? CategoriaGeneral : categoria;
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private string ObtenerTipoVenta()
        {
            string tipoBase = (cmbTipoVenta.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Unidad";

            // Si es Granel, agregar la medida seleccionada
            if (tipoBase == "Granel" && cmbMedidaGranel.SelectedItem is ComboBoxItem medida)
            {
                return $"Granel - {medida.Content}";
            }

            return tipoBase;
        }

        private void SeleccionarTipoVenta(string tipoVenta)
        {
            foreach (object item in cmbTipoVenta.Items)
            {
                if (item is ComboBoxItem comboBoxItem
                    && string.Equals(comboBoxItem.Content.ToString(), tipoVenta, StringComparison.OrdinalIgnoreCase))
                {
                    cmbTipoVenta.SelectedItem = comboBoxItem;
                    return;
                }
            }

            cmbTipoVenta.SelectedIndex = 0;
        }

        private void CmbTipoVenta_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTipoVenta.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            string tipoSeleccionado = item.Content.ToString() ?? "";

            if (tipoSeleccionado == "Granel")
            {
                // Mostrar opciones de granel
                panelGranel.Visibility = Visibility.Visible;
                cmbMedidaGranel.SelectedIndex = 0; // Litro por defecto
                txtPrecioGranel.Text = txtPrecioVenta.Text;
            }
            else
            {
                // Ocultar opciones de granel
                panelGranel.Visibility = Visibility.Collapsed;
                txtPrecioVenta.Text = txtPrecioGranel.Text;
            }
        }

        private void CmbMedidaGranel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // La etiqueta de precio se actualiza automáticamente según la medida seleccionada
            if (cmbMedidaGranel.SelectedItem is ComboBoxItem medida)
            {
                // Puedes agregar lógica aquí si necesitas cambiar el precio base según la medida
                // Por ejemplo: precio por litro vs precio por metro
            }
        }

    }
}

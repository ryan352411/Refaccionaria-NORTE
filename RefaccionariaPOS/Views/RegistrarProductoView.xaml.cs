using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace RefaccionariaPOS.Views
{
    public partial class RegistrarProductoView : Window
    {
        private const string CategoriaGeneral = "General";
        private const string SqlStateUniqueViolation = "23505";

        private const string QueryInsertarProducto = @"
            INSERT INTO productos
            (codigo_barras, nombre, descripcion, costo_proveedor, precio_venta, stock_actual, stock_minimo, categoria)
            VALUES (@codigo, @nombre, @desc, @costo, @venta, @stock, @stockMinimo, @categoria);";

        private const string QueryActualizarProducto = @"
            UPDATE productos
            SET nombre = @nombre,
                descripcion = @desc,
                costo_proveedor = @costo,
                precio_venta = @venta,
                stock_minimo = @stockMinimo,
                categoria = @categoria,
                stock_actual = stock_actual + @stockAgregar
            WHERE id = @id;";

        private const string QueryBuscarProducto = @"
            SELECT id, nombre, descripcion, costo_proveedor, precio_venta, stock_minimo, categoria
            FROM productos
            WHERE codigo_barras = @codigo
            LIMIT 1;";

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

        private int? productoExistenteId;
        private string ultimoCodigoConsultado = string.Empty;

        public RegistrarProductoView()
        {
            InitializeComponent();
            VerificarColumnasInventario();
            CargarCategorias();
            Loaded += (_, _) => txtCodigo.Focus();
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (!TryLeerFormulario(out decimal costo, out decimal precioVenta, out int stockMinimo, out int stock))
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

        private bool TryLeerFormulario(out decimal costo, out decimal precioVenta, out int stockMinimo, out int stock)
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

            if (!int.TryParse(txtStockMinimo.Text, out stockMinimo) || stockMinimo < 0)
            {
                MessageBox.Show("Ingresa un stock mínimo válido.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (int.TryParse(txtStock.Text, out stock) && stock >= 0)
            {
                return true;
            }

            MessageBox.Show(productoExistenteId.HasValue
                ? "Ingresa cuántas piezas vas a agregar al inventario."
                : "Ingresa el stock inicial del producto.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void GuardarProducto(decimal costo, decimal precioVenta, int stockMinimo, int stock)
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

        private void InsertarProductoNuevo(decimal costo, decimal precioVenta, int stockMinimo, int stock)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                using (NpgsqlCommand cmd = new NpgsqlCommand(QueryInsertarProducto, conexion))
                {
                    AgregarParametrosProducto(cmd, costo, precioVenta, stockMinimo);
                    cmd.Parameters.AddWithValue("@stock", stock);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void ActualizarProductoExistente(int idProducto, decimal costo, decimal precioVenta, int stockMinimo, int stockAgregar)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                using (NpgsqlCommand cmd = new NpgsqlCommand(QueryActualizarProducto, conexion))
                {
                    AgregarParametrosProducto(cmd, costo, precioVenta, stockMinimo);
                    cmd.Parameters.AddWithValue("@stockAgregar", stockAgregar);
                    cmd.Parameters.AddWithValue("@id", idProducto);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void AgregarParametrosProducto(NpgsqlCommand cmd, decimal costo, decimal precioVenta, int stockMinimo)
        {
            cmd.Parameters.AddWithValue("@codigo", txtCodigo.Text.Trim());
            cmd.Parameters.AddWithValue("@nombre", txtNombre.Text.Trim());
            cmd.Parameters.AddWithValue("@desc", txtDescripcion.Text.Trim());
            cmd.Parameters.AddWithValue("@costo", costo);
            cmd.Parameters.AddWithValue("@venta", precioVenta);
            cmd.Parameters.AddWithValue("@stockMinimo", stockMinimo);
            cmd.Parameters.AddWithValue("@categoria", ObtenerCategoria());
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
            txtStockMinimo.Text = Convert.ToInt32(reader["stock_minimo"]).ToString();
            cmbCategoria.Text = reader["categoria"].ToString() ?? CategoriaGeneral;
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
                            MessageBox.Show("Faltan las columnas categoria o stock_minimo en productos. Ejecuta la migración de inventario antes de registrar productos.", "Producto", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo verificar la estructura de inventario: " + ex.Message, "Producto", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RefaccionariaPOS.Views
{
    public partial class VentaView : Window
    {
        private ObservableCollection<ProductoCarrito> listaCarrito = new ObservableCollection<ProductoCarrito>();
        private decimal totalVenta = 0;
        private Producto? productoEnVistaPrevia; // Reutilizamos tu variable perfectamente
        private readonly int usuarioId;
        private CancellationTokenSource? busquedaCancellation;
        private int busquedaVersion;

        public VentaView(int usuarioId)
        {
            InitializeComponent();
            this.usuarioId = usuarioId;
            dgCarrito.ItemsSource = listaCarrito;
            CargarImpresoras();
            Loaded += (_, _) => txtBuscarId.Focus();
        }

        public VentaView() : this(0)
        {
        }

        // ==========================================================
        // 1. BUSCADOR EN TIEMPO REAL (Reemplaza al BuscarProductoReal)
        // ==========================================================
        private void TxtBuscarId_TextChanged(object sender, TextChangedEventArgs e)
        {
            string busqueda = txtBuscarId.Text.Trim();
            CancelarBusquedaPendiente();

            // Si hay menos de 2 letras, ocultamos la tablita flotante y la vista previa
            if (busqueda.Length < 2)
            {
                if (dgResultadosBusqueda != null) dgResultadosBusqueda.Visibility = Visibility.Collapsed;
                OcultarVistaPrevia();
                return;
            }

            busquedaCancellation = new CancellationTokenSource();
            int versionActual = ++busquedaVersion;
            _ = BuscarProductosConEsperaAsync(busqueda, versionActual, busquedaCancellation.Token);
        }

        private async Task BuscarProductosConEsperaAsync(string busqueda, int version, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(250, cancellationToken);
                List<Producto> resultados = await BuscarProductosAsync(busqueda, cancellationToken);

                if (cancellationToken.IsCancellationRequested || version != busquedaVersion)
                {
                    return;
                }

                if (resultados.Count > 0)
                {
                    dgResultadosBusqueda.ItemsSource = resultados;
                    dgResultadosBusqueda.Visibility = Visibility.Visible;
                    OcultarVistaPrevia();
                    return;
                }

                dgResultadosBusqueda.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    dgResultadosBusqueda.Visibility = Visibility.Collapsed;
                }
            }
        }

        private static async Task<List<Producto>> BuscarProductosAsync(string busqueda, CancellationToken cancellationToken)
        {
            List<Producto> resultados = new List<Producto>();
            DatabaseConnection db = new DatabaseConnection();

            await using (NpgsqlConnection conexion = db.GetConnection())
            {
                await conexion.OpenAsync(cancellationToken);

                const string query = @"SELECT codigo_barras, nombre, precio_venta, stock_actual
                                       FROM productos
                                       WHERE nombre ILIKE @busqueda OR codigo_barras ILIKE @busqueda
                                       ORDER BY nombre ASC LIMIT 15;";

                await using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@busqueda", "%" + busqueda + "%");

                    await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            resultados.Add(new Producto
                            {
                                CodigoBarras = reader["codigo_barras"].ToString() ?? string.Empty,
                                Nombre = reader["nombre"].ToString() ?? string.Empty,
                                PrecioVenta = Convert.ToDecimal(reader["precio_venta"]),
                                Stock = Convert.ToInt32(reader["stock_actual"])
                            });
                        }
                    }
                }
            }

            return resultados;
        }

        // ==========================================================
        // 2. AL SELECCIONAR UN PRODUCTO DE LA LISTA FLOTANTE
        // ==========================================================
        private void DgResultadosBusqueda_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgResultadosBusqueda.SelectedItem is Producto seleccionado)
            {
                // Pasamos el producto seleccionado a tu variable global
                productoEnVistaPrevia = seleccionado;

                // Llamamos a tu método que ya tenías para llenar la interfaz
                MostrarVistaPrevia(productoEnVistaPrevia);

                // Ocultamos la lista flotante porque ya eligió uno
                dgResultadosBusqueda.Visibility = Visibility.Collapsed;

                // Limpiamos la selección para que pueda volver a elegir el mismo después si quiere
                dgResultadosBusqueda.SelectedItem = null;
            }
        }
        private async void TxtBuscarId_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CancelarBusquedaPendiente();
            await ProcesarCodigoEscaneadoAsync();
        }

        // ==========================================================
        // TUS MÉTODOS ORIGINALES (Se mantienen intactos)
        // ==========================================================
        private void MostrarVistaPrevia(Producto p)
        {
            lblPreviewCodigo.Text = p.CodigoBarras;
            lblPreviewNombre.Text = p.Nombre;
            lblPreviewPrecio.Text = string.Format("{0:C}", p.PrecioVenta);
            lblPreviewStock.Text = p.Stock.ToString();
            txtCantidadAgregar.Text = "1";


            brdPreview.Visibility = Visibility.Visible;
            btnAgregarAlCarrito.Visibility = Visibility.Visible;

            txtCantidadAgregar.Focus();
            txtCantidadAgregar.SelectAll();
        }

        private void OcultarVistaPrevia()
        {
            brdPreview.Visibility = Visibility.Collapsed;
            btnAgregarAlCarrito.Visibility = Visibility.Collapsed;
            productoEnVistaPrevia = null;
        }

        private void BtnAgregarAlCarrito_Click(object sender, RoutedEventArgs e)
        {
            if (productoEnVistaPrevia == null) return;

            if (!int.TryParse(txtCantidadAgregar.Text, out int cantidad) || cantidad <= 0)
            {
                MessageBox.Show("Ingresa una cantidad válida a agregar.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AgregarProductoAlCarrito(productoEnVistaPrevia, cantidad);
        }

        private void AgregarProductoAlCarrito(Producto producto, int cantidad)
        {
            if (cantidad > producto.Stock)
            {
                MessageBox.Show($"¡Error de Stock! Solo quedan {producto.Stock} piezas.", "Sin Existencias", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var itemExistente = listaCarrito.FirstOrDefault(i => i.CodigoBarras == producto.CodigoBarras);

            if (itemExistente != null)
            {
                if ((itemExistente.Cantidad + cantidad) > producto.Stock)
                {
                    MessageBox.Show($"Límite excedido. Ya tienes {itemExistente.Cantidad} en el carrito.", "Límite de Stock", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                itemExistente.Cantidad += cantidad;
                dgCarrito.Items.Refresh();
            }
            else
            {
                listaCarrito.Add(new ProductoCarrito
                {
                    CodigoBarras = producto.CodigoBarras,
                    Nombre = producto.Nombre,
                    PrecioVenta = producto.PrecioVenta,
                    Cantidad = cantidad
                });
            }

            ActualizarTotales();
            txtBuscarId.Clear();
            OcultarVistaPrevia();
            txtBuscarId.Focus();
        }

        private async Task ProcesarCodigoEscaneadoAsync()
        {
            string codigo = txtBuscarId.Text.Trim();
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return;
            }

            Producto? producto;
            try
            {
                producto = await BuscarProductoPorCodigoExactoAsync(codigo);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo consultar el código escaneado: " + ex.Message, "Error de conexión", MessageBoxButton.OK, MessageBoxImage.Error);
                txtBuscarId.SelectAll();
                return;
            }

            if (producto == null)
            {
                MessageBox.Show("No se encontró una refacción con el código escaneado: " + codigo, "Código no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtBuscarId.SelectAll();
                return;
            }

            AgregarProductoAlCarrito(producto, 1);
        }

        private static async Task<Producto?> BuscarProductoPorCodigoExactoAsync(string codigo)
        {
            DatabaseConnection db = new DatabaseConnection();

            await using (NpgsqlConnection conexion = db.GetConnection())
            {
                await conexion.OpenAsync();
                const string query = @"SELECT codigo_barras, nombre, precio_venta, stock_actual
                                       FROM productos
                                       WHERE codigo_barras = @codigo
                                       LIMIT 1;";

                await using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                {
                    cmd.Parameters.AddWithValue("@codigo", codigo);

                    await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!await reader.ReadAsync())
                        {
                            return null;
                        }

                        return new Producto
                        {
                            CodigoBarras = reader["codigo_barras"].ToString() ?? string.Empty,
                            Nombre = reader["nombre"].ToString() ?? string.Empty,
                            PrecioVenta = Convert.ToDecimal(reader["precio_venta"]),
                            Stock = Convert.ToInt32(reader["stock_actual"])
                        };
                    }
                }
            }
        }

        private void CancelarBusquedaPendiente()
        {
            busquedaVersion++;
            busquedaCancellation?.Cancel();
            busquedaCancellation?.Dispose();
            busquedaCancellation = null;
        }

        private void ActualizarTotales()
        {
            totalVenta = listaCarrito.Sum(item => item.Subtotal);
            lblTotalCarrito.Text = string.Format("{0:C}", totalVenta);
        }

        // =========================================================================
        // MOTOR DE COBRO DEFINITIVO: POSTGRESQL GENERA EL FOLIO Y C# LO IMPRIME
        // =========================================================================
        private void BtnCobrar_Click(object sender, RoutedEventArgs e)
        {
            if (listaCarrito.Count == 0)
            {
                MessageBox.Show("El carrito de compras está vacío.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int ventaIdGenerado = 0;
            int folioGeneradoBaseDatos = 0;
            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                using (NpgsqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        string queryVenta = @"
                    INSERT INTO ventas (usuario_id, total, fecha_venta, estado, metodo_pago)
                    VALUES (@usuarioId, @total, @fecha, @estado, @metodoPago)
                    RETURNING id, folio;";

                        using (NpgsqlCommand cmdVenta = new NpgsqlCommand(queryVenta, conexion, transaccion))
                        {
                            cmdVenta.Parameters.AddWithValue("@usuarioId", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                            cmdVenta.Parameters.AddWithValue("@total", totalVenta);
                            cmdVenta.Parameters.AddWithValue("@fecha", DateTime.Now);
                            cmdVenta.Parameters.AddWithValue("@estado", "Completada");
                            cmdVenta.Parameters.AddWithValue("@metodoPago", "Mostrador");

                            using (NpgsqlDataReader reader = cmdVenta.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    ventaIdGenerado = Convert.ToInt32(reader["id"]);
                                    folioGeneradoBaseDatos = Convert.ToInt32(reader["folio"]);
                                }
                            }
                        }

                        foreach (var item in listaCarrito)
                        {
                            int productoId = 0;

                            string queryProducto = @"
                        SELECT id, stock_actual
                        FROM productos
                        WHERE codigo_barras = @codigo;";

                            using (NpgsqlCommand cmdProducto = new NpgsqlCommand(queryProducto, conexion, transaccion))
                            {
                                cmdProducto.Parameters.AddWithValue("@codigo", item.CodigoBarras);

                                using (NpgsqlDataReader reader = cmdProducto.ExecuteReader())
                                {
                                    if (!reader.Read())
                                    {
                                        throw new Exception("No se encontró el producto con código: " + item.CodigoBarras);
                                    }

                                    productoId = Convert.ToInt32(reader["id"]);
                                    int stockActual = Convert.ToInt32(reader["stock_actual"]);

                                    if (stockActual < item.Cantidad)
                                    {
                                        throw new Exception("Stock insuficiente para el producto: " + item.Nombre);
                                    }
                                }
                            }

                            string queryDetalle = @"
                        INSERT INTO detalles_venta
                        (venta_id, producto_id, cantidad, precio_unitario, subtotal)
                        VALUES
                        (@ventaId, @productoId, @cantidad, @precioUnitario, @subtotal);";

                            using (NpgsqlCommand cmdDetalle = new NpgsqlCommand(queryDetalle, conexion, transaccion))
                            {
                                cmdDetalle.Parameters.AddWithValue("@ventaId", ventaIdGenerado);
                                cmdDetalle.Parameters.AddWithValue("@productoId", productoId);
                                cmdDetalle.Parameters.AddWithValue("@cantidad", item.Cantidad);
                                cmdDetalle.Parameters.AddWithValue("@precioUnitario", item.PrecioVenta);
                                cmdDetalle.Parameters.AddWithValue("@subtotal", item.Subtotal);

                                cmdDetalle.ExecuteNonQuery();
                            }

                            string queryStock = @"
                        UPDATE productos
                        SET stock_actual = stock_actual - @cantidad
                        WHERE codigo_barras = @codigo;";

                            using (NpgsqlCommand cmdStock = new NpgsqlCommand(queryStock, conexion, transaccion))
                            {
                                cmdStock.Parameters.AddWithValue("@cantidad", item.Cantidad);
                                cmdStock.Parameters.AddWithValue("@codigo", item.CodigoBarras);

                                cmdStock.ExecuteNonQuery();
                            }
                        }

                        transaccion.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaccion.Rollback();
                        MessageBox.Show("Error al procesar la venta en la Base de Datos: " + ex.Message, "Venta Cancelada", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            GenerarTicket(folioGeneradoBaseDatos);

            MessageBox.Show("¡Venta con Folio #" + folioGeneradoBaseDatos + " procesada con éxito!", "Venta Completada", MessageBoxButton.OK, MessageBoxImage.Information);

            listaCarrito.Clear();
            ActualizarTotales();
            txtBuscarId.Focus();
        }

        private void GenerarTicket(int folio)
        {
            try
            {
                List<string> lineasTicket = CrearLineasTicket(folio);
                string rutaCarpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tickets_RefaxManager");

                if (!Directory.Exists(rutaCarpeta))
                {
                    Directory.CreateDirectory(rutaCarpeta);
                }

                string rutaArchivo = Path.Combine(rutaCarpeta, $"Ticket_{folio}.txt");

                File.WriteAllLines(rutaArchivo, lineasTicket);

                if (chkImprimirTicket.IsChecked == true)
                {
                    ImprimirTicket(lineasTicket);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("La venta se registró, pero no se pudo generar o imprimir el ticket: " + ex.Message, "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private List<string> CrearLineasTicket(int folio)
        {
            List<string> lineas = new List<string>
            {
                "SERVICIO AUTOMOTRIZ LOPEZ",
                "Refacciones y Accesorios Automotrices",
                "Direccion:",
                "Calle Principal #123, Col. Centro, Tehuacan, Puebla",
                "Tel / WhatsApp:",
                "(238) 000-0000",
                $"Ticket No.: {folio}",
                $"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                string.Empty,
                "#  Clave        Descripcion                 Marca      Cant.  P. Unit.      Total",
                "--------------------------------------------------------------------------------"
            };

            int numeroLinea = 1;
            foreach (var item in listaCarrito.Take(15))
            {
                lineas.Add(string.Format(
                    "{0,-2} {1,-12} {2,-27} {3,-10} {4,5} {5,9:C} {6,10:C}",
                    numeroLinea,
                    AjustarTexto(item.CodigoBarras, 12),
                    AjustarTexto(item.Nombre, 27),
                    string.Empty,
                    item.Cantidad,
                    item.PrecioVenta,
                    item.Subtotal));
                numeroLinea++;
            }

            while (numeroLinea <= 15)
            {
                lineas.Add($"{numeroLinea,-2}");
                numeroLinea++;
            }

            lineas.Add("--------------------------------------------------------------------------------");
            lineas.Add(string.Format("{0,63} {1,14:C}", "Subtotal:", totalVenta));
            lineas.Add(string.Format("{0,63} {1,14:C}", "TOTAL:", totalVenta));
            lineas.Add(string.Empty);
            lineas.Add("Gracias por su preferencia  |  Servicio Automotriz Lopez  |  \"Tu refaccionaria de confianza\"");

            return lineas;
        }

        private static string AjustarTexto(string texto, int longitudMaxima)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }

            return texto.Length <= longitudMaxima
                ? texto
                : texto.Substring(0, longitudMaxima);
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

        private void ImprimirTicket(List<string> lineasTicket)
        {
            string? impresoraSeleccionada = cmbImpresoras.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(impresoraSeleccionada))
            {
                throw new InvalidOperationException("No hay una impresora seleccionada.");
            }

            int lineaActual = 0;
            using (PrintDocument documento = new PrintDocument())
            {
                documento.PrinterSettings.PrinterName = impresoraSeleccionada;
                documento.PrintPage += (sender, e) =>
                {
                    if (e.Graphics == null)
                    {
                        return;
                    }

                    using Font fuente = new Font("Courier New", 8);
                    Brush brocha = Brushes.Black;
                    float altoLinea = fuente.GetHeight(e.Graphics) + 2;
                    float x = e.MarginBounds.Left;
                    float y = e.MarginBounds.Top;

                    while (lineaActual < lineasTicket.Count)
                    {
                        if (y + altoLinea > e.MarginBounds.Bottom)
                        {
                            e.HasMorePages = true;
                            return;
                        }

                        e.Graphics.DrawString(lineasTicket[lineaActual], fuente, brocha, x, y);
                        y += altoLinea;
                        lineaActual++;
                    }

                    e.HasMorePages = false;
                };

                documento.Print();
            }
        }

        private void BtnProbarImpresora_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ImprimirTicket(new List<string>
                {
                    "========================================",
                    "      PRUEBA DE IMPRESORA RefaxManager  ",
                    "========================================",
                    $"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                    "Impresora lista para tickets.",
                    "========================================"
                });

                MessageBox.Show("Ticket de prueba enviado a la impresora.", "Impresora", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo imprimir la prueba: " + ex.Message, "Impresora", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public class ProductoCarrito
    {
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal PrecioVenta { get; set; }
        public int Cantidad { get; set; }
        public decimal Subtotal => PrecioVenta * Cantidad;
    }
}

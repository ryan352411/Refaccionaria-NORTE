using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Models;
using RefaccionariaPOS.Services;
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
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace RefaccionariaPOS.Views
{
    public partial class VentaView : Window
    {
        private ObservableCollection<ProductoCarrito> listaCarrito = new ObservableCollection<ProductoCarrito>();
        private decimal totalVenta = 0;
        private Producto? productoEnVistaPrevia; // Reutilizamos tu variable perfectamente
        private readonly int usuarioId;
        private readonly string codigoInicial;
        private CancellationTokenSource? busquedaCancellation;
        private int busquedaVersion;
        private bool activada;

        public VentaView(int usuarioId, string codigoInicial = "")
        {
            InitializeComponent();
            this.usuarioId = usuarioId;
            this.codigoInicial = codigoInicial;
            dgCarrito.ItemsSource = listaCarrito;
            PreviewKeyDown += VentaView_PreviewKeyDown;
            CargarClientesFrecuentes();
            CargarImpresoras();
            Loaded += VentaView_Loaded;
        }

        public VentaView() : this(0)
        {
        }

        private async void VentaView_Loaded(object sender, RoutedEventArgs e)
        {
            await ActivarAsync();
        }

        public async void ActivarDesdePanel()
        {
            await ActivarAsync();
        }

        private async Task ActivarAsync()
        {
            if (activada)
            {
                return;
            }

            activada = true;
            txtBuscarId.Focus();
            if (string.IsNullOrWhiteSpace(codigoInicial))
            {
                return;
            }

            txtBuscarId.Text = codigoInicial;
            await ProcesarCodigoEscaneadoAsync();
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

                const string query = @"SELECT id, codigo_barras, nombre, precio_venta, stock_actual,
                                              COALESCE(tipo_venta, 'Unidad') AS tipo_venta
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
                                Stock = Convert.ToDecimal(reader["stock_actual"]),
                                TipoVenta = reader["tipo_venta"].ToString() ?? "Unidad",
                                Id = Convert.ToInt32(reader["id"])
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
            lblPreviewStock.Text = FormatearCantidad(p.Stock);
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

            if (!decimal.TryParse(txtCantidadAgregar.Text, out decimal cantidad) || cantidad <= 0)
            {
                MessageBox.Show("Ingresa una cantidad válida a agregar.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (productoEnVistaPrevia.TipoVenta.Equals("Unidad", StringComparison.OrdinalIgnoreCase) && !EsCantidadEntera(cantidad))
            {
                MessageBox.Show("Este producto se vende por unidad. Ingresa una cantidad entera.", "Cantidad", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AgregarProductoAlCarrito(productoEnVistaPrevia, cantidad);
        }

        private void AgregarProductoAlCarrito(Producto producto, decimal cantidad)
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
                    ProductoId = producto.Id,
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

        private void VentaView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBoxBase)
            {
                return;
            }

            if (dgCarrito.SelectedItem is not ProductoCarrito item)
            {
                return;
            }

            if (e.Key == Key.Add || e.Key == Key.OemPlus)
            {
                item.Cantidad++;
                dgCarrito.Items.Refresh();
                ActualizarTotales();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Subtract || e.Key == Key.OemMinus)
            {
                item.Cantidad--;
                if (item.Cantidad <= 0)
                {
                    listaCarrito.Remove(item);
                }

                dgCarrito.Items.Refresh();
                ActualizarTotales();
                e.Handled = true;
            }
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
                const string query = @"SELECT id, codigo_barras, nombre, precio_venta, stock_actual,
                                              COALESCE(tipo_venta, 'Unidad') AS tipo_venta
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
                            Stock = Convert.ToDecimal(reader["stock_actual"]),
                            TipoVenta = reader["tipo_venta"].ToString() ?? "Unidad",
                            Id = Convert.ToInt32(reader["id"])
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
            ActualizarCambio();
        }

        private void TxtEfectivoRecibido_TextChanged(object sender, TextChangedEventArgs e)
        {
            ActualizarCambio();
        }

        private void CmbMetodoPago_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
            decimal cambio = Math.Max(0, recibido - totalVenta);
            lblCambio.Text = cambio.ToString("C");
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

        private int? ObtenerClienteSeleccionadoId()
        {
            return cmbClientes.SelectedValue is int clienteId && clienteId > 0
                ? clienteId
                : null;
        }

        private async void CargarClientesFrecuentes()
        {
            List<ClienteVentaOpcion> clientes = new()
            {
                new ClienteVentaOpcion { Id = 0, Nombre = "Sin cliente" }
            };

            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    await conexion.OpenAsync();

                    const string query = @"
                        SELECT id, nombre
                        FROM clientes
                        ORDER BY nombre ASC
                        LIMIT 500;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (reader.Read())
                        {
                            clientes.Add(new ClienteVentaOpcion
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Nombre = reader["nombre"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            catch
            {
            }

            cmbClientes.ItemsSource = clientes;
            cmbClientes.SelectedValue = 0;
        }

        private void SumarPuntoClienteSeleccionado(NpgsqlConnection conexion, NpgsqlTransaction transaccion)
        {
            int? clienteId = ObtenerClienteSeleccionadoId();
            if (!clienteId.HasValue)
            {
                return;
            }

            using (NpgsqlCommand cmd = new NpgsqlCommand("UPDATE clientes SET puntos = puntos + 1 WHERE id = @clienteId;", conexion, transaccion))
            {
                cmd.Parameters.AddWithValue("@clienteId", clienteId.Value);
                cmd.ExecuteNonQuery();
            }
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
            string metodoPago = ObtenerMetodoPago();
            decimal efectivoRecibido = metodoPago.Equals("Efectivo", StringComparison.OrdinalIgnoreCase)
                ? LeerEfectivoRecibido()
                : 0;
            decimal cambioEntregado = metodoPago.Equals("Efectivo", StringComparison.OrdinalIgnoreCase)
                ? efectivoRecibido - totalVenta
                : 0;

            if (metodoPago.Equals("Efectivo", StringComparison.OrdinalIgnoreCase) && efectivoRecibido < totalVenta)
            {
                MessageBox.Show("El efectivo recibido no cubre el total de la venta.", "Efectivo insuficiente", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtEfectivoRecibido.Focus();
                return;
            }

            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();

                using (NpgsqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        string queryVenta = @"
                    INSERT INTO ventas (usuario_id, cliente_id, total, fecha_venta, estado, metodo_pago, efectivo_recibido, cambio_entregado)
                    VALUES (@usuarioId, @clienteId, @total, @fecha, @estado, @metodoPago, @efectivoRecibido, @cambioEntregado)
                    RETURNING id, folio;";

                        using (NpgsqlCommand cmdVenta = new NpgsqlCommand(queryVenta, conexion, transaccion))
                        {
                            cmdVenta.Parameters.AddWithValue("@usuarioId", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                            cmdVenta.Parameters.AddWithValue("@clienteId", ObtenerClienteSeleccionadoId().HasValue ? (object)ObtenerClienteSeleccionadoId()!.Value : DBNull.Value);
                            cmdVenta.Parameters.AddWithValue("@total", totalVenta);
                            cmdVenta.Parameters.AddWithValue("@fecha", DateTime.Now);
                            cmdVenta.Parameters.AddWithValue("@estado", "Completada");
                            cmdVenta.Parameters.AddWithValue("@metodoPago", metodoPago);
                            cmdVenta.Parameters.AddWithValue("@efectivoRecibido", efectivoRecibido);
                            cmdVenta.Parameters.AddWithValue("@cambioEntregado", cambioEntregado);

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
                            int? productoId = item.ProductoId > 0 ? item.ProductoId : null;

                            if (!item.EsArticuloComun)
                            {
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
                                    decimal stockActual = Convert.ToDecimal(reader["stock_actual"]);

                                        if (stockActual < item.Cantidad)
                                        {
                                            throw new Exception("Stock insuficiente para el producto: " + item.Nombre);
                                        }
                                    }
                                }
                            }

                            string queryDetalle = @"
                        INSERT INTO detalles_venta
                        (venta_id, producto_id, cantidad, precio_unitario, subtotal, descripcion_manual, tipo_articulo)
                        VALUES
                        (@ventaId, @productoId, @cantidad, @precioUnitario, @subtotal, @descripcionManual, @tipoArticulo);";

                            using (NpgsqlCommand cmdDetalle = new NpgsqlCommand(queryDetalle, conexion, transaccion))
                            {
                                cmdDetalle.Parameters.AddWithValue("@ventaId", ventaIdGenerado);
                                cmdDetalle.Parameters.AddWithValue("@productoId", productoId.HasValue ? (object)productoId.Value : DBNull.Value);
                                cmdDetalle.Parameters.AddWithValue("@cantidad", item.Cantidad);
                                cmdDetalle.Parameters.AddWithValue("@precioUnitario", item.PrecioVenta);
                                cmdDetalle.Parameters.AddWithValue("@subtotal", item.Subtotal);
                                cmdDetalle.Parameters.AddWithValue("@descripcionManual", item.EsArticuloComun ? item.Nombre : string.Empty);
                                cmdDetalle.Parameters.AddWithValue("@tipoArticulo", item.EsArticuloComun ? "Comun" : "Inventario");

                                cmdDetalle.ExecuteNonQuery();
                            }

                            if (item.EsArticuloComun)
                            {
                                continue;
                            }

                            string queryStock = @"
                        UPDATE productos
                        SET stock_actual = stock_actual - @cantidad
                        WHERE codigo_barras = @codigo AND stock_actual >= @cantidad;";

                            using (NpgsqlCommand cmdStock = new NpgsqlCommand(queryStock, conexion, transaccion))
                            {
                                cmdStock.Parameters.AddWithValue("@cantidad", item.Cantidad);
                                cmdStock.Parameters.AddWithValue("@codigo", item.CodigoBarras);

                                int filasAfectadas = cmdStock.ExecuteNonQuery();
                                if (filasAfectadas == 0)
                                {
                                    throw new Exception("Stock insuficiente para el producto: " + item.Nombre);
                                }
                            }
                        }

                        SumarPuntoClienteSeleccionado(conexion, transaccion);

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

            GenerarTicket(ventaIdGenerado, folioGeneradoBaseDatos);

            MessageBox.Show("¡Venta con Folio #" + folioGeneradoBaseDatos + " procesada con éxito!", "Venta Completada", MessageBoxButton.OK, MessageBoxImage.Information);

            listaCarrito.Clear();
            txtEfectivoRecibido.Clear();
            ActualizarTotales();
            txtBuscarId.Focus();
        }

        private void GenerarTicket(int ventaId, int folio)
        {
            try
            {
                TicketService.GenerarTicketVenta(
                    ventaId,
                    chkImprimirTicket.IsChecked == true,
                    cmbImpresoras.SelectedItem?.ToString());
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
                    FormatearCantidad(item.Cantidad),
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

        private static string FormatearCantidad(decimal cantidad)
        {
            return cantidad % 1 == 0 ? cantidad.ToString("0") : cantidad.ToString("0.###");
        }

        private static bool EsCantidadEntera(decimal cantidad)
        {
            return cantidad % 1 == 0;
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
                TicketService.ImprimirTicket(new List<string>
                {
                    "REFACCIONARIA NORTE",
                    "AV. DIVICION DEL NORTE N.63 COL. CENTRO",
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
    }

    public class ProductoCarrito
    {
        public int ProductoId { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal PrecioVenta { get; set; }
        public decimal Cantidad { get; set; }
        public bool EsArticuloComun { get; set; }
        public decimal Subtotal => PrecioVenta * Cantidad;
    }

    public class ClienteVentaOpcion
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }
}

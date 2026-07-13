using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RefaccionariaPOS.Views
{
    public partial class ArticulosComunesView : Window
    {
        private readonly int usuarioId;
        private readonly ObservableCollection<ProductoCarrito> articulos = new();
        private decimal totalVenta;
        private List<ClienteVentaOpcion> clientesVenta = new();

        public ArticulosComunesView(int usuarioId)
        {
            InitializeComponent();
            this.usuarioId = usuarioId;
            dgArticulos.ItemsSource = articulos;
            AsegurarColumnasArticulosComunes();
            CargarClientesFrecuentes();
            Loaded += (_, _) => txtNombre.Focus();
            ActualizarTotales();
        }

        public void ActivarDesdePanel()
        {
            txtNombre.Focus();
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            AgregarArticulo();
        }

        private void CamposArticulo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            AgregarArticulo();
        }

        private void AgregarArticulo()
        {
            string nombre = txtNombre.Text.Trim();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                MessageBox.Show("Escribe el nombre del articulo.", "Articulos comunes", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNombre.Focus();
                return;
            }

            if (!decimal.TryParse(txtCantidad.Text, out decimal cantidad) || cantidad <= 0)
            {
                MessageBox.Show("Ingresa una cantidad valida.", "Articulos comunes", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtCantidad.Focus();
                txtCantidad.SelectAll();
                return;
            }

            if (!decimal.TryParse(txtPrecio.Text, out decimal precio) || precio < 0)
            {
                MessageBox.Show("Ingresa un precio valido.", "Articulos comunes", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPrecio.Focus();
                txtPrecio.SelectAll();
                return;
            }

            articulos.Add(new ProductoCarrito
            {
                CodigoBarras = "COMUN",
                Nombre = nombre,
                Cantidad = cantidad,
                PrecioVenta = precio,
                EsArticuloComun = true
            });

            txtNombre.Clear();
            txtCantidad.Text = "1";
            txtPrecio.Clear();
            ActualizarTotales();
            txtNombre.Focus();
        }

        private void BtnQuitar_Click(object sender, RoutedEventArgs e)
        {
            QuitarSeleccionado();
        }

        private void DgArticulos_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
            {
                return;
            }

            e.Handled = true;
            QuitarSeleccionado();
        }

        private void QuitarSeleccionado()
        {
            if (dgArticulos.SelectedItem is not ProductoCarrito item)
            {
                return;
            }

            articulos.Remove(item);
            ActualizarTotales();
        }

        private void ActualizarTotales()
        {
            totalVenta = articulos.Sum(item => item.Subtotal);
            lblTotal.Text = totalVenta.ToString("C");
            lblEstado.Text = $"{articulos.Count} articulos en la venta.";
        }

        private void BtnRegistrarVenta_Click(object sender, RoutedEventArgs e)
        {
            if (articulos.Count == 0)
            {
                MessageBox.Show("Agrega al menos un articulo.", "Articulos comunes", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtNombre.Focus();
                return;
            }

            CobroWindow cobro = new CobroWindow(totalVenta, clientesVenta);
            Window? duenio = Application.Current?.MainWindow;
            if (duenio != null && duenio.IsVisible && !ReferenceEquals(duenio, this))
            {
                cobro.Owner = duenio;
                cobro.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            if (cobro.ShowDialog() != true)
            {
                return;
            }

            try
            {
                (int ventaId, int folio) = RegistrarVentaComun(cobro.MetodoPago, cobro.EfectivoRecibido, cobro.CambioEntregado, cobro.ClienteId);
                TicketService.GenerarTicketVenta(ventaId, cobro.Imprimir, cobro.Impresora);

                // Aviso por Telegram a los destinatarios configurados (no bloquea ni afecta la venta si falla).
                TelegramNotificationService.NotificarVentaEnSegundoPlano(ventaId);

                articulos.Clear();
                ActualizarTotales();
                txtNombre.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo registrar la venta comun: " + ex.Message, "Articulos comunes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private (int ventaId, int folio) RegistrarVentaComun(string metodoPago, decimal efectivoRecibido, decimal cambioEntregado, int? clienteId)
        {
            DatabaseConnection db = new DatabaseConnection();
            using (NpgsqlConnection conexion = db.GetConnection())
            {
                conexion.Open();
                using (NpgsqlTransaction transaccion = conexion.BeginTransaction())
                {
                    int ventaId;
                    int folio;

                    const string queryVenta = @"
                        INSERT INTO ventas (usuario_id, cliente_id, total, fecha_venta, estado, metodo_pago, efectivo_recibido, cambio_entregado)
                        VALUES (@usuarioId, @clienteId, @total, @fecha, @estado, @metodoPago, @efectivoRecibido, @cambioEntregado)
                        RETURNING id, folio;";

                    using (NpgsqlCommand cmdVenta = new NpgsqlCommand(queryVenta, conexion, transaccion))
                    {
                        cmdVenta.Parameters.AddWithValue("@usuarioId", usuarioId == 0 ? DBNull.Value : (object)usuarioId);
                        cmdVenta.Parameters.AddWithValue("@clienteId", clienteId.HasValue ? (object)clienteId.Value : DBNull.Value);
                        cmdVenta.Parameters.AddWithValue("@total", totalVenta);
                        cmdVenta.Parameters.AddWithValue("@fecha", DateTime.Now);
                        cmdVenta.Parameters.AddWithValue("@estado", "Completada");
                        cmdVenta.Parameters.AddWithValue("@metodoPago", metodoPago);
                        cmdVenta.Parameters.AddWithValue("@efectivoRecibido", efectivoRecibido);
                        cmdVenta.Parameters.AddWithValue("@cambioEntregado", cambioEntregado);

                        using (NpgsqlDataReader reader = cmdVenta.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                throw new InvalidOperationException("No se pudo crear la venta.");
                            }

                            ventaId = Convert.ToInt32(reader["id"]);
                            folio = Convert.ToInt32(reader["folio"]);
                        }
                    }

                    const string queryDetalle = @"
                        INSERT INTO detalles_venta
                            (venta_id, producto_id, cantidad, precio_unitario, subtotal, descripcion_manual, tipo_articulo)
                        VALUES
                            (@ventaId, NULL, @cantidad, @precioUnitario, @subtotal, @descripcionManual, 'Comun');";

                    foreach (ProductoCarrito item in articulos)
                    {
                        using (NpgsqlCommand cmdDetalle = new NpgsqlCommand(queryDetalle, conexion, transaccion))
                        {
                            cmdDetalle.Parameters.AddWithValue("@ventaId", ventaId);
                            cmdDetalle.Parameters.AddWithValue("@cantidad", item.Cantidad);
                            cmdDetalle.Parameters.AddWithValue("@precioUnitario", item.PrecioVenta);
                            cmdDetalle.Parameters.AddWithValue("@subtotal", item.Subtotal);
                            cmdDetalle.Parameters.AddWithValue("@descripcionManual", item.Nombre);
                            cmdDetalle.ExecuteNonQuery();
                        }
                    }

                    SumarPuntoClienteSeleccionado(conexion, transaccion, clienteId);
                    transaccion.Commit();
                    return (ventaId, folio);
                }
            }
        }

        private void CargarClientesFrecuentes()
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
                    conexion.Open();

                    const string query = "SELECT id, nombre FROM clientes ORDER BY nombre ASC;";
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    using (NpgsqlDataReader reader = cmd.ExecuteReader())
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

            clientesVenta = clientes;
        }

        private static void SumarPuntoClienteSeleccionado(NpgsqlConnection conexion, NpgsqlTransaction transaccion, int? clienteId)
        {
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

        private static void AsegurarColumnasArticulosComunes()
        {
            try
            {
                DatabaseConnection db = new DatabaseConnection();
                using (NpgsqlConnection conexion = db.GetConnection())
                {
                    conexion.Open();

                    const string query = @"
                        CREATE TABLE IF NOT EXISTS clientes (
                            id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                            nombre varchar(180) NOT NULL,
                            telefono varchar(40) NOT NULL DEFAULT '',
                            correo varchar(160) NOT NULL DEFAULT '',
                            notas text NOT NULL DEFAULT '',
                            puntos integer NOT NULL DEFAULT 0,
                            fecha_alta timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                        );

                        ALTER TABLE detalles_venta
                            ADD COLUMN IF NOT EXISTS descripcion_manual text NOT NULL DEFAULT '',
                            ADD COLUMN IF NOT EXISTS tipo_articulo varchar(30) NOT NULL DEFAULT 'Inventario';

                        ALTER TABLE detalles_venta
                            ALTER COLUMN cantidad TYPE numeric(12, 3) USING cantidad::numeric;

                        ALTER TABLE ventas
                            ADD COLUMN IF NOT EXISTS cliente_id integer NULL REFERENCES clientes(id) ON DELETE SET NULL,
                            ADD COLUMN IF NOT EXISTS efectivo_recibido numeric(12, 2) NOT NULL DEFAULT 0,
                            ADD COLUMN IF NOT EXISTS cambio_entregado numeric(12, 2) NOT NULL DEFAULT 0;";

                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conexion))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
            }
        }

        private static string FormatearCantidad(decimal cantidad)
        {
            return cantidad % 1 == 0 ? cantidad.ToString("0") : cantidad.ToString("0.###");
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
    }
}

using Npgsql;
using RefaccionariaPOS.Data;
using RefaccionariaPOS.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace RefaccionariaPOS.Views
{
    public partial class ImagenesProductoWindow : Window
    {
        private const string QueryImagenesProducto = @"
            SELECT imagen_url, imagen_data
            FROM producto_imagenes
            WHERE producto_id = @productoId
              AND (COALESCE(octet_length(imagen_data), 0) > 0 OR COALESCE(imagen_url, '') <> '')
            ORDER BY orden, id;";

        private const string QueryImagenLegadaProducto = @"
            SELECT COALESCE(imagen_url, '') AS imagen_url
            FROM productos
            WHERE id = @productoId
              AND COALESCE(imagen_url, '') <> '';";

        private readonly int productoId;
        private readonly List<ImagenProducto> imagenes = new();
        private int indiceActual;

        public ImagenesProductoWindow(int productoId, string nombreProducto)
        {
            InitializeComponent();
            this.productoId = productoId;
            lblNombreProducto.Text = nombreProducto;
            Title = $"Imagenes - {nombreProducto}";
            Loaded += async (_, _) => await CargarImagenesAsync();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    break;
                case Key.Left:
                    MostrarImagenAnterior();
                    break;
                case Key.Right:
                    MostrarImagenSiguiente();
                    break;
            }
        }

        private async Task CargarImagenesAsync()
        {
            try
            {
                List<ImagenProducto> resultado = await Task.Run(() => ObtenerImagenes(productoId));
                imagenes.Clear();
                imagenes.AddRange(resultado);
                indiceActual = 0;
                MostrarImagenActual();
            }
            catch (Exception ex)
            {
                lblEstado.Text = "No se pudieron cargar las imagenes: " + ex.Message;
                lblEstado.Visibility = Visibility.Visible;
            }
        }

        private static List<ImagenProducto> ObtenerImagenes(int productoId)
        {
            List<ImagenProducto> resultado = new();
            DatabaseConnection db = new DatabaseConnection();

            using NpgsqlConnection conexion = db.GetConnection();
            conexion.Open();

            using (NpgsqlCommand cmd = new NpgsqlCommand(QueryImagenesProducto, conexion))
            {
                cmd.Parameters.AddWithValue("@productoId", productoId);
                using NpgsqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    resultado.Add(new ImagenProducto(
                        reader["imagen_url"].ToString() ?? string.Empty,
                        reader["imagen_data"] is DBNull ? null : (byte[])reader["imagen_data"]));
                }
            }

            if (resultado.Count > 0)
            {
                return resultado;
            }

            using (NpgsqlCommand cmd = new NpgsqlCommand(QueryImagenLegadaProducto, conexion))
            {
                cmd.Parameters.AddWithValue("@productoId", productoId);
                object? imagenUrl = cmd.ExecuteScalar();
                if (imagenUrl is string url && !string.IsNullOrWhiteSpace(url))
                {
                    resultado.Add(new ImagenProducto(url, null));
                }
            }

            return resultado;
        }

        private void MostrarImagenActual()
        {
            imgProducto.Source = null;
            ActualizarNavegacion();

            if (imagenes.Count == 0)
            {
                lblEstado.Text = "Este producto no tiene imagenes registradas.";
                lblEstado.Visibility = Visibility.Visible;
                return;
            }

            ImagenProducto imagen = imagenes[indiceActual];
            BitmapImage? bitmap = ProductImageService.CargarImagen(imagen.Data, imagen.Url, 1200);
            if (bitmap == null)
            {
                lblEstado.Text = "No se pudo cargar esta imagen.";
                lblEstado.Visibility = Visibility.Visible;
                return;
            }

            imgProducto.Source = bitmap;
            lblEstado.Visibility = Visibility.Collapsed;
        }

        private void ActualizarNavegacion()
        {
            bool hayVarias = imagenes.Count > 1;
            btnAnterior.IsEnabled = hayVarias;
            btnSiguiente.IsEnabled = hayVarias;
            lblContador.Text = imagenes.Count == 0
                ? string.Empty
                : $"{indiceActual + 1} de {imagenes.Count}";
        }

        private void BtnAnterior_Click(object sender, RoutedEventArgs e)
        {
            MostrarImagenAnterior();
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            MostrarImagenSiguiente();
        }

        private void MostrarImagenAnterior()
        {
            if (imagenes.Count < 2)
            {
                return;
            }

            indiceActual = (indiceActual - 1 + imagenes.Count) % imagenes.Count;
            MostrarImagenActual();
        }

        private void MostrarImagenSiguiente()
        {
            if (imagenes.Count < 2)
            {
                return;
            }

            indiceActual = (indiceActual + 1) % imagenes.Count;
            MostrarImagenActual();
        }

        private sealed record ImagenProducto(string Url, byte[]? Data);
    }
}

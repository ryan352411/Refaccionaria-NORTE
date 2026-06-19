using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace RefaccionariaPOS.Services
{
    public static class ProductImageService
    {
        private static readonly string[] ExtensionesPermitidas = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

        public static string ImagenesFolder
        {
            get
            {
                string carpeta = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RefaxManager",
                    "ImagenesProductos");
                Directory.CreateDirectory(carpeta);
                return carpeta;
            }
        }

        public static string GuardarImagenLocal(string rutaOrigen, string codigoProducto)
        {
            if (string.IsNullOrWhiteSpace(rutaOrigen))
            {
                return string.Empty;
            }

            string? archivoOrigen = ResolverRutaLocal(rutaOrigen);
            if (archivoOrigen == null || !File.Exists(archivoOrigen))
            {
                return rutaOrigen.Trim();
            }

            string extension = Path.GetExtension(archivoOrigen);
            if (!ExtensionesPermitidas.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Selecciona una imagen JPG, PNG, BMP, GIF o WEBP.");
            }

            string carpetaImagenes = Path.GetFullPath(ImagenesFolder);
            string rutaOrigenCompleta = Path.GetFullPath(archivoOrigen);
            if (rutaOrigenCompleta.StartsWith(carpetaImagenes + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return rutaOrigenCompleta;
            }

            string codigoLimpio = LimpiarNombreArchivo(codigoProducto);
            string nombreArchivo = $"{codigoLimpio}_{DateTime.Now:yyyyMMddHHmmssfff}{extension.ToLowerInvariant()}";
            string destino = Path.Combine(ImagenesFolder, nombreArchivo);

            if (!archivoOrigen.Equals(destino, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(archivoOrigen, destino, overwrite: true);
            }

            return destino;
        }

        public static BitmapImage? CargarImagen(string ruta, int decodePixelWidth = 0)
        {
            if (string.IsNullOrWhiteSpace(ruta))
            {
                return null;
            }

            try
            {
                string? archivoLocal = ResolverRutaLocal(ruta);
                if (archivoLocal != null && File.Exists(archivoLocal))
                {
                    using FileStream stream = new FileStream(archivoLocal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    BitmapImage imagenLocal = new BitmapImage();
                    imagenLocal.BeginInit();
                    imagenLocal.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodePixelWidth > 0)
                    {
                        imagenLocal.DecodePixelWidth = decodePixelWidth;
                    }
                    imagenLocal.StreamSource = stream;
                    imagenLocal.EndInit();
                    imagenLocal.Freeze();
                    return imagenLocal;
                }

                if (Uri.TryCreate(ruta, UriKind.Absolute, out Uri? uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    BitmapImage imagenRemota = new BitmapImage();
                    imagenRemota.BeginInit();
                    imagenRemota.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodePixelWidth > 0)
                    {
                        imagenRemota.DecodePixelWidth = decodePixelWidth;
                    }
                    imagenRemota.UriSource = uri;
                    imagenRemota.EndInit();
                    imagenRemota.Freeze();
                    return imagenRemota;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        public static string? ResolverRutaLocal(string ruta)
        {
            string valor = ruta.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(valor))
            {
                return null;
            }

            if (Uri.TryCreate(valor, UriKind.Absolute, out Uri? uri))
            {
                if (uri.IsFile)
                {
                    return uri.LocalPath;
                }

                return null;
            }

            if (Path.IsPathRooted(valor))
            {
                return valor;
            }

            string enCarpetaImagenes = Path.Combine(ImagenesFolder, valor);
            if (File.Exists(enCarpetaImagenes))
            {
                return enCarpetaImagenes;
            }

            return Path.GetFullPath(valor);
        }

        private static string LimpiarNombreArchivo(string texto)
        {
            string valor = string.IsNullOrWhiteSpace(texto) ? "producto" : texto.Trim();
            foreach (char invalido in Path.GetInvalidFileNameChars())
            {
                valor = valor.Replace(invalido, '_');
            }

            return valor.Length > 48 ? valor.Substring(0, 48) : valor;
        }
    }
}

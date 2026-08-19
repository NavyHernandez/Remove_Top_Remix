using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;

namespace Remove_Top.Features.ImagePreview
{
    /// <summary>
    /// Soporte del previsualizador de imágenes: detección de archivos de imagen
    /// y creación de la fuente nativa de WinUI 3. No agrega librerías externas:
    /// usa WIC vía BitmapImage (raster) y SvgImageSource (SVG).
    /// </summary>
    public static class ImagePreviewSupport
    {
        /// <summary>Extensiones de imagen soportadas (mismo set que FileTypeIconConverter).</summary>
        private static readonly string[] ImageExtensions =
            [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".ico", ".jfif", ".svg"];

        /// <summary>Ancho de decodificado máximo para raster: limita la memoria aunque la imagen sea enorme.</summary>
        private const int DecodePixelWidth = 1600;

        /// <summary>Indica si un archivo es una imagen soportada por el previsualizador.</summary>
        public static bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext != null && Array.IndexOf(ImageExtensions, ext) >= 0;
        }

        /// <summary>
        /// Crea la fuente de imagen nativa para la ruta indicada:
        ///   - Raster (jpg/png/gif/bmp/tiff/webp/ico...): BitmapImage con
        ///     decodificado limitado (DecodePixelWidth) para ser ligero.
        ///   - SVG: SvgImageSource (BitmapImage no lo soporta).
        /// La fuente debe crearse en el hilo de la UI (es un DependencyObject).
        /// </summary>
        public static ImageSource CreateSource(string path)
        {
            var uri = new Uri(path);
            var ext = Path.GetExtension(path);

            if (string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase))
            {
                return new SvgImageSource(uri)
                {
                    RasterizePixelWidth = DecodePixelWidth,
                    RasterizePixelHeight = DecodePixelWidth
                };
            }

            var bitmap = new BitmapImage();
            bitmap.DecodePixelWidth = DecodePixelWidth;
            bitmap.UriSource = uri;
            return bitmap;
        }
    }
}
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace Remove_Top.Features.ImagePreview
{
    /// <summary>
    /// Visor simple de imágenes (raster o SVG) para el previsualizador.
    /// Usa las fuentes nativas de WinUI 3 (BitmapImage vía WIC para raster y
    /// SvgImageSource para SVG) sin librerías externas. La imagen se ajusta al
    /// espacio disponible (Stretch Uniform) y el decodificado se limita con
    /// DecodePixelWidth para mantener el consumo de memoria ligero.
    ///
    /// API pública:
    ///   - Load(path): carga una imagen mostrando los estados cargando/error.
    ///   - Clear(): libera la fuente (memoria y posible bloqueo del archivo).
    ///   - CurrentPath: ruta de la imagen cargada.
    ///   - ImageLoaded(int w, int h) / ImageLoadFailed: notificaciones para la UI.
    /// </summary>
    public sealed partial class ImagePreviewView : UserControl
    {
        private string _currentPath = "";

        /// <summary>Ruta de la imagen cargada actualmente (vacío si no hay ninguna).</summary>
        public string CurrentPath => _currentPath;

        /// <summary>
        /// Se dispara cuando la imagen se decodifica correctamente, con sus
        /// dimensiones en píxeles (0, 0 si no están disponibles, p. ej. SVG).
        /// </summary>
        public event Action<int, int>? ImageLoaded;

        /// <summary>Se dispara cuando la imagen no se puede abrir.</summary>
        public event Action? ImageLoadFailed;

        public ImagePreviewView()
        {
            InitializeComponent();
            EmptyState.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Carga una imagen desde su ruta. Muestra el estado de carga mientras
        /// decodifica y, al terminar, la imagen o el estado de error.
        /// </summary>
        public void Load(string path)
        {
            _currentPath = path;

            // Limpia la fuente anterior para liberar memoria/bloqueo.
            PreviewImage.Source = null;
            ErrorState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            LoadingState.Visibility = Visibility.Visible;

            var source = ImagePreviewSupport.CreateSource(path);
            AttachEvents(source);
            PreviewImage.Source = source;
        }

        /// <summary>Libera la fuente (memoria y posible bloqueo del archivo) y vuelve al estado vacío.</summary>
        public void Clear()
        {
            _currentPath = "";
            PreviewImage.Source = null;
            LoadingState.Visibility = Visibility.Collapsed;
            ErrorState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }

        /// <summary>Conecta los eventos de decodificación según el tipo de fuente.</summary>
        private void AttachEvents(ImageSource source)
        {
            if (source is BitmapImage bmp)
            {
                bmp.ImageOpened += Bitmap_ImageOpened;
                bmp.ImageFailed += Bitmap_ImageFailed;
            }
            else if (source is SvgImageSource svg)
            {
                svg.Opened += Svg_Opened;
                svg.OpenFailed += Svg_OpenFailed;
            }
        }

        private void Bitmap_ImageOpened(object sender, RoutedEventArgs e)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            var bmp = (BitmapImage)sender;
            ImageLoaded?.Invoke(bmp.PixelWidth, bmp.PixelHeight);
        }

        private void Bitmap_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            ErrorState.Visibility = Visibility.Visible;
            ImageLoadFailed?.Invoke();
        }

        private void Svg_Opened(SvgImageSource sender, SvgImageSourceOpenedEventArgs e)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            // Las dimensiones del SVG no se exponen fácilmente: se notifican 0, 0.
            ImageLoaded?.Invoke(0, 0);
        }

        private void Svg_OpenFailed(SvgImageSource sender, SvgImageSourceFailedEventArgs e)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            ErrorState.Visibility = Visibility.Visible;
            ImageLoadFailed?.Invoke();
        }
    }
}
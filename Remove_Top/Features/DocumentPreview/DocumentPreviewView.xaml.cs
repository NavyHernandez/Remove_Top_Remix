using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;

namespace Remove_Top.Features.DocumentPreview
{
    /// <summary>
    /// Visor de documentos para el previsualizador de Duplicados: texto plano
    /// o extraído de Office (.docx/.xlsx/.pptx) con scroll, y PDF renderizado
    /// con WebView2. Sin librerías externas.
    ///
    /// API pública (misma forma que ImagePreviewView):
    ///   - LoadAsync(path, maxBytes, maxLines): carga mostrando cargando/error.
    ///   - Clear(): libera el documento y vuelve al estado vacío.
    ///   - CurrentPath: ruta del documento cargado.
    ///   - DocumentLoaded(string resumen) / DocumentLoadFailed: notificaciones.
    /// </summary>
    public sealed partial class DocumentPreviewView : UserControl
    {
        private string _currentPath = "";

        /// <summary>Ruta del documento cargado actualmente (vacío si no hay ninguno).</summary>
        public string CurrentPath => _currentPath;

        /// <summary>
        /// Se dispara cuando el documento se muestra correctamente, con el
        /// resumen para el pie de la tarjeta ("1.240 líneas", "PDF"...).
        /// </summary>
        public event Action<string>? DocumentLoaded;

        /// <summary>Se dispara cuando el documento no se puede abrir.</summary>
        public event Action? DocumentLoadFailed;

        public DocumentPreviewView()
        {
            InitializeComponent();
            PdfViewer.NavigationCompleted += PdfViewer_NavigationCompleted;
            EmptyState.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Carga un documento desde su ruta. Muestra el estado de carga
        /// mientras lee/extrae y, al terminar, el texto o el PDF (o el error).
        /// Si se pide otro archivo antes de terminar, el anterior se ignora.
        /// </summary>
        public async Task LoadAsync(string path, int maxBytes, int maxLines)
        {
            _currentPath = path;
            TextScroll.Visibility = Visibility.Collapsed;
            HidePdf();
            ErrorState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            LoadingState.Visibility = Visibility.Visible;

            try
            {
                var kind = DocumentPreviewSupport.Classify(path);
                if (kind == DocumentKind.Pdf)
                {
                    await LoadPdfAsync(path);
                    return;
                }

                var result = await DocumentPreviewSupport.ExtractTextAsync(path, kind, maxBytes, maxLines);
                if (!IsCurrent(path)) return;
                TextContent.Text = string.IsNullOrWhiteSpace(result.Text)
                    ? "(Sin texto para mostrar)"
                    : result.Text;
                LoadingState.Visibility = Visibility.Collapsed;
                TextScroll.Visibility = Visibility.Visible;
                string summary = result.Truncated
                    ? $"primeras {result.LinesShown:N0} líneas"
                    : $"{result.LinesShown:N0} líneas";
                DocumentLoaded?.Invoke(summary);
            }
            // Filtro amplio a propósito: un preview nunca debe tumbar la
            // página (Office corrupto, PDF roto, WebView2 sin inicializar...).
            catch (Exception ex)
            {
                if (!IsCurrent(path)) return;
                ShowError(ex is InvalidOperationException ? ex.Message : "No se pudo abrir el documento.");
            }
        }

        /// <summary>Libera el documento (texto y PDF) y vuelve al estado vacío.</summary>
        public void Clear()
        {
            _currentPath = "";
            TextContent.Text = "";
            TextScroll.Visibility = Visibility.Collapsed;
            HidePdf();
            LoadingState.Visibility = Visibility.Collapsed;
            ErrorState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }

        /// <summary>Muestra el PDF con WebView2 (requiere el Runtime instalado).</summary>
        private async Task LoadPdfAsync(string path)
        {
            if (!DocumentPreviewSupport.IsPdfViewerAvailable())
                throw new InvalidOperationException("El visor de PDF no está disponible en este equipo.");
            await PdfViewer.EnsureCoreWebView2Async();
            if (!IsCurrent(path)) return;
            PdfViewer.Source = new Uri(path);
            LoadingState.Visibility = Visibility.Collapsed;
            PdfViewer.Visibility = Visibility.Visible;
            DocumentLoaded?.Invoke(DocumentPreviewSupport.DescribeKind(DocumentKind.Pdf));
        }

        /// <summary>Si la navegación del PDF falla, muestra el error.</summary>
        private void PdfViewer_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess && !string.IsNullOrEmpty(_currentPath) && PdfViewer.Visibility == Visibility.Visible)
                ShowError("No se pudo mostrar el PDF.");
        }

        private void ShowError(string message)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            TextScroll.Visibility = Visibility.Collapsed;
            HidePdf();
            ErrorText.Text = message;
            ErrorState.Visibility = Visibility.Visible;
            DocumentLoadFailed?.Invoke();
        }

        /// <summary>Oculta el visor PDF liberando el documento (about:blank).</summary>
        private void HidePdf()
        {
            try
            {
                if (PdfViewer.CoreWebView2 != null)
                    PdfViewer.Source = new Uri("about:blank");
            }
            catch { /* Visor aún no inicializado: nada que liberar. */ }
            PdfViewer.Visibility = Visibility.Collapsed;
        }

        private bool IsCurrent(string path)
            => string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase);
    }
}

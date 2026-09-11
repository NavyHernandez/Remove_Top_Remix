using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remove_Top.Controls;
using Remove_Top.Features.ImagePreview;
using Remove_Top.Helpers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace Remove_Top.Features.TagRemoval
{
    /// <summary>
    /// Página de Eliminar/Reemplazar Etiquetas. Las dos acciones viven en
    /// pestañas en la parte superior: cada una tiene su propia selección de
    /// origen + análisis (TagSourceControl). Se puede arrastrar contenido a
    /// toda la página (se muestra un overlay oscuro al arrastrar) y el
    /// procesamiento comparte progreso, resultados y tarjeta premium.
    /// </summary>
    public sealed partial class TagRemovalPage : Page
    {
        private static readonly string[] ImagePickerExtensions =
            [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".ico"];

        private readonly ObservableCollection<TagResult> _results = [];
        private CancellationTokenSource? _cts;
        private TagSourceControl _activeSource = null!;
        private bool _isProcessing;
        private string? _coverPath;

        public TagRemovalPage()
        {
            InitializeComponent();
            ResultsListView.ItemsSource = _results;
            ClearButton.Content = UiHelpers.Content(Icon.Tag, "Eliminar etiquetas", foreground: ClearButton.Foreground);
            ReplaceButton.Content = UiHelpers.Content(Icon.Edit, "Aplicar reemplazo", foreground: ReplaceButton.Foreground);

            // Título y descripción del encabezado, centralizados en AppLimits.
            PageTitleText.Text = AppLimits.TagsPageTitle;
            PageSubtitleText.Text = AppLimits.TagsPageSubtitle;
            BrandText.Text = AppLimits.AppName;

            // Límite de la versión gratuita (badge + texto compacto, desde AppLimits).
            LimitInfoTitle.Text = AppLimits.TagsInfoBarTitle;

            // Textos de las dos pestañas de acción (generados desde AppLimits).
            ClearInfoText.Text = AppLimits.TagsClearInfoText;
            ReplaceInfoText.Text = AppLimits.TagsReplaceInfoText;

            // Pestaña activa por defecto.
            _activeSource = ClearSource;
            UpdateUI();
        }

        // ================================================================
        // PESTAÑAS
        // ================================================================

        private void ActionsTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _activeSource = ActionsTabView.SelectedItem as TabViewItem == ReplaceTab ? ReplaceSource : ClearSource;
            UpdateUI();
        }

        /// <summary>Al cambiar el estado de un origen (carga/análisis/reset) se refrescan los botones.</summary>
        private void Source_StateChanged(object? sender, EventArgs e) => UpdateUI();

        // ================================================================
        // ARRASTRE (delegado a DropTargetControl reutilizable)
        // ================================================================

        /// <summary>
        /// Al soltar contenido sobre la página (toda la página es el destino
        /// de arrastre): las carpetas se escanean de forma recursiva y los
        /// archivos se usan tal cual, cargándose en la pestaña activa. Si la
        /// app está procesando, el arrastre se ignora.
        /// </summary>
        private void DropTarget_FilesDropped(object? sender, DropFilesEventArgs e)
        {
            if (_isProcessing) return;
            _activeSource.LoadSource(e.Paths);
        }

        /// <summary>
        /// Si el arrastre no se pudo leer (fallo Win10), se muestra el motivo
        /// en la línea de estado del origen activo en vez de quedarse en silencio.
        /// </summary>
        private void DropTarget_DropFailed(object? sender, DropFailedEventArgs e)
        {
            if (_isProcessing) return;
            _activeSource.SetStatus(e.Reason);
        }

        // ================================================================
        // ACCIÓN 1: ELIMINAR ETIQUETAS
        // ================================================================

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                return;
            }
            if (_activeSource.Files.Count == 0) return;

            if (!await ConfirmClearAsync()) return;
            await RunAsync(TagMode.Clear, new TagValues());
        }

        private async Task<bool> ConfirmClearAsync()
        {
            var count = _activeSource.Files.Count;
            var dialog = new ContentDialog
            {
                Title = "Eliminar etiquetas",
                Content = $"Se eliminarán TODAS las etiquetas de {count} archivo(s), " +
                          "incluida la imagen de portada.\n\nEsta acción no se puede deshacer.",
                PrimaryButtonText = "Eliminar etiquetas",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        // ================================================================
        // ACCIÓN 2: REEMPLAZAR ETIQUETAS
        // ================================================================

        private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                return;
            }
            if (_activeSource.Files.Count == 0) return;

            var values = new TagValues
            {
                Title = TitleBox.Text.Trim(),
                Artist = ArtistBox.Text.Trim(),
                AlbumArtist = AlbumArtistBox.Text.Trim(),
                Album = AlbumBox.Text.Trim(),
                Genre = GenreBox.Text.Trim(),
                Year = YearBox.Text.Trim(),
                Comment = CommentBox.Text.Trim(),
                CoverPath = _coverPath
            };

            bool anyField = !string.IsNullOrEmpty(values.Title) ||
                            !string.IsNullOrEmpty(values.Artist) ||
                            !string.IsNullOrEmpty(values.AlbumArtist) ||
                            !string.IsNullOrEmpty(values.Album) ||
                            !string.IsNullOrEmpty(values.Genre) ||
                            !string.IsNullOrEmpty(values.Year) ||
                            !string.IsNullOrEmpty(values.Comment) ||
                            _coverPath != null;
            if (!anyField)
            {
                _activeSource.SetStatus("Rellena al menos un campo o elige una portada.");
                return;
            }

            if (!await ConfirmReplaceAsync(values)) return;
            await RunAsync(TagMode.Replace, values);
        }

        private async Task<bool> ConfirmReplaceAsync(TagValues v)
        {
            var count = _activeSource.Files.Count;
            var dialog = new ContentDialog
            {
                Title = "Reemplazar etiquetas",
                Content = $"Se reemplazarán las etiquetas de {count} archivo(s) con:\n\n" +
                          $"Título: {OrDash(v.Title)}\n" +
                          $"Intérprete: {OrDash(v.Artist)}\n" +
                          $"Artistas: {OrDash(v.AlbumArtist)}\n" +
                          $"Álbum: {OrDash(v.Album)}\n" +
                          $"Género: {OrDash(v.Genre)}\n" +
                          $"Año: {OrDash(v.Year)}\n" +
                          $"Comentario: {OrDash(v.Comment)}\n" +
                          $"Portada: {(v.CoverPath != null ? Path.GetFileName(v.CoverPath) : "Se conserva la actual")}\n\n" +
                          "Los campos vacíos se limpian. Esta acción no se puede deshacer.",
                PrimaryButtonText = "Aplicar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private static string OrDash(string value) =>
            string.IsNullOrEmpty(value) ? "—" : value;

        // ================================================================
        // GESTIÓN DE LA PORTADA (pestaña Reemplazar)
        // ================================================================

        private async void CoverBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var ext in ImagePickerExtensions)
                picker.FileTypeFilter.Add(ext);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            _coverPath = file.Path;
            CoverPathBox.Text = file.Path;
            CoverPreview.Source = ImagePreviewSupport.CreateSource(file.Path);
            CoverClearButton.Visibility = Visibility.Visible;
        }

        private void CoverClearButton_Click(object sender, RoutedEventArgs e)
        {
            _coverPath = null;
            CoverPathBox.Text = "";
            CoverPreview.Source = null;
            CoverClearButton.Visibility = Visibility.Collapsed;
        }

        // ================================================================
        // PROCESAMIENTO
        // ================================================================

        /// <summary>
        /// Ejecuta el lote de la pestaña activa (borrado total o reemplazo de
        /// tags) mostrando progreso, resultados y, si el escaneo se truncó, la
        /// tarjeta premium.
        /// </summary>
        private async Task RunAsync(TagMode mode, TagValues values)
        {
            var files = _activeSource.Files;
            if (files.Count == 0) return;

            _results.Clear();
            _isProcessing = true;
            SetBusy(mode);
            _activeSource.SetEnabled(false);

            ResultsSection.Visibility = Visibility.Visible;
            ProgressSection.Visibility = Visibility.Visible;
            OverallProgressBar.Value = 0;
            CompleteBadge.Visibility = Visibility.Collapsed;
            PremiumSection.Visibility = Visibility.Collapsed;

            _cts = new CancellationTokenSource();
            var progress = new Progress<TagProgress>(p =>
            {
                OverallProgressBar.Value = p.Percentage;
                ProgressCountText.Text = $"{p.CurrentIndex}/{p.TotalCount}";
                ProgressText.Text = p.Result == null
                    ? $"Procesando: {p.CurrentFile}"
                    : p.CurrentFile;

                if (p.Result != null)
                {
                    _results.Add(p.Result);
                    ResultsListView.ScrollIntoView(p.Result);
                    UpdateSummary();
                }
            });

            try
            {
                await new TagService().ProcessAsync(mode, files, values, progress, _cts.Token);

                var ok = _results.Count(r => r.Success);
                var fail = _results.Count(r => !r.Success);
                CompleteText.Text = fail == 0 ? "\u2713 Completado" : "\u2713 Completado con errores";
                CompleteBadge.Visibility = Visibility.Visible;
                OverallProgressBar.Value = 100;
                ProgressText.Text = "Proceso finalizado";
                RestartButton.Visibility = Visibility.Visible;

                if (_activeSource.Truncated)
                {
                    PremiumMessageText.Text = $"Se analizaron solo los primeros {_activeSource.ScannedFiles} " +
                        $"de {_activeSource.TotalFound} archivos encontrados. Con la versión premium podrás " +
                        "procesar carpetas completas sin límites.";
                    PremiumSection.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException)
            {
                _results.Add(new TagResult { FileName = "---", Success = false, Message = "Proceso cancelado" });
                UpdateSummary();
            }
            catch (Exception ex)
            {
                _results.Add(new TagResult { FileName = "ERROR", Success = false, Message = $"Error general: {ex.Message}" });
                UpdateSummary();
            }
            finally
            {
                _isProcessing = false;
                SetIdle();
                _activeSource.SetEnabled(true);
                _cts?.Dispose();
                _cts = null;
                UpdateUI();
            }
        }

        /// <summary>
        /// Durante el procesamiento el botón activo pasa a "Cancelar" y el resto
        /// de controles se deshabilitan.
        /// </summary>
        private void SetBusy(TagMode mode)
        {
            ClearButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: ClearButton.Foreground);
            ReplaceButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: ReplaceButton.Foreground);
            ClearButton.IsEnabled = mode == TagMode.Clear;
            ReplaceButton.IsEnabled = mode == TagMode.Replace;
            CoverBrowseButton.IsEnabled = false;
            CoverClearButton.IsEnabled = false;
        }

        private void SetIdle()
        {
            ClearButton.Content = UiHelpers.Content(Icon.Tag, "Eliminar etiquetas", foreground: ClearButton.Foreground);
            ReplaceButton.Content = UiHelpers.Content(Icon.Edit, "Aplicar reemplazo", foreground: ReplaceButton.Foreground);
        }

        private void UpdateSummary()
        {
            int ok = _results.Count(r => r.Success);
            int fail = _results.Count(r => !r.Success);
            SummaryText.Text = $"{ok} correctos \u00b7 {fail} errores \u00b7 {_results.Count} total";
        }

        // ================================================================
        // LIMPIAR / PREMIUM / ESTADO
        // ================================================================

        /// <summary>"Limpiar" tras la ejecución: vuelve la página a su estado inicial.</summary>
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            ResetAll();
        }

        /// <summary>Reinicia ambas pestañas, los resultados y la portada.</summary>
        private void ResetAll()
        {
            ClearSource.Reset();
            ReplaceSource.Reset();
            _results.Clear();
            CoverPathBox.Text = "";
            CoverPreview.Source = null;
            _coverPath = null;
            CoverClearButton.Visibility = Visibility.Collapsed;
            TitleBox.Text = "";
            ArtistBox.Text = "";
            AlbumArtistBox.Text = "";
            AlbumBox.Text = "";
            GenreBox.Text = "";
            YearBox.Text = "";
            CommentBox.Text = "";
            ProgressSection.Visibility = Visibility.Collapsed;
            ResultsSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            PremiumSection.Visibility = Visibility.Collapsed;
            UpdateUI();
        }

        /// <summary>
        /// Abre en el navegador la URL de la versión premium
        /// (<see cref="PremiumLinks.UpgradeUrl"/>).
        /// </summary>
        private async void UpgradeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var uri = new Uri(PremiumLinks.UpgradeUrl);
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            catch (Exception ex)
            {
                _activeSource.SetStatus($"No se pudo abrir el enlace premium: {ex.Message}");
            }
        }

        /// <summary>Habilita cada botón según el estado de su propio origen.</summary>
        private void UpdateUI()
        {
            ClearButton.IsEnabled = ClearSource.HasFiles && !ClearSource.IsBusy && !_isProcessing;
            ReplaceButton.IsEnabled = ReplaceSource.HasFiles && !ReplaceSource.IsBusy && !_isProcessing;

            // El menú de inputs de Reemplazar solo se muestra con archivos cargados.
            ReplaceInputsSection.Visibility = (ReplaceSource.HasFiles && !ReplaceSource.IsBusy && !_isProcessing)
                ? Visibility.Visible
                : Visibility.Collapsed;

            CoverBrowseButton.IsEnabled = !_isProcessing;
            CoverClearButton.IsEnabled = _coverPath != null && !_isProcessing;
        }

        /// <summary>Al salir de la página se cancela cualquier operación en curso.</summary>
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }
    }
}
using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remove_Top.Controls;
using Remove_Top.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>
    /// Página de Edición Rápida: selecciona una carpeta o archivos .mp3/.wav
    /// (carpeta, botones o arrastre) y permite editar cada nombre en una caja
    /// de texto inline (con extensión). Aplica los cambios con File.Move
    /// directamente sobre los originales.
    /// </summary>
    public sealed partial class QuickRenamePage : Page
    {
        private readonly ObservableCollection<QuickRenameItem> _items = [];
        private CancellationTokenSource? _cts;
        private bool _isProcessing;

        public QuickRenamePage()
        {
            InitializeComponent();
            FilesListView.ItemsSource = _items;
            ResetButton.Content = UiHelpers.Content(Icon.ArrowUndo, "Restaurar originales", semibold: false, foreground: ResetButton.Foreground);
            StartButton.Content = UiHelpers.Content(Icon.Checkmark, "Aplicar cambios", foreground: StartButton.Foreground);
            RestartButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: RestartButton.Foreground);
            FreeBadgeText.Text = AppLimits.FreeBadgeText;
            LimitInfoText.Text = AppLimits.QuickRenameLimitMessage;

            // Configuración del control de origen: solo .mp3/.wav, sin subcarpetas.
            Source.FileFilter = QuickRenamer.IsSupportedFile;
            Source.PickerExtensions = [".mp3", ".wav"];

            // Título y subtítulo del encabezado, centralizados en AppLimits.
            PageTitleText.Text = AppLimits.QuickRenamePageTitle;
            PageSubtitleText.Text = AppLimits.QuickRenamePageSubtitle;
            BrandText.Text = AppLimits.AppName;
            SiteBrandText.Text = AppLimits.AppBrandSite;
        }

        // ================================================================
        // ARRASTRE / CARGA DE ORIGEN
        // ================================================================

        /// <summary>
        /// Al soltar contenido sobre la página (toda la página es destino de
        /// arrastre) se carga en el control de origen, que escanea carpetas o
        /// usa archivos sueltos. Si la app está procesando, se ignora.
        /// </summary>
        private void DropTarget_FilesDropped(object? sender, DropFilesEventArgs e)
        {
            if (_isProcessing) return;
            Source.LoadSource(e.Paths);
        }

        /// <summary>
        /// Al cambiar el estado del origen (carga/reset) se reconstruye la lista
        /// editable a partir de los archivos seleccionados.
        /// </summary>
        private void Source_StateChanged(object? sender, EventArgs e)
        {
            if (_isProcessing) return;

            ResultSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;

            if (Source.HasFiles)
                PopulateItems();
            else
                ClearItems();

            UpdateUI();
        }

        /// <summary>Reconstruye la lista editable a partir de los archivos cargados.</summary>
        private void PopulateItems()
        {
            foreach (var item in _items)
                item.PropertyChanged -= Item_PropertyChanged;

            _items.Clear();

            foreach (var file in Source.Files)
            {
                var name = Path.GetFileName(file);
                var item = new QuickRenameItem
                {
                    OriginalPath = file,
                    OriginalName = name,
                    CurrentName = name
                };
                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            FileCountText.Text = $"{_items.Count} archivo(s) .mp3/.wav encontrado(s)";
            FileCountText.Visibility = Visibility.Visible;
            ListSection.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Limpia la lista editable (cuando el origen queda sin archivos).</summary>
        private void ClearItems()
        {
            foreach (var item in _items)
                item.PropertyChanged -= Item_PropertyChanged;
            _items.Clear();
            FileCountText.Visibility = Visibility.Collapsed;
            ListSection.Visibility = Visibility.Collapsed;
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(QuickRenameItem.IsDirty))
                UpdateUI();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.CurrentName = item.OriginalName;
            UpdateUI();
        }

        private void UpdateUI()
        {
            int dirty = _items.Count(i => i.IsDirty);
            DirtyCountText.Text = dirty > 0 ? $"{dirty} modificado(s)" : "Sin cambios";
            ResetButton.IsEnabled = dirty > 0;
            StartButton.IsEnabled = _items.Count > 0 && dirty > 0 && !_isProcessing;
            StartButton.Content = UiHelpers.Content(Icon.Checkmark,
                dirty > 0 ? $"Aplicar cambios ({dirty})" : "Aplicar cambios",
                foreground: StartButton.Foreground);
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                return;
            }

            if (_items.Count == 0) return;

            _isProcessing = true;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            Source.SetEnabled(false);
            ResetButton.IsEnabled = false;

            _cts = new CancellationTokenSource();
            var renamer = new QuickRenamer();
            var pending = _items.Count(i => i.IsDirty);

            try
            {
                var changed = await renamer.ApplyRenamesAsync(_items, _cts.Token);

                CompleteText.Text = "\u2713 Completado";
                CompleteBadge.Visibility = Visibility.Visible;
                ResultSummaryText.Text = $"Se cambiaron {changed} de {pending} archivo(s).";
                ResultSection.Visibility = Visibility.Visible;
                RestartButton.Visibility = Visibility.Visible;

                PopulateItems();
            }
            catch (OperationCanceledException)
            {
                CompleteText.Text = "\u2713 Cancelado";
                CompleteBadge.Visibility = Visibility.Visible;
                ResultSummaryText.Text = "Proceso cancelado por el usuario.";
                ResultSection.Visibility = Visibility.Visible;
                RestartButton.Visibility = Visibility.Visible;
            }
            finally
            {
                _isProcessing = false;
                Source.SetEnabled(true);
                _cts?.Dispose();
                _cts = null;
                UpdateUI();
            }
        }

        /// <summary>
        /// "Limpiar": vuelve la página a su estado inicial tras el renombrado.
        /// Limpia el origen, la lista editable y el resultado.
        /// </summary>
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            Source.Reset();
            ClearItems();

            ResultSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;

            UpdateUI();
        }
    }
}
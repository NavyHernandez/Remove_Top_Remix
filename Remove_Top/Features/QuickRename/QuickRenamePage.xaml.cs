using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remove_Top.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>
    /// Página de Edición Rápida: lista los .mp3/.wav de la carpeta principal y
    /// permite editar cada nombre en una caja de texto inline (con extensión).
    /// Aplica los cambios con File.Move directamente sobre los originales.
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
            BrowseButton.Content = UiHelpers.Content(Icon.FolderOpen, "Examinar...", foreground: BrowseButton.Foreground);
            ResetButton.Content = UiHelpers.Content(Icon.ArrowUndo, "Restaurar originales", semibold: false, foreground: ResetButton.Foreground);
            StartButton.Content = UiHelpers.Content(Icon.Checkmark, "Aplicar cambios", foreground: StartButton.Foreground);
            RestartButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: RestartButton.Foreground);
            FreeBadgeText.Text = AppLimits.FreeBadgeText;
            LimitInfoText.Text = AppLimits.QuickRenameLimitMessage;

            // Título y subtítulo del encabezado, centralizados en AppLimits.
            PageTitleText.Text = AppLimits.QuickRenamePageTitle;
            PageSubtitleText.Text = AppLimits.QuickRenamePageSubtitle;
            BrandText.Text = AppLimits.AppName;
            SiteBrandText.Text = AppLimits.AppBrandSite;
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                FolderPathBox.Text = folder.Path;
                LoadFiles(folder.Path);
            }
        }

        private void LoadFiles(string folderPath)
        {
            ResultSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            PopulateItems(folderPath);
            UpdateUI();
        }

        /// <summary>
        /// Reconstruye la lista editable de archivos (primeros
        /// <see cref="AppLimits.QuickRenameMaxFilesToScan"/> de la carpeta)
        /// sin tocar las secciones de progreso/resultados.
        /// </summary>
        private void PopulateItems(string folderPath)
        {
            foreach (var item in _items)
                item.PropertyChanged -= Item_PropertyChanged;

            _items.Clear();

            var files = QuickRenamer.GetAudioFiles(folderPath, AppLimits.QuickRenameMaxFilesToScan);
            foreach (var f in files)
            {
                var name = Path.GetFileName(f);
                var item = new QuickRenameItem
                {
                    OriginalPath = f,
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

            var folderPath = FolderPathBox.Text;
            if (string.IsNullOrEmpty(folderPath) || _items.Count == 0) return;

            _isProcessing = true;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            BrowseButton.IsEnabled = false;
            FolderPathBox.IsEnabled = false;
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

                PopulateItems(folderPath);
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
                BrowseButton.IsEnabled = true;
                FolderPathBox.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
                UpdateUI();
            }
        }

        /// <summary>
        /// "Limpiar": vuelve la página a su estado inicial tras el
        /// renombrado. Limpia la ruta, la lista editable y el resultado.
        /// </summary>
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            FolderPathBox.Text = "";
            _items.Clear();

            ListSection.Visibility = Visibility.Collapsed;
            ResultSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            FileCountText.Visibility = Visibility.Collapsed;

            UpdateUI();
        }
    }
}

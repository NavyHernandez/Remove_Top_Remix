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
        private readonly ObservableCollection<QuickRenameResult> _results = [];
        private CancellationTokenSource? _cts;
        private bool _isProcessing;

        public QuickRenamePage()
        {
            InitializeComponent();
            FilesListView.ItemsSource = _items;
            ResultsListView.ItemsSource = _results;
            BrowseButton.Content = UiHelpers.Content(Icon.FolderOpen, "Examinar...", foreground: BrowseButton.Foreground);
            ResetButton.Content = UiHelpers.Content(Icon.ArrowUndo, "Restaurar originales", semibold: false, foreground: ResetButton.Foreground);
            StartButton.Content = UiHelpers.Content(Icon.Checkmark, "Aplicar cambios", foreground: StartButton.Foreground);

            // Título y subtítulo del encabezado, centralizados en AppLimits.
            PageTitleText.Text = AppLimits.QuickRenamePageTitle;
            PageSubtitleText.Text = AppLimits.QuickRenamePageSubtitle;
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
            foreach (var item in _items)
                item.PropertyChanged -= Item_PropertyChanged;

            _items.Clear();
            _results.Clear();
            ResultsSection.Visibility = Visibility.Collapsed;
            ProgressSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;

            var files = QuickRenamer.GetAudioFiles(folderPath);
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
            UpdateUI();
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

            _results.Clear();
            _isProcessing = true;
            CompleteBadge.Visibility = Visibility.Collapsed;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            BrowseButton.IsEnabled = false;
            FolderPathBox.IsEnabled = false;
            ResetButton.IsEnabled = false;
            ProgressSection.Visibility = Visibility.Visible;
            ResultsSection.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;

            _cts = new CancellationTokenSource();
            var renamer = new QuickRenamer();
            var progress = new Progress<QuickRenameProgress>(p =>
            {
                ProgressBar.Value = p.Percentage;
                ProgressCountText.Text = $"{p.CurrentIndex}/{p.TotalCount}";
                ProgressText.Text = p.CurrentFile;

                if (p.Result != null)
                {
                    _results.Add(p.Result);
                    ResultsListView.ScrollIntoView(p.Result);
                    UpdateSummary();
                }
            });

            try
            {
                await renamer.ApplyRenamesAsync(_items, progress, _cts.Token);

                var ok = _results.Count(r => r.Success);
                var fail = _results.Count(r => !r.Success);
                CompleteText.Text = fail == 0 ? "\u2713 Completado" : "\u2713 Completado con errores";
                CompleteBadge.Visibility = Visibility.Visible;
                ProgressBar.Value = 100;
                ProgressText.Text = "Proceso finalizado";

                LoadFiles(folderPath);
            }
            catch (OperationCanceledException)
            {
                _results.Add(new QuickRenameResult
                {
                    OriginalName = "---",
                    Success = false,
                    Message = "Proceso cancelado por el usuario"
                });
                UpdateSummary();
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

        private void UpdateSummary()
        {
            int ok = _results.Count(r => r.Success);
            int fail = _results.Count(r => !r.Success);
            SummaryText.Text = $"{ok} correctos \u00b7 {fail} errores \u00b7 {_results.Count} total";
        }
    }
}

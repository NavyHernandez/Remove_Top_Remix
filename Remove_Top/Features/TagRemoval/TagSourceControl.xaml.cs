using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Remove_Top.Features.TagRemoval
{
    /// <summary>
    /// Control reutilizable de selección de origen + análisis de etiquetas.
    /// Encapsula la tarjeta "Origen" (carpeta, archivos, pista de arrastre y
    /// botón "Limpiar") y la tarjeta "Análisis" (contador, progreso, lista con
    /// las tags actuales y estado). Se usa una instancia por pestaña en
    /// TagRemovalPage para que cada modo tenga su propia selección.
    /// </summary>
    public sealed partial class TagSourceControl : UserControl
    {
        private static readonly string[] PickerExtensions =
            [".mp3", ".flac", ".m4a", ".mp4", ".wma", ".asf", ".ogg", ".oga",
             ".opus", ".aiff", ".aif", ".wav", ".ape", ".wv", ".mka", ".tta", ".dsf"];

        private List<string> _files = [];
        private readonly ObservableCollection<TagFileItem> _items = [];
        private CancellationTokenSource? _cts;
        private bool _isBusy;
        private bool _enabled = true;
        private bool _truncated;
        private int _scannedFiles;
        private int _totalFound;
        private int _generation;
        private Brush _sourceCardBackground = null!;
        private Brush _sourceCardBorder = null!;

        /// <summary>Se dispara cuando cambia el estado (archivos cargados, análisis, reset).</summary>
        public event EventHandler? StateChanged;

        /// <summary>Archivos analizados (hasta <see cref="AppLimits.TagsMaxFilesToScan"/>).</summary>
        public IReadOnlyList<string> Files => _files;

        /// <summary>Ítems del análisis (lista con las tags actuales).</summary>
        public ObservableCollection<TagFileItem> Items => _items;

        /// <summary>Indica si hay archivos cargados.</summary>
        public bool HasFiles => _files.Count > 0;

        /// <summary>Indica si el análisis está en curso.</summary>
        public bool IsBusy => _isBusy;

        /// <summary>Indica si el escaneo se truncó (más archivos del límite).</summary>
        public bool Truncated => _truncated;

        /// <summary>Archivos analizados en el último escaneo.</summary>
        public int ScannedFiles => _scannedFiles;

        /// <summary>Archivos de audio encontrados (antes de aplicar el límite).</summary>
        public int TotalFound => _totalFound;

        public TagSourceControl()
        {
            InitializeComponent();
            AnalysisListView.ItemsSource = _items;
            ClearLoadedButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: ClearLoadedButton.Foreground);
            _sourceCardBackground = SourceCard.Background;
            _sourceCardBorder = SourceCard.BorderBrush;
            UpdateState();
        }

        // ================================================================
        // SELECCIÓN DE ORIGEN
        // ================================================================

        private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
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
            if (folder != null) LoadSource([folder.Path]);
        }

        private async void BrowseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var ext in PickerExtensions)
                picker.FileTypeFilter.Add(ext);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
                LoadSource(files.Select(f => f.Path));
        }

        /// <summary>"Limpiar": resetea esta carga (si el usuario no quiere ejecutar).</summary>
        private void ClearLoadedButton_Click(object sender, RoutedEventArgs e) => Reset();

        /// <summary>
        /// Carga rutas (carpetas → recursivo, archivos → tal cual), analiza las
        /// tags actuales y notifica el cambio de estado.
        /// </summary>
        public void LoadSource(IEnumerable<string> paths)
        {
            var list = paths.ToArray();
            if (list.Length == 0) return;

            Reset();
            var generation = _generation;

            var scan = TagService.CollectFiles(list);
            _files = scan.Files;
            _truncated = scan.Truncated;
            _scannedFiles = scan.Files.Count;
            _totalFound = scan.TotalFound;

            SourcePathBox.Text = list.Length == 1
                ? list[0]
                : $"{list.Length} elemento(s) seleccionado(s)";

            if (_files.Count == 0)
            {
                AnalysisSection.Visibility = Visibility.Visible;
                AnalysisCountText.Text = "0 archivos";
                AnalysisStatusText.Text = "No se encontraron archivos de audio compatibles.";
                UpdateState();
                return;
            }

            _ = RunAnalysisAsync(generation);
        }

        // ================================================================
        // ANÁLISIS
        // ================================================================

        /// <summary>
        /// Lee las tags actuales de cada archivo en segundo plano y puebla la
        /// lista del análisis. La generación evita que un análisis viejo
        /// sobrescriba el estado de uno más reciente.
        /// </summary>
        private async Task RunAnalysisAsync(int generation)
        {
            _isBusy = true;
            UpdateState();
            AnalysisSection.Visibility = Visibility.Visible;
            AnalysisProgressBar.Visibility = Visibility.Visible;
            AnalysisStatusText.Text = "Leyendo etiquetas...";
            _items.Clear();

            _cts = new CancellationTokenSource();
            try
            {
                var items = await new TagService().AnalyzeAsync(_files, cancellationToken: _cts.Token);
                if (generation != _generation) return;

                foreach (var item in items) _items.Add(item);

                int covers = _items.Count(i => i.HasCover);
                AnalysisCountText.Text = $"{_items.Count} archivo(s) \u00b7 {covers} con portada";
                string truncated = _truncated
                    ? $" (se analizaron los primeros {_scannedFiles} de {_totalFound})"
                    : "";
                AnalysisStatusText.Text = $"Listo para procesar.{truncated}";
            }
            catch (OperationCanceledException)
            {
                if (generation == _generation) Reset();
            }
            catch (Exception ex)
            {
                if (generation != _generation) return;
                AnalysisCountText.Text = "0 archivos";
                AnalysisStatusText.Text = $"Error al analizar: {ex.Message}";
            }
            finally
            {
                if (generation == _generation)
                {
                    _isBusy = false;
                    AnalysisProgressBar.Visibility = Visibility.Collapsed;
                    _cts?.Dispose();
                    _cts = null;
                    UpdateState();
                }
            }
        }

        /// <summary>Muestra un mensaje en la línea de estado del análisis.</summary>
        public void SetStatus(string message)
        {
            AnalysisStatusText.Text = message;
            if (!string.IsNullOrEmpty(message))
                AnalysisSection.Visibility = Visibility.Visible;
        }

        // ================================================================
        // ESTADO
        // ================================================================

        /// <summary>Reinicia el control: cancela el análisis y limpia ruta, lista y estado.</summary>
        public void Reset()
        {
            _generation++;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _isBusy = false;
            _files = [];
            _items.Clear();
            SourcePathBox.Text = "";
            AnalysisSection.Visibility = Visibility.Collapsed;
            AnalysisProgressBar.Visibility = Visibility.Collapsed;
            AnalysisCountText.Text = "";
            AnalysisStatusText.Text = "";
            _truncated = false;
            _scannedFiles = 0;
            _totalFound = 0;
            UpdateState();
        }

        /// <summary>Habilita/deshabilita los controles (se usa durante el procesamiento).</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            UpdateState();
        }

        /// <summary>
        /// Refresca el aspecto de la tarjeta (tinte teal cuando hay archivos
        /// cargados y listos para procesar) y notifica a la página mediante
        /// <see cref="StateChanged"/>.
        /// </summary>
        private void UpdateState()
        {
            bool loaded = HasFiles && !_isBusy && _enabled;
            SourceCard.Background = loaded
                ? new SolidColorBrush(Color.FromArgb(0x1F, 0xB8, 0x86, 0x0B))
                : _sourceCardBackground;
            SourceCard.BorderBrush = loaded
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0xB8, 0x86, 0x0B))
                : _sourceCardBorder;

            ClearLoadedButton.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
            BrowseFolderButton.IsEnabled = !_isBusy && _enabled;
            BrowseFilesButton.IsEnabled = !_isBusy && _enabled;

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }
    }
}
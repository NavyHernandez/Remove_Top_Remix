using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>
    /// Página de Eliminación de Duplicados: selecciona una carpeta (se escanea
    /// de forma recursiva, incluidas las subcarpetas, hasta
    /// DuplicateScanner.MaxFilesToScan archivos), agrupa los duplicados en
    /// pestañas numeradas (exactos y posibles) y envía los confirmados a la
    /// Papelera de Windows.
    /// </summary>
    public sealed partial class DuplicateRemovalPage : Page
    {
        private string _folderPath = "";
        private readonly ObservableCollection<DuplicateItem> _exactItems = [];
        private readonly ObservableCollection<DuplicateItem> _possibleItems = [];
        private readonly ObservableCollection<DuplicateItem> _damagedItems = [];
        private readonly ObservableCollection<DeletionResult> _deletionResults = [];
        private CancellationTokenSource? _cts;
        private DeletionMode _lastDeletionMode = DeletionMode.RecycleBin;
        private bool _isScanning;
        private bool _isProcessing;
        private bool _scanPerformed;
        private bool _deletionCompleted;

        // Control de la tarjeta premium: se muestra solo si el escaneo se
        // truncó (la carpeta tenía más de MaxFilesToScan archivos) y la
        // limpieza terminó correctamente. Se guardan los contadores del
        // escaneo para construir el mensaje informativo.
        private bool _scanTruncated;
        private int _scannedFiles;
        private int _totalFound;

        public DuplicateRemovalPage()
        {
            InitializeComponent();
            ExactListView.ItemsSource = _exactItems;
            PossibleListView.ItemsSource = _possibleItems;
            DamagedListView.ItemsSource = _damagedItems;
            DeletionResultsListView.ItemsSource = _deletionResults;
            BrowseButton.Content = UiHelpers.Content(Icon.FolderOpen, "Examinar...", foreground: BrowseButton.Foreground);
            ScanButton.Content = UiHelpers.Content(Icon.Search, "Escanear duplicados", foreground: ScanButton.Foreground);
            SelectAllButton.Content = UiHelpers.Content(Icon.Checkmark, "Borrar todos", semibold: false, foreground: SelectAllButton.Foreground);
            DeleteButton.Content = UiHelpers.Content(Icon.BinRecycle, "Eliminar seleccionados", foreground: DeleteButton.Foreground);
            DeletePermanentButton.Content = UiHelpers.Content(Icon.EraserTool, "Eliminar definitivamente", foreground: DeletePermanentButton.Foreground);
            CancelButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", semibold: false, foreground: CancelButton.Foreground);
            RestartButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: RestartButton.Foreground);

            // Título y subtítulo del encabezado, centralizados en AppLimits.
            PageTitleText.Text = AppLimits.DuplicatesPageTitle;
            PageSubtitleText.Text = AppLimits.DuplicatesPageSubtitle;
            BrandText.Text = AppLimits.AppName;
            BrandSiteRun.Text = AppLimits.AppBrandSite;

            // Muestra el límite de la versión gratuita. El texto se genera a
            // partir de AppLimits para que coincida siempre con el límite real
            // de escaneo (DuplicatesMaxFilesToScan).
            LimitInfoBar.Title = AppLimits.DuplicatesInfoBarTitle;
            LimitInfoBar.Message = AppLimits.DuplicatesInfoBarMessage;

            UpdateUI();
        }

        // ================================================================
        // SELECCIÓN DE CARPETA DE ORIGEN
        // ================================================================

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
            if (folder == null) return;

            _folderPath = folder.Path;
            FolderPathBox.Text = _folderPath;

            ResetResults();
            UpdateUI();
        }

        /// <summary>Limpia los resultados y devuelve la UI al estado inicial.</summary>
        private void ResetResults()
        {
            UnsubscribeItems();
            _exactItems.Clear();
            _possibleItems.Clear();
            _damagedItems.Clear();
            _deletionResults.Clear();
            _scanPerformed = false;
            _deletionCompleted = false;
            _scanTruncated = false;
            _scannedFiles = 0;
            _totalFound = 0;
            ResultsSection.Visibility = Visibility.Collapsed;
            ProgressSection.Visibility = Visibility.Collapsed;
            NoDuplicatesIcon.Visibility = Visibility.Collapsed;
            DeletionResultsSection.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            ScanStatusText.Text = "";
            UpdateTabHeaders();
        }

        private void UnsubscribeItems()
        {
            foreach (var item in _exactItems.Concat(_possibleItems).Concat(_damagedItems))
                item.PropertyChanged -= Item_PropertyChanged;
        }

        /// <summary>
        /// Re-asigna el ItemsSource de las tres ListView de resultados. Si el
        /// contenido de una pestaña del TabView aún no se materializó (pestaña
        /// no seleccionada durante el escaneo), la lista puede quedar vacía
        /// pese a que el contador de la pestaña muestre ítems; esto la fuerza
        /// a re-leer la colección.
        /// </summary>
        private void RefreshListBindings()
        {
            ExactListView.ItemsSource = null;
            ExactListView.ItemsSource = _exactItems;
            PossibleListView.ItemsSource = null;
            PossibleListView.ItemsSource = _possibleItems;
            DamagedListView.ItemsSource = null;
            DamagedListView.ItemsSource = _damagedItems;
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not DuplicateItem item) return;

            if (e.PropertyName == nameof(DuplicateItem.IsMarkedForDeletion))
            {
                UpdateSelectionSummary();
            }
        }

        /// <summary>
        /// Al cambiar de pestaña se fuerza que la ListView de la pestaña recién
        /// seleccionada re-lea su colección. El contenido de las pestañas no
        /// seleccionadas puede no estar materializado/conectado al árbol visual
        /// del TabView, así que el ItemsSource asignado al escanear no basta:
        /// sin este refresco el contador de la pestaña muestra ítems pero la
        /// lista puede quedar vacía (bug de "Posibles").
        /// </summary>
        private void ResultsTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PossibleTab.IsSelected)
            {
                PossibleListView.ItemsSource = null;
                PossibleListView.ItemsSource = _possibleItems;
            }
            else if (ExactTab.IsSelected)
            {
                ExactListView.ItemsSource = null;
                ExactListView.ItemsSource = _exactItems;
            }
            else if (DamagedTab.IsSelected)
            {
                DamagedListView.ItemsSource = null;
                DamagedListView.ItemsSource = _damagedItems;
            }
        }

        // ================================================================
        // ESCANEO
        // ================================================================

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            // Si ya está escaneando, el botón funciona como "Cancelar"
            if (_isScanning)
            {
                _cts?.Cancel();
                return;
            }

            if (string.IsNullOrEmpty(_folderPath))
            {
                ScanStatusText.Text = "Selecciona una carpeta primero.";
                return;
            }

            ResetResults();
            _isScanning = true;
            ScanButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: ScanButton.Foreground);
            BrowseButton.IsEnabled = false;
            ScanLoader.IsActive = true;
            ScanLoader.Visibility = Visibility.Visible;
            ScanStatusText.Text = "Enumerando archivos...";
            NoDuplicatesIcon.Visibility = Visibility.Collapsed;

            _cts = new CancellationTokenSource();
            var scanner = new DuplicateScanner();
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatusText.Text = p.IsIndeterminate
                    ? p.Phase
                    : $"{p.Phase} ({p.Current}/{p.Total})";
            });

            try
            {
                var result = await scanner.ScanAsync(_folderPath, progress, _cts.Token);

                foreach (var item in result.ExactGroups.SelectMany(g => g.Duplicates))
                {
                    item.PropertyChanged += Item_PropertyChanged;
                    _exactItems.Add(item);
                }
                foreach (var item in result.PossibleGroups.SelectMany(g => g.Duplicates))
                {
                    item.PropertyChanged += Item_PropertyChanged;
                    _possibleItems.Add(item);
                }
                foreach (var item in result.DamagedFiles)
                {
                    item.PropertyChanged += Item_PropertyChanged;
                    _damagedItems.Add(item);
                }

                // Fuerza que las ListView (sobre todo la de "Posibles", cuyo
                // contenido puede no estar materializado si su pestaña no está
                // seleccionada) re-lean la colección y rendericen los ítems.
                RefreshListBindings();

                _scanPerformed = true;

                // El escaneo se truncó si la carpeta tenía más archivos de los
                // que se analizaron (MaxFilesToScan). Esto habilita la tarjeta
                // premium, que solo se muestra al terminar la limpieza.
                _scanTruncated = result.TotalFilesFound > result.ScannedFiles;
                _scannedFiles = result.ScannedFiles;
                _totalFound = result.TotalFilesFound;

                UpdateTabHeaders();
                UpdateSelectionSummary();

                if (_exactItems.Count == 0 && _possibleItems.Count == 0 && _damagedItems.Count == 0)
                {
                    NoDuplicatesIcon.Visibility = Visibility.Visible;
                    ScanStatusText.Text = "No se encontraron duplicados.";
                    ResultsSection.Visibility = Visibility.Collapsed;
                }
                else
                {
                    NoDuplicatesIcon.Visibility = Visibility.Collapsed;
                    string truncated = result.TotalFilesFound > result.ScannedFiles
                        ? $" (se analizaron los primeros {result.ScannedFiles} de {result.TotalFilesFound})"
                        : "";
                    string damagedNote = _damagedItems.Count > 0
                        ? $" · {_damagedItems.Count} archivo(s) dañado(s) (< {DuplicateItem.FormatSize(AppLimits.DuplicatesMinValidFileSizeBytes)})"
                        : "";
                    int unmarkedPossible = _possibleItems.Count(i => !i.IsMarkedForDeletion);
                    string possibleNote = unmarkedPossible > 0
                        ? $" · {unmarkedPossible} posible(s) desmarcado(s): revísalos antes de borrar"
                        : "";
                    ScanStatusText.Text = $"Escaneo completado{truncated}. Revisa las pestañas y confirma con los checks.{damagedNote}{possibleNote}";
                    ResultsSection.Visibility = Visibility.Visible;

                    // Muestra directamente la pestaña que tenga resultados
                    ResultsTabView.SelectedItem = _exactItems.Count > 0
                        ? ExactTab
                        : _possibleItems.Count > 0
                            ? PossibleTab
                            : DamagedTab;
                }
            }
            catch (OperationCanceledException)
            {
                // El usuario canceló: se reinicia todo desde cero (ruta incluida)
                ResetAll();
                ScanStatusText.Text = "Escaneo cancelado. Selecciona una carpeta para volver a empezar.";
            }
            catch (Exception ex)
            {
                ScanStatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isScanning = false;
                ScanLoader.IsActive = false;
                ScanLoader.Visibility = Visibility.Collapsed;
                BrowseButton.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
                UpdateUI();
            }
        }

        /// <summary>
        /// Reinicia la página desde cero: limpia la ruta seleccionada, los
        /// resultados y la UI. Se usa al cancelar el escaneo.
        /// </summary>
        private void ResetAll()
        {
            _folderPath = "";
            FolderPathBox.Text = "";
            ResetResults();
            UpdateUI();
        }

        private void UpdateTabHeaders()
        {
            ExactTabHeader.Text = $"Duplicados exactos ({_exactItems.Count})";
            PossibleTabHeader.Text = $"Posibles ({_possibleItems.Count})";
            DamagedTabHeader.Text = $"Archivos dañados ({_damagedItems.Count})";

            int unmarkedPossible = _possibleItems.Count(i => !i.IsMarkedForDeletion);
            string possibleNote = unmarkedPossible > 0
                ? $" · {unmarkedPossible} desmarcados"
                : "";
            long wasted = _exactItems.Concat(_possibleItems).Concat(_damagedItems)
                .Where(i => i.IsMarkedForDeletion).Sum(i => i.Size);
            SummaryText.Text = $"{_exactItems.Count} exacto(s) · {_possibleItems.Count} posible(s){possibleNote} · " +
                DuplicateItem.FormatSize(wasted) + " liberables";
        }

        // ================================================================
        // SELECCIÓN
        // ================================================================

        private int SelectedCount => _exactItems.Concat(_possibleItems).Concat(_damagedItems)
            .Count(i => i.IsMarkedForDeletion);

        private long SelectedBytes => _exactItems.Concat(_possibleItems).Concat(_damagedItems)
            .Where(i => i.IsMarkedForDeletion).Sum(i => i.Size);

        private void UpdateSelectionSummary()
        {
            int selected = SelectedCount;
            SelectionCountText.Text = $"{selected} seleccionado(s) · liberará {DuplicateItem.FormatSize(SelectedBytes)} · " +
                $"límite {AppLimits.DuplicatesMaxDeletionsPerRun} por ejecución";
            bool hasItems = _exactItems.Count + _possibleItems.Count + _damagedItems.Count > 0;
            SelectAllButton.IsEnabled = hasItems && !_isProcessing;
            DeleteButton.IsEnabled = selected > 0 && !_isProcessing;
            DeleteButton.Content = UiHelpers.Content(Icon.BinRecycle,
                selected > 0 ? $"Eliminar seleccionados ({selected})" : "Eliminar seleccionados",
                foreground: DeleteButton.Foreground);
            DeletePermanentButton.IsEnabled = selected > 0 && !_isProcessing;
            DeletePermanentButton.Content = UiHelpers.Content(Icon.EraserTool,
                selected > 0 ? $"Eliminar definitivamente ({selected})" : "Eliminar definitivamente",
                foreground: DeletePermanentButton.Foreground);
        }
        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            // "Borrar todos": marca todos los archivos de las tres pestañas.
            foreach (var item in _exactItems.Concat(_possibleItems).Concat(_damagedItems))
            {
                item.IsMarkedForDeletion = true;
            }
            UpdateSelectionSummary();
        }

        // ================================================================
        // ELIMINACIÓN (Papelera o definitiva)
        // ================================================================

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
            => await RunDeletionAsync(DeletionMode.RecycleBin);

        private async void DeletePermanentButton_Click(object sender, RoutedEventArgs e)
            => await RunDeletionAsync(DeletionMode.Permanent);

        private async Task RunDeletionAsync(DeletionMode mode)
        {
            if (_isProcessing) return;

            var marked = _exactItems.Concat(_possibleItems).Concat(_damagedItems)
                .Where(i => i.IsMarkedForDeletion).ToList();
            if (marked.Count == 0) return;

            if (!await ConfirmDeletionAsync(marked, mode)) return;

            _lastDeletionMode = mode;
            _deletionResults.Clear();
            _isProcessing = true;
            DeleteButton.IsEnabled = false;
            DeletePermanentButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;
            ScanButton.IsEnabled = false;
            SelectAllButton.IsEnabled = false;
            ProgressSection.Visibility = Visibility.Visible;
            DeletionResultsSection.Visibility = Visibility.Visible;
            DeleteProgressBar.Value = 0;

            _cts = new CancellationTokenSource();
            var remover = new DuplicateRemover();
            var progress = new Progress<DeletionProgress>(p =>
            {
                DeleteProgressBar.Value = p.Percentage;
                DeleteProgressCountText.Text = $"{p.CurrentIndex}/{p.TotalCount}";
                DeleteProgressText.Text = p.CurrentFile;

                if (p.Result != null)
                {
                    _deletionResults.Add(p.Result);
                    DeletionResultsListView.ScrollIntoView(p.Result);
                    UpdateDeletionSummary();
                }
            });

            try
            {
                await remover.RemoveFilesAsync(marked, mode, progress, _cts.Token);

                // Quita de la lista SOLO los archivos que se eliminaron
                // correctamente; los que fallaron permanecen marcados para
                // poder reintentarlos.
                var deletedPaths = _deletionResults
                    .Where(r => r.Success)
                    .Select(r => r.FilePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var item in marked
                    .Where(i => i.IsMarkedForDeletion && deletedPaths.Contains(i.FilePath))
                    .ToList())
                {
                    item.PropertyChanged -= Item_PropertyChanged;
                    _exactItems.Remove(item);
                    _possibleItems.Remove(item);
                    _damagedItems.Remove(item);
                }

                UpdateTabHeaders();
                UpdateSelectionSummary();

                // Tras la eliminación, la fila de botones ya no debe mostrarse:
                // solo quedan visibles los resultados de la operación.
                _deletionCompleted = true;

                // Se oculta el TabView de resultados: aunque queden ítems sin
                // seleccionar (p. ej. "Posibles"), tras el borrado solo debe
                // verse el resultado de lo que se eliminó.
                ResultsSection.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                _deletionResults.Add(new DeletionResult
                {
                    FileName = "---",
                    Success = false,
                    Message = "Proceso cancelado por el usuario"
                });
                UpdateDeletionSummary();
            }
            finally
            {
                _isProcessing = false;
                ProgressSection.Visibility = Visibility.Collapsed;
                BrowseButton.IsEnabled = true;
                ScanButton.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
                RestartButton.Visibility = Visibility.Visible;
                UpdateUI();
            }
        }

        /// <summary>
        /// Muestra un diálogo de confirmación con el conteo exacto y el
        /// espacio liberable. Para el borrado definitivo el aviso es más
        /// explícito porque la acción NO se puede deshacer.
        /// </summary>
        private async Task<bool> ConfirmDeletionAsync(IReadOnlyList<DuplicateItem> items, DeletionMode mode)
        {
            bool permanent = mode == DeletionMode.Permanent;
            var dialog = new ContentDialog
            {
                Title = permanent ? "Eliminación definitiva" : "Confirmar eliminación",
                Content = permanent
                    ? $"Se ELIMINARÁN DEFINITIVAMENTE {items.Count} archivo(s) " +
                      $"({DuplicateItem.FormatSize(items.Sum(i => i.Size))}).\n" +
                      "Esta acción NO se puede deshacer y no pasarán por la Papelera.\n" +
                      "Se conservará 1 copia de cada grupo."
                    : $"Se enviarán a la Papelera {items.Count} archivo(s) " +
                      $"({DuplicateItem.FormatSize(items.Sum(i => i.Size))}).\n" +
                      "Se conservará 1 copia de cada grupo.",
                PrimaryButtonText = permanent ? "Eliminar definitivamente" : "Eliminar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private void UpdateDeletionSummary()
        {
            int ok = _deletionResults.Count(r => r.Success);
            int fail = _deletionResults.Count(r => !r.Success);
            string action = _lastDeletionMode == DeletionMode.Permanent ? "eliminados" : "en Papelera";
            DeletionSummaryText.Text = $"{ok} {action} · {fail} errores · {_deletionResults.Count} total";
        }

        /// <summary>
        /// "Limpiar": vuelve la página a su estado inicial tras la
        /// eliminación. Limpia la ruta seleccionada y todos los resultados.
        /// </summary>
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            ResetAll();
        }

        // ================================================================
        // VERSIÓN PREMIUM
        // ================================================================

        /// <summary>
        /// Abre en el navegador la URL de la versión premium. El destino se
        /// centraliza en <see cref="PremiumLinks.UpgradeUrl"/> para poder
        /// cambiarlo manualmente en un solo lugar. Si la apertura falla se
        /// muestra el motivo en la línea de estado del escaneo.
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
                ScanStatusText.Text = $"No se pudo abrir el enlace premium: {ex.Message}";
            }
        }

        // ================================================================
        // ESTADO DE LA UI
        // ================================================================

        private void UpdateUI()
        {
            // Durante el escaneo el botón queda activo como "Cancelar"
            ScanButton.IsEnabled = !_isProcessing && (_isScanning || !string.IsNullOrEmpty(_folderPath));
            ScanButton.Content = UiHelpers.Content(
                _isScanning ? Icon.Dismiss : Icon.Search,
                _isScanning ? "Cancelar" : "Escanear duplicados",
                foreground: ScanButton.Foreground);

            // "Cancelar" está activo mientras haya una carpeta cargada (o un escaneo en curso)
            CancelButton.IsEnabled = !_isProcessing && (_isScanning || !string.IsNullOrEmpty(_folderPath));

            // Fila de acciones: al cargar una carpeta solo aparece "Cancelar".
            // La fila completa (Borrar todos / Eliminar...) solo tras un escaneo
            // con resultados. Si no hay duplicados ni dañados, no aparece nada.
            // Tras una eliminación, los botones desaparecen y solo quedan los resultados.
            bool folderLoaded = !string.IsNullOrEmpty(_folderPath);
            bool hasItems = _exactItems.Count + _possibleItems.Count + _damagedItems.Count > 0;

            if (_deletionCompleted || !folderLoaded || (!hasItems && _scanPerformed))
            {
                ActionsSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                ActionsSection.Visibility = Visibility.Visible;
                SelectionCountText.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
                SelectAllButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
                DeleteButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
                DeletePermanentButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Visible;
            }

            // La tarjeta premium solo aparece si el escaneo se truncó (carpeta
            // con más de AppLimits.DuplicatesMaxFilesToScan archivos) y la
            // limpieza ya terminó correctamente. En cualquier otro momento
            // permanece oculta.
            if (_deletionCompleted && _scanTruncated)
            {
                PremiumMessageText.Text = $"Se analizaron solo los primeros {_scannedFiles} de {_totalFound} " +
                    "archivos encontrados. Con la versión premium podrás escanear carpetas completas " +
                    "sin límites y eliminar todos los duplicados de una sola vez.";
                PremiumSection.Visibility = Visibility.Visible;
            }
            else
            {
                PremiumSection.Visibility = Visibility.Collapsed;
            }

            UpdateSelectionSummary();
        }

        /// <summary>
        /// "Cancelar": si hay un escaneo en curso lo cancela (el catch reinicia
        /// todo desde cero); si no, limpia los resultados y la ruta seleccionada.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning)
            {
                _cts?.Cancel();
                return;
            }
            if (_isProcessing) return;
            ResetAll();
        }
    }
}

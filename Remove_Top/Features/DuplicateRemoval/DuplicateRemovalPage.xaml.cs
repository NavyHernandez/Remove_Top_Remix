using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remove_Top.Features.AudioPreview;
using Remove_Top.Features.ImagePreview;
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

        // Previsualizador de audio: motor NAudio + timer que actualiza el
        // playhead y el reloj de la onda mientras se reproduce.
        private readonly AudioPreviewPlayer _previewPlayer = new();
        private readonly DispatcherTimer _previewTimer;

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
            SelectAllButton.Content = UiHelpers.Content(Icon.Checkmark, "Marcar todos", semibold: false, foreground: SelectAllButton.Foreground);
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

            // Previsualizador: timer de playhead (100 ms), evento de fin de
            // reproducción y acento de la onda (naranja, el color de la feature).
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _previewTimer.Tick += PreviewTimer_Tick;
            _previewPlayer.PlaybackEnded += PreviewPlayer_PlaybackEnded;
            PreviewWaveform.SeekRequested += PreviewWaveform_SeekRequested;
            PreviewWaveform.SetAccentColor(Windows.UI.Color.FromArgb(255, 230, 126, 34));
            PreviewPlayButton.Content = UiHelpers.Icon(Icon.Play, IconVariant.Regular, IconSize.Size16, foreground: PreviewPlayButton.Foreground);
            PreviewStopButton.Content = UiHelpers.Icon(Icon.Stop, IconVariant.Regular, IconSize.Size16, foreground: PreviewStopButton.Foreground);

            // Visor de imágenes: notificaciones para el pie de la tarjeta
            // (dimensiones y error de lectura).
            ImagePreviewViewer.ImageLoaded += ImagePreviewViewer_ImageLoaded;
            ImagePreviewViewer.ImageLoadFailed += ImagePreviewViewer_ImageLoadFailed;

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
            // Detiene y libera cualquier audio/imagen en reproducción (el archivo
            // puede borrarse o desaparecer al cambiar de carpeta).
            StopAllPreviews();
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

            ScannedFilesText.Text = _totalFound > _scannedFiles
                ? $"Se examinaron los primeros {_scannedFiles} de {_totalFound} archivos"
                : $"{_scannedFiles} archivo(s) examinado(s)";
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

            // Detiene y libera el archivo en reproducción: sin esto, el borrado
            // del archivo que estaba sonando fallaría por el bloqueo del lector.
            StopAllPreviews();

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
        // PREVISUALIZADOR DE AUDIO (solo pestañas Exactos/Posibles)
        // ================================================================

        /// <summary>
        /// Botón "Previsualizar" de una fila: carga y reproduce el audio o
        /// muestra la imagen según el tipo del archivo. La ruta viaja en el Tag
        /// del botón (binding FilePath).
        /// </summary>
        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string path }) return;
            if (!File.Exists(path)) return;

            // Tipos no visualizables (video, documentos, etc.): el botón está
            // deshabilitado y solo informa de que no hay preview.
            if (!ImagePreviewSupport.IsImageFile(path) && !AudioPreviewPlayer.IsSupportedAudio(path))
                return;

            if (ImagePreviewSupport.IsImageFile(path))
                BeginImagePreview(path);
            else
                await BeginPreviewAsync(path);
        }

        // ================================================================
        // PREVISUALIZADOR DE IMÁGENES (solo pestañas Exactos/Posibles)
        // ================================================================

        /// <summary>
        /// Muestra la tarjeta de imagen con la imagen seleccionada ajustada al
        /// espacio disponible (sin zoom). Cierra cualquier audio previo para
        /// tener un solo preview activo a la vez.
        /// </summary>
        private void BeginImagePreview(string path)
        {
            // Ya es la imagen actual: solo reabrir la tarjeta.
            if (string.Equals(ImagePreviewViewer.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                ImagePreviewSection.Visibility = Visibility.Visible;
                return;
            }

            // Otro preview (audio o imagen): cerrar el actual liberando su archivo.
            StopAllPreviews();

            ImagePreviewSection.Visibility = Visibility.Visible;
            ImagePreviewFileNameText.Text = Path.GetFileName(path);
            ImagePreviewInfoText.Text = "Cargando...";
            ImagePreviewViewer.Load(path);
        }

        /// <summary>
        /// Libera la imagen mostrada (memoria y posible bloqueo del archivo) y
        /// oculta la tarjeta.
        /// </summary>
        private void ClearImagePreview()
        {
            ImagePreviewViewer.Clear();
            ImagePreviewSection.Visibility = Visibility.Collapsed;
            ImagePreviewFileNameText.Text = "";
            ImagePreviewInfoText.Text = "";
        }

        private void ImagePreviewClose_Click(object sender, RoutedEventArgs e)
        {
            ClearImagePreview();
        }

        /// <summary>
        /// Imagen decodificada correctamente: muestra sus dimensiones reales
        /// (0 x 0 en SVG, cuyo tamaño no se expone) y el tamaño en disco.
        /// </summary>
        private void ImagePreviewViewer_ImageLoaded(int width, int height)
        {
            if (string.IsNullOrEmpty(ImagePreviewViewer.CurrentPath)) return;
            var sizeText = DuplicateItem.FormatSize(new FileInfo(ImagePreviewViewer.CurrentPath).Length);
            string dims = width > 0 && height > 0 ? $"{width} × {height} px" : "Vectorial (SVG)";
            ImagePreviewInfoText.Text = $"{dims} · {sizeText}";
        }

        /// <summary>No se pudo leer la imagen: lo indica el propio visor y el pie de la tarjeta.</summary>
        private void ImagePreviewViewer_ImageLoadFailed()
        {
            if (string.IsNullOrEmpty(ImagePreviewViewer.CurrentPath)) return;
            ImagePreviewInfoText.Text = "No se pudo abrir la imagen.";
        }

        /// <summary>
        /// Detiene y libera todos los previews activos (audio y/o imagen).
        /// </summary>
        private void StopAllPreviews()
        {
            StopPreviewCore(closeFile: true);
            ClearImagePreview();
        }

        /// <summary>
        /// Carga un archivo en el previsualizador: libera el anterior (para no
        /// dejar el archivo bloqueado), carga el audio con NAudio en segundo
        /// plano, extrae la forma de onda y reproduce.
        /// </summary>
        private async Task BeginPreviewAsync(string path)
        {
            // Ya es el archivo actual: solo mostrar la tarjeta y reproducir.
            if (_previewPlayer.IsLoaded &&
                string.Equals(_previewPlayer.CurrentFilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                PreviewSection.Visibility = Visibility.Visible;
                if (_previewPlayer.State != AudioPreviewState.Playing)
                    _previewPlayer.Play();
                UpdateTransportControls();
                return;
            }

            // Otro archivo (o ninguno): cierra el preview actual (audio o
            // imagen) liberando su bloqueo.
            StopAllPreviews();

            PreviewSection.Visibility = Visibility.Visible;
            PreviewFileNameText.Text = Path.GetFileName(path);
            PreviewTimeText.Text = "Cargando...";
            PreviewWaveform.SetData(new WaveformData());
            UpdateTransportControls();

            // Carga el archivo (NAudio) en segundo plano.
            bool loaded = await _previewPlayer.LoadAsync(path);
            if (!loaded)
            {
                PreviewTimeText.Text = "No se pudo leer el archivo.";
                PreviewPlayButton.IsEnabled = false;
                PreviewStopButton.IsEnabled = false;
                return;
            }

            // Extrae los peaks de la onda en segundo plano. El número de
            // columnas sigue el ancho del control (con un rango razonable).
            double width = PreviewWaveform.ActualWidth > 0 ? PreviewWaveform.ActualWidth : 600;
            int columns = (int)Math.Clamp(width, 200, 1200);
            var peaks = await WaveformPeaks.ComputeAsync(path, columns);
            PreviewWaveform.SetData(peaks);

            _previewTimer.Start();
            _previewPlayer.Play();
            UpdateTransportControls();
        }

        /// <summary>Actualiza los botones de transporte según el estado del reproductor.</summary>
        private void UpdateTransportControls()
        {
            bool loaded = _previewPlayer.IsLoaded;
            bool playing = _previewPlayer.State == AudioPreviewState.Playing;
            PreviewPlayButton.IsEnabled = loaded;
            PreviewStopButton.IsEnabled = loaded;
            PreviewPlayButton.Content = UiHelpers.Icon(
                playing ? Icon.Pause : Icon.Play,
                IconVariant.Regular,
                IconSize.Size16,
                foreground: PreviewPlayButton.Foreground);
        }

        private void PreviewPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (!_previewPlayer.IsLoaded) return;
            _previewPlayer.TogglePlay();
            UpdateTransportControls();
        }

        private void PreviewStop_Click(object sender, RoutedEventArgs e)
        {
            if (!_previewPlayer.IsLoaded) return;
            _previewPlayer.Stop();
            PreviewWaveform.SetPlayhead(0);
            PreviewTimeText.Text = $"0:00 / {FormatTs(_previewPlayer.Duration)}";
            UpdateTransportControls();
        }

        /// <summary>Cierra la tarjeta y libera el archivo (para poder borrarlo).</summary>
        private void PreviewClose_Click(object sender, RoutedEventArgs e)
        {
            StopPreviewCore(closeFile: true);
        }

        /// <summary>Scrub sobre la onda: mueve la reproducción a la fracción elegida.</summary>
        private void PreviewWaveform_SeekRequested(double fraction)
        {
            if (!_previewPlayer.IsLoaded) return;
            _previewPlayer.SeekToFraction(fraction);
        }

        /// <summary>
        /// Tick del timer: mientras se reproduce, actualiza el playhead de la
        /// onda y el reloj (posición / duración).
        /// </summary>
        private void PreviewTimer_Tick(object? sender, object e)
        {
            if (!_previewPlayer.IsLoaded) return;
            PreviewWaveform.SetPlayhead(_previewPlayer.PositionFraction);
            PreviewTimeText.Text = $"{FormatTs(_previewPlayer.Position)} / {FormatTs(_previewPlayer.Duration)}";
        }

        /// <summary>
        /// Fin natural de la reproducción (el evento llega desde un hilo de
        /// audio, así que se marisme a la UI con DispatcherQueue): vuelve al
        /// inicio y deja el botón en "Reproducir".
        /// </summary>
        private void PreviewPlayer_PlaybackEnded()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _previewPlayer.Seek(TimeSpan.Zero);
                PreviewWaveform.SetPlayhead(0);
                PreviewTimeText.Text = $"0:00 / {FormatTs(_previewPlayer.Duration)}";
                UpdateTransportControls();
            });
        }

        /// <summary>
        /// Detiene el previsualizador y, si se indica, cierra y libera el
        /// archivo para que pueda ser borrado. Oculta la tarjeta.
        /// </summary>
        private void StopPreviewCore(bool closeFile)
        {
            _previewPlayer.Stop();
            if (closeFile) _previewPlayer.Close();
            _previewTimer.Stop();
            PreviewSection.Visibility = Visibility.Collapsed;
            PreviewFileNameText.Text = "";
            PreviewTimeText.Text = "";
            UpdateTransportControls();
        }

        /// <summary>Formatea una duración como m:ss (u h:mm:ss).</summary>
        private static string FormatTs(TimeSpan t)
        {
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }

        /// <summary>Al salir de la página se detiene y libera la reproducción.</summary>
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAllPreviews();
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

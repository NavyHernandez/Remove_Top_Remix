using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Remove_Top.Features.AudioPreview;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.Storage;

namespace Remove_Top.Features.Normalization
{
    /// <summary>
    /// Página de Normalización: selecciona una carpeta, analiza los picos de los
    /// archivos de audio y los normaliza a un dBFS objetivo usando AudioNormalizer.
    /// </summary>
    public sealed partial class NormalizationPage : Page
    {
        private readonly ObservableCollection<AnalysisResult> _analysisResults = [];
        /// <summary>
        /// Respaldo síncrono de todos los análisis (incluye los acumulados por
        /// arrastre). Se usa para calcular válidos/errores y habilitar Normalizar
        /// sin depender de los callbacks asíncronos que llenan _analysisResults
        /// (esa carrera dejaba "No se pudieron analizar" con archivos válidos).
        /// </summary>
        private readonly List<AnalysisResult> _analysisAll = [];
        private readonly ObservableCollection<NormalizationResult> _results = [];
        private CancellationTokenSource? _cts;
        private bool _isProcessing;
        private bool _isAnalyzing;
        private bool _isUpdatingSlider;
        /// <summary>Canciones sueltas cargadas por arrastre (vacío = modo carpeta).</summary>
        private string[] _looseFiles = [];
        /// <summary>Retardo para ocultar el overlay al salir (evita parpadeos).</summary>
        private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(250);
        private readonly DispatcherTimer _hideTimer;

        /// <summary>Previsualizador de audio (módulo compartido con Duplicados).</summary>
        private readonly AudioPreviewPlayer _previewPlayer = new();
        private readonly DispatcherTimer _previewTimer;

        public NormalizationPage()
        {
            InitializeComponent();
            AnalysisListView.ItemsSource = _analysisResults;
            ResultsListView.ItemsSource = _results;
            TargetSlider.Value = -1.0;
            BrowseButton.Content = UiHelpers.Content(Icon.FolderOpen, "Examinar...", foreground: BrowseButton.Foreground);
            CancelButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: CancelButton.Foreground);
            ClearButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: ClearButton.Foreground);

            // Título y subtítulo del encabezado, centralizados en AppLimits.
            PageTitleText.Text = AppLimits.NormalizationPageTitle;
            PageSubtitleText.Text = AppLimits.NormalizationPageSubtitle;
            BrandText.Text = AppLimits.AppName;

            // Muestra el límite de la versión gratuita. El texto (título y
            // mensaje) se genera a partir de AppLimits: usa el límite publicitado
            // (NormalizationFreeLimitDisplay); el procesamiento real sigue el de
            // NormalizationMaxFilesToScan.
            LimitInfoBar.Title = AppLimits.NormalizationInfoBarTitle;
            LimitInfoBar.Message = AppLimits.NormalizationInfoBarMessage;

            PopulateIntensityOptions();
            UpdateStartButtonText();
            _hideTimer = new DispatcherTimer { Interval = HideDelay };
            _hideTimer.Tick += HideTimer_Tick;

            // Previsualizador: timer que refresca el playhead y el reloj.
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _previewTimer.Tick += PreviewTimer_Tick;
            _previewPlayer.PlaybackEnded += PreviewPlayer_PlaybackEnded;
            PreviewWaveform.SeekRequested += PreviewWaveform_SeekRequested;
        }

        /// <summary>
        /// Llena el selector de intensidad de masterización con los tres perfiles
        /// disponibles. Por defecto se elige "Hard Limiter" (el paso profesional
        /// que rellena la onda); el usuario puede volver a "Ligera" en cualquier momento.
        /// </summary>
        private void PopulateIntensityOptions()
        {
            IntensityComboBox.Items.Clear();
            foreach (var intensity in Enum.GetValues<MasteringIntensity>())
            {
                IntensityComboBox.Items.Add(new ComboBoxItem
                {
                    Content = MasteringChain.DisplayName(intensity),
                    Tag = intensity
                });
            }
            IntensityComboBox.SelectedIndex = 1; // Hard Limiter
        }

        /// <summary>
        /// Devuelve el perfil de intensidad seleccionado en el ComboBox.
        /// </summary>
        private MasteringIntensity GetSelectedIntensity()
        {
            if (IntensityComboBox.SelectedItem is ComboBoxItem item && item.Tag is MasteringIntensity intensity)
                return intensity;
            return MasteringIntensity.HardLimiter;
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
                await LoadFolderAsync(folder.Path);
        }

        /// <summary>
        /// Carga una carpeta: el MISMO camino que el botón Examinar. También lo
        /// usa el arrastre cuando se suelta una sola carpeta.
        /// </summary>
        private async Task LoadFolderAsync(string folderPath)
        {
            _looseFiles = [];
            FolderPathBox.Text = folderPath;
            var files = AudioNormalizer.GetAudioFiles(folderPath, out int totalFound, out int alreadyProcessed);
            FileCountText.Text = BuildFileCountText(files.Length, totalFound, alreadyProcessed);
            FileCountText.Visibility = Visibility.Visible;
            StartButton.IsEnabled = false;
            await AnalyzeFilesAsync(files);
            UpdateStartButtonText();
        }

        /// <summary>
        /// Arrastre sobre la página: solo se aceptan elementos del sistema de
        /// archivos y solo si no hay análisis/procesado en curso. Mientras se
        /// está encima se muestra el overlay ("Suelta para cargar").
        /// </summary>
        private void Root_DragEnter(object sender, DragEventArgs e) => Root_DragOver(sender, e);

        private void Root_DragOver(object sender, DragEventArgs e)
        {
            if (_isAnalyzing || _isProcessing) return;
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Suelta para cargar";
                Overlay.Visibility = Visibility.Visible;
                _hideTimer.Stop();
            }
            else
            {
                Overlay.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Al salir del área se programa la ocultación del overlay: sin este
        /// retardo, pasar sobre los elementos hijos lo haría parpadear.
        /// </summary>
        private void Root_DragLeave(object sender, DragEventArgs e)
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private void HideTimer_Tick(object? sender, object e)
        {
            _hideTimer.Stop();
            Overlay.Visibility = Visibility.Collapsed;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _hideTimer.Stop();
            StopAllPreviews();
        }

        /// <summary>
        /// Suelta sobre la página: una sola carpeta sigue el camino manual
        /// (LoadFolderAsync); canciones sueltas (o mezcla) van a la lista de
        /// sueltas filtrada por formato de audio. Nada usa los controles
        /// compartidos de arrastre: esta página analiza al cargar y necesita
        /// su propio camino con timeout y cancelación.
        /// </summary>
        private async void Root_Drop(object sender, DragEventArgs e)
        {
            _hideTimer.Stop();
            Overlay.Visibility = Visibility.Collapsed;
            if (_isAnalyzing || _isProcessing) return;
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            IReadOnlyList<IStorageItem>? items;
            try
            {
                items = await e.DataView.GetStorageItemsAsync();
            }
            catch (Exception ex)
            {
                App.Log("Normalization", $"Drop GetStorageItemsAsync: {ex.Message}", ex.StackTrace);
                FileCountText.Text = "No se pudo leer lo soltado. Usa el botón Examinar...";
                FileCountText.Visibility = Visibility.Visible;
                return;
            }

            if (items == null || items.Count == 0) return;

            var folders = items.OfType<StorageFolder>().Select(f => f.Path).ToArray();
            var loose = items.OfType<StorageFile>()
                .Select(f => f.Path)
                .Where(p => AudioNormalizer.IsAudioFile(p))
                .ToArray();

            // Una sola carpeta y nada más: camino manual idéntico.
            if (folders.Length == 1 && loose.Length == 0)
            {
                await LoadFolderAsync(folders[0]);
                return;
            }

            // Canciones sueltas (más lo de las carpetas soltadas, si las hay).
            var fromFolders = new List<string>();
            foreach (var folder in folders)
            {
                fromFolders.AddRange(AudioNormalizer.GetAudioFiles(
                    folder, out _, out _));
            }

            var all = loose.Concat(fromFolders).Distinct().ToArray();
            var pending = AudioNormalizer.GetAudioFiles(all, out _, out _);

            if (pending.Length == 0 && _looseFiles.Length == 0)
            {
                FileCountText.Text = "No se encontraron archivos de audio en lo soltado.";
                FileCountText.Visibility = Visibility.Visible;
                return;
            }

            // Acumular: lo ya cargado + lo recién soltado, sin repetir, con el
            // tope de la versión gratuita. Soltar una carpeta sola o Examinar
            // sigue reemplazando (LoadFolderAsync vacía la lista).
            bool freshList = _looseFiles.Length == 0;
            var previous = _looseFiles;
            var newcomers = pending
                .Where(p => !previous.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var combined = previous.Concat(newcomers).ToArray();
            bool capped = combined.Length > AppLimits.NormalizationFreeLimitDisplay;
            _looseFiles = combined.Take(AppLimits.NormalizationFreeLimitDisplay).ToArray();
            var added = _looseFiles
                .Where(p => !previous.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (added.Length == 0)
            {
                FileCountText.Text = capped
                    ? $"Tope de {AppLimits.NormalizationFreeLimitDisplay} de la versión gratuita: {_looseFiles.Length} canción(es) suelta(s)."
                    : "Esas canciones ya están cargadas.";
                FileCountText.Visibility = Visibility.Visible;
                return;
            }

            FolderPathBox.Text = $"{_looseFiles.Length} canción(es) suelta(s)";
            var countMsg = BuildFileCountText(_looseFiles.Length, _looseFiles.Length, 0);
            if (capped)
                countMsg += $" · tope de {AppLimits.NormalizationFreeLimitDisplay} de la versión gratuita";
            FileCountText.Text = countMsg;
            FileCountText.Visibility = Visibility.Visible;
            StartButton.IsEnabled = false;
            // Solo se analiza lo recién llegado; lo anterior ya está en la tabla
            // (si la lista es nueva, se limpia primero al venir de modo carpeta).
            await AnalyzeFilesAsync(added, append: !freshList);
            UpdateStartButtonText();
        }

        /// <summary>
        /// Construye el texto con el conteo de archivos. Muestra cuántos ya fueron
        /// procesados (omitidos) y, si la carpeta supera el límite gratuito, avisa
        /// que solo se analizan/procesan los primeros N.
        /// </summary>
        private static string BuildFileCountText(int scanned, int totalFound, int alreadyProcessed)
        {
            var parts = new List<string>();

            if (alreadyProcessed > 0)
                parts.Add($"{alreadyProcessed} ya procesado(s), omitidos");

            parts.Add(totalFound > scanned
                ? $"{totalFound} archivo(s) de audio encontrados \u00b7 se analizan los primeros {scanned} (l\u00edmite de la versi\u00f3n gratuita)"
                : $"{scanned} archivo(s) de audio pendiente(s) de procesar");

            return string.Join(" \u00b7 ", parts);
        }

        /// <summary>
        /// Analiza archivos y agrega sus filas a la tabla. Con
        /// <paramref name="append"/> en true (suelta que acumula) se conservan
        /// las filas ya cargadas y solo se analiza lo recién llegado.
        /// </summary>
        private async Task AnalyzeFilesAsync(string[] files, bool append = false)
        {
            if (!append)
            {
                _analysisResults.Clear();
                _analysisAll.Clear();
            }
            _isAnalyzing = true;
            AnalysisSection.Visibility = Visibility.Visible;
            AnalysisProgressBar.IsIndeterminate = true;
            AnalysisStatusText.Text = "Analizando archivos...";
            BrowseButton.IsEnabled = false;
            CancelButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", semibold: false, foreground: CancelButton.Foreground);
            CancelButton.Visibility = Visibility.Visible;

            // El callback solo da feedback en vivo del archivo en curso; las
            // filas se cargan de forma síncrona al terminar (abajo), para que la
            // tabla y los conteos nunca queden desfasados.
            var progress = new Progress<AnalysisResult>(r =>
                AnalysisStatusText.Text = $"Analizando... {r.FileName}");

            _cts = new CancellationTokenSource();

            try
            {
                var normalizer = new AudioNormalizer();
                var results = await normalizer.AnalyzeFilesAsync(files, progress, _cts.Token);

                // Fuente confiable: el array que devuelve el análisis (síncrono).
                _analysisAll.AddRange(results);
                _analysisResults.Clear();
                foreach (var r in _analysisAll)
                    _analysisResults.Add(r);

                var valid = _analysisAll.Where(r => r.Success).ToArray();
                int errors = _analysisAll.Count - valid.Length;
                if (valid.Length > 0)
                {
                    double min = valid.Min(r => r.PeakDb);
                    double max = valid.Max(r => r.PeakDb);
                    AnalysisSummaryText.Text = $"Rango: {min:F1} a {max:F1} dBFS \u00b7 {valid.Length} v\u00e1lidos"
                        + (errors > 0 ? $" \u00b7 {errors} con error" : "");
                }
                else
                {
                    AnalysisSummaryText.Text = "No se pudieron analizar los archivos";
                }

                StartButton.IsEnabled = valid.Length > 0 && !_isProcessing;
                CancelButton.Visibility = valid.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

                // El estado final se encola para que corra después de los
                // callbacks de progreso pendientes (DispatcherQueue es FIFO).
                DispatcherQueue.TryEnqueue(() =>
                    AnalysisStatusText.Text = $"Completo \u2014 {_analysisAll.Count} archivos");
            }
            catch (OperationCanceledException)
            {
                ResetPageState();
                FileCountText.Text = "Análisis cancelado. Selecciona una carpeta para volver a empezar.";
                FileCountText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AnalysisStatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isAnalyzing = false;
                AnalysisProgressBar.IsIndeterminate = false;
                BrowseButton.IsEnabled = true;
                CancelButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: CancelButton.Foreground);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void TargetSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingSlider) return;
            _isUpdatingSlider = true;
            TargetValueBox.Text = TargetSlider.Value.ToString("F1");
            _isUpdatingSlider = false;
            UpdateStartButtonText();
        }

        private void TargetValueBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            if (string.IsNullOrEmpty(args.NewText)) return;
            args.Cancel = !double.TryParse(args.NewText.Replace(',', '.'), out var val)
                          || val < -12.0 || val > 0.0;
        }

        private void TargetValueBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingSlider) return;
            if (double.TryParse(TargetValueBox.Text.Replace(',', '.'), out var val))
            {
                val = Math.Clamp(val, -12.0, 0.0);
                _isUpdatingSlider = true;
                TargetSlider.Value = val;
                _isUpdatingSlider = false;
                UpdateStartButtonText();
            }
        }

        private void UpdateStartButtonText()
        {
            if (_isProcessing) return;
            double target = TargetSlider.Value;
            StartButton.Content = UiHelpers.Content(Icon.Play, $"Normalizar a {target:F1} dBFS",
                foreground: StartButton.Foreground);
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                return;
            }

            // Se procesa la lista mostrada (lo que el usuario no quitó con la
            // "x"). Así la eliminación de una fila excluye de verdad la canción.
            var files = _analysisAll.Select(a => a.FilePath).ToArray();
            if (files.Length == 0) return;
            int totalFound = files.Length;
            int alreadyProcessed = 0;

            // Libera cualquier archivo en reproducción antes de procesar.
            StopAllPreviews();

            // Mantiene el aviso de límite/omitidos visible al iniciar el procesamiento
            FileCountText.Text = BuildFileCountText(files.Length, totalFound, alreadyProcessed);
            FileCountText.Visibility = Visibility.Visible;

            if (!double.TryParse(TargetValueBox.Text.Replace(',', '.'), out var targetDb))
                targetDb = -1.0;

            _results.Clear();
            _isProcessing = true;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            CancelButton.Visibility = Visibility.Collapsed;
            BrowseButton.IsEnabled = false;
            FolderPathBox.IsEnabled = false;
            ProgressSection.Visibility = Visibility.Visible;
            ResultsSection.Visibility = Visibility.Visible;
            ProcessingRing.IsActive = true;
            CompletedIcon.Visibility = Visibility.Collapsed;
            ClearButton.Visibility = Visibility.Collapsed;
            ProgressTitleText.Text = "Procesando...";
            ProgressText.Text = $"Preparando {files.Length} archivo(s)...";

            _cts = new CancellationTokenSource();
            var normalizer = new AudioNormalizer();
            var progress = new Progress<NormalizationProgress>(p =>
            {
                ProgressBar.Value = p.Percentage;
                ProgressText.Text = $"[{p.CurrentIndex}/{p.TotalCount}] {p.CurrentFile}";

                if (p.Result != null)
                {
                    _results.Add(p.Result);
                    ResultsListView.ScrollIntoView(p.Result);
                    UpdateSummary();
                }
            });

            try
            {
                await normalizer.ProcessFilesAsync(files, targetDb, GetSelectedIntensity(), progress, _cts.Token);

                // Corrección ortográfica de nombres de salida
                ProgressText.Text = "Corrigiendo nombres...";
                AudioNormalizer.CorrectOutputNames(_results);

                // Reconstruir la colección para refrescar el ListView
                var corrected = _results.ToList();
                _results.Clear();
                foreach (var r in corrected)
                    _results.Add(r);

                // Terminó el procesamiento: muestra el estado "Completado"
                ProgressBar.Value = 100;
                ProcessingRing.IsActive = false;
                int ok = _results.Count(r => r.Success);
                int fail = _results.Count(r => !r.Success);
                int correctedCount = _results.Count(r => r.Success && r.Message.Contains("nombre corregido"));

                // Icono profesional de estado: check verde si todo salió bien,
                // advertencia ámbar si hubo errores.
                CompletedIcon.Icon = fail > 0 ? Icon.Warning : Icon.CheckmarkCircle;
                CompletedIcon.Foreground = fail > 0
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 230, 126, 34))
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 39, 174, 96));
                CompletedIcon.Visibility = Visibility.Visible;

                ProgressTitleText.Text = "Completado";
                var completionMsg = fail > 0
                    ? $"Completado \u2014 {ok} de {files.Length} archivo(s) procesado(s) correctamente, {fail} con error"
                    : $"Completado \u2014 {ok} de {files.Length} archivo(s) procesado(s) correctamente";
                if (correctedCount > 0)
                    completionMsg += $" \u00b7 {correctedCount} nombre(s) corregido(s)";
                ProgressText.Text = completionMsg;

                // Solo si TODO terminó correctamente se ofrece limpiar y empezar de nuevo
                if (fail == 0)
                    ClearButton.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException)
            {
                ProcessingRing.IsActive = false;
                ProgressTitleText.Text = "Cancelado";
                ProgressText.Text = "Proceso cancelado por el usuario.";
                var canceled = new NormalizationResult
                {
                    FileName = "---",
                    Success = false,
                    Message = "Proceso cancelado por el usuario"
                };
                _results.Add(canceled);
                UpdateSummary();
            }
            finally
            {
                _isProcessing = false;
                UpdateStartButtonText();
                BrowseButton.IsEnabled = true;
                FolderPathBox.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void UpdateSummary()
        {
            int ok = _results.Count(r => r.Success);
            int fail = _results.Count(r => !r.Success);
            SummaryText.Text = $"{ok} correctos \u00b7 {fail} errores \u00b7 {_results.Count} total";
        }

        /// <summary>
        /// Limpia los resultados y restablece la página a su estado inicial:
        /// quita la carpeta seleccionada, oculta las secciones y deshabilita el
        /// botón de inicio.
        /// </summary>
        private void ResetPageState()
        {
            // Limpiar también detiene lo que esté en curso: sin esto, un
            // archivo atascado seguía palpitando en "Analizando...".
            try { _cts?.Cancel(); } catch { }
            StopAllPreviews();
            _looseFiles = [];
            _results.Clear();
            _analysisResults.Clear();
            _analysisAll.Clear();

            FolderPathBox.Text = "";
            FileCountText.Text = "";
            FileCountText.Visibility = Visibility.Collapsed;

            AnalysisSection.Visibility = Visibility.Collapsed;
            PreviewSection.Visibility = Visibility.Collapsed;
            ProgressSection.Visibility = Visibility.Collapsed;
            ResultsSection.Visibility = Visibility.Collapsed;

            ProgressBar.Value = 0;
            ProcessingRing.IsActive = false;
            CompletedIcon.Visibility = Visibility.Collapsed;
            ClearButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;

            StartButton.IsEnabled = false;
            UpdateStartButtonText();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ResetPageState();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Durante el análisis, el botón actúa como "Cancelar": detiene el
            // escaneo en curso y resetea la página al estado inicial.
            if (_isAnalyzing)
            {
                _cts?.Cancel();
                return;
            }

            ResetPageState();
        }

        // ================================================================
        // PREVISUALIZADOR DE AUDIO (módulo compartido con Duplicados)
        // ================================================================

        /// <summary>Botón play de una fila del análisis: abre el mini reproductor.</summary>
        private async void PreviewRowButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string path }) return;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!AudioPreviewPlayer.IsSupportedAudio(path)) return;
            await BeginPreviewAsync(path);
        }

        /// <summary>
        /// Carga el archivo en el previsualizador (liberando el anterior para no
        /// dejar bloqueos), extrae la onda y reproduce. Un solo preview activo.
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

            StopPreviewCore(closeFile: true);

            PreviewSection.Visibility = Visibility.Visible;
            PreviewFileNameText.Text = Path.GetFileName(path);
            PreviewTimeText.Text = "Cargando...";
            PreviewWaveform.SetData(new WaveformData());
            UpdateTransportControls();

            bool loaded = await _previewPlayer.LoadAsync(path);
            if (!loaded)
            {
                PreviewTimeText.Text = "No se pudo leer el archivo.";
                PreviewPlayButton.IsEnabled = false;
                PreviewStopButton.IsEnabled = false;
                return;
            }

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

        private void PreviewClose_Click(object sender, RoutedEventArgs e)
        {
            StopPreviewCore(closeFile: true);
        }

        private void PreviewWaveform_SeekRequested(double fraction)
        {
            if (!_previewPlayer.IsLoaded) return;
            _previewPlayer.SeekToFraction(fraction);
        }

        private void PreviewTimer_Tick(object? sender, object e)
        {
            if (!_previewPlayer.IsLoaded) return;
            PreviewWaveform.SetPlayhead(_previewPlayer.PositionFraction);
            PreviewTimeText.Text = $"{FormatTs(_previewPlayer.Position)} / {FormatTs(_previewPlayer.Duration)}";
        }

        /// <summary>Fin natural de la reproducción: vuelve al inicio y deja "Reproducir".</summary>
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

        /// <summary>Detiene/libera el preview (y opcionalmente cierra el archivo) y oculta la tarjeta.</summary>
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

        private void StopAllPreviews() => StopPreviewCore(closeFile: true);

        /// <summary>Formatea una duración como m:ss (u h:mm:ss).</summary>
        private static string FormatTs(TimeSpan t)
        {
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }

        /// <summary>
        /// "x" de una fila del análisis: quita esa canción de la lista para que
        /// no se normalice. Sincroniza los conteos, el resumen y el botón
        /// Normalizar; si era la que sonaba, detiene el preview.
        /// </summary>
        private void RemoveAnalysisItem_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnalyzing || _isProcessing) return;
            if (sender is not FrameworkElement { Tag: string path }) return;
            if (string.IsNullOrEmpty(path)) return;

            var item = _analysisAll.FirstOrDefault(
                a => string.Equals(a.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;

            // Si era la canción en preview, detener y liberar el archivo.
            if (string.Equals(_previewPlayer.CurrentFilePath, path, StringComparison.OrdinalIgnoreCase))
                StopPreviewCore(closeFile: true);

            _analysisAll.Remove(item);
            _analysisResults.Remove(item);
            _looseFiles = _looseFiles
                .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            UpdateAnalysisAfterRemoval();
        }

        /// <summary>Recalcula conteos/resumen y el estado de Normalizar tras quitar una fila.</summary>
        private void UpdateAnalysisAfterRemoval()
        {
            var valid = _analysisAll.Where(a => a.Success).ToArray();
            int errors = _analysisAll.Count - valid.Length;

            if (_analysisAll.Count == 0)
            {
                AnalysisSummaryText.Text = "No hay archivos en la lista.";
            }
            else if (valid.Length > 0)
            {
                double min = valid.Min(a => a.PeakDb);
                double max = valid.Max(a => a.PeakDb);
                AnalysisSummaryText.Text = $"Rango: {min:F1} a {max:F1} dBFS \u00b7 {valid.Length} v\u00e1lidos"
                    + (errors > 0 ? $" \u00b7 {errors} con error" : "");
            }
            else
            {
                AnalysisSummaryText.Text = "No se pudieron analizar los archivos";
            }

            // En modo sueltas la casilla muestra el total acumulado restante.
            if (_looseFiles.Length > 0)
                FolderPathBox.Text = $"{_looseFiles.Length} canción(es) suelta(s)";

            FileCountText.Text = BuildFileCountText(_analysisAll.Count, _analysisAll.Count, 0);
            FileCountText.Visibility = Visibility.Visible;

            StartButton.IsEnabled = _analysisAll.Count > 0 && !_isProcessing;
            CancelButton.Visibility = _analysisAll.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

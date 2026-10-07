using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Remove_Top.Features.FormatConverter
{
    /// <summary>
    /// Página de Convertir Formatos: convierte OGG/MPEG (y otros formatos de voz)
    /// a WAV o MP3 320 kbps, siempre en estéreo 44.1 kHz, con reducción de
    /// ruido y mejora de voz opcionales. La salida queda en la subcarpeta
    /// <c>OneDj_Convertidos</c> junto a cada archivo de entrada.
    ///
    /// Secciones (práctica de la app, sin wizard): Origen, Formato de salida
    /// y Conversión (cola + preview + progreso). El origen acepta carpeta,
    /// archivos sueltos o arrastre nativo con overlay propio.
    /// </summary>
    public sealed partial class FormatConverterPage : Page
    {
        /// <summary>Cola visible de archivos a convertir (bindeo del ListView).</summary>
        private readonly ObservableCollection<ConverterItem> _queue = [];

        /// <summary>Servicio de conversión (decode → voz → encode).</summary>
        private readonly AudioConverter _converter = new();

        /// <summary>Cancelación del trabajo en curso (carga o conversión).</summary>
        private CancellationTokenSource? _cts;

        /// <summary>
        /// True cuando ya se completó una conversión en la cola actual: el
        /// previsualizador del original solo existe antes de convertir.
        /// Se limpia con Limpiar o al cargar una carpeta nueva.
        /// </summary>
        private bool _hasConverted;

        /// <summary>True mientras se convierte un lote.</summary>
        private bool _isWorking;

        /// <summary>True mientras se cargan archivos.</summary>
        private bool _isLoading;

        /// <summary>Retardo para ocultar el overlay al salir (evita parpadeos).</summary>
        private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(250);

        /// <summary>Timer anti-parpadeo del overlay de arrastre.</summary>
        private readonly DispatcherTimer _hideTimer;

        /// <summary>Previsualizador de audio (módulo compartido).</summary>
        private readonly AudioPreviewPlayer _previewPlayer = new();

        /// <summary>Timer que refresca el playhead y el reloj del preview.</summary>
        private readonly DispatcherTimer _previewTimer;

        /// <summary>Crea la página y monta textos, formato y timers.</summary>
        public FormatConverterPage()
        {
            InitializeComponent();
            QueueListView.ItemsSource = _queue;

            // Iconos de transporte del mini reproductor (inicializados aquí
            // para que Stop siempre sea visible, como en Normalización).
            PreviewPlayButton.Content = UiHelpers.Icon(Icon.Play, IconVariant.Regular, IconSize.Size16, foreground: PreviewPlayButton.Foreground);
            PreviewStopButton.Content = UiHelpers.Icon(Icon.Stop, IconVariant.Regular, IconSize.Size16, foreground: PreviewStopButton.Foreground);

            // Textos centralizados en AppLimits (módulo de límites).
            PageTitleText.Text = AppLimits.ConverterPageTitle;
            PageSubtitleText.Text = AppLimits.ConverterPageSubtitle;
            BrandText.Text = AppLimits.AppName;
            FreeBadgeText.Text = AppLimits.FreeBadgeText;
            LimitInfoText.Text = AppLimits.ConverterLimitMessage;

            // Menú de formato de salida (mismo patrón que Intensidad en Normalización).
            PopulateFormatOptions();

            // Anti-parpadeo del overlay de arrastre.
            _hideTimer = new DispatcherTimer { Interval = HideDelay };
            _hideTimer.Tick += HideTimer_Tick;

            // Previsualizador: timer de playhead + fin de reproducción + scrub.
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _previewTimer.Tick += PreviewTimer_Tick;
            _previewPlayer.PlaybackEnded += PreviewPlayer_PlaybackEnded;
            PreviewWaveform.SeekRequested += PreviewWaveform_SeekRequested;
        }

        // ================================================================
        // Formato de salida (menú desplegable)
        // ================================================================

        /// <summary>
        /// Llena el menú de formato con las dos salidas disponibles.
        /// Por defecto se elige MP3 320 kbps (recomendado para voz).
        /// </summary>
        private void PopulateFormatOptions()
        {
            FormatComboBox.Items.Clear();
            FormatComboBox.Items.Add(new ComboBoxItem
            {
                Content = "MP3 · 320 kbps (recomendado)",
                Tag = ConverterFormat.Mp3_320
            });
            FormatComboBox.Items.Add(new ComboBoxItem
            {
                Content = "WAV · 16-bit / 44.1 kHz",
                Tag = ConverterFormat.Wav
            });
            FormatComboBox.SelectedIndex = 0;
        }

        /// <summary>Devuelve el formato elegido en el menú desplegable.</summary>
        private ConverterFormat GetSelectedFormat()
        {
            if (FormatComboBox.SelectedItem is ComboBoxItem item && item.Tag is ConverterFormat format)
                return format;
            return ConverterFormat.Mp3_320;
        }

        // ================================================================
        // Origen: carpeta, archivos sueltos o arrastre
        // ================================================================

        /// <summary>Botón "Carpeta...": escanea la carpeta (reemplaza la cola).</summary>
        private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isWorking || _isLoading) return;
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

        /// <summary>Botón "Archivos...": suma canciones sueltas a la cola.</summary>
        private async void BrowseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isWorking || _isLoading) return;
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var ext in new[] { ".ogg", ".oga", ".opus", ".mp3", ".wav", ".flac", ".aac", ".m4a", ".mp4", ".wma", ".aiff", ".aif", ".wv", ".mpeg", ".mpg" })
                picker.FileTypeFilter.Add(ext);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
                AddPaths(files.Select(f => f.Path).ToList(), replace: false);
        }

        /// <summary>
        /// Carga una carpeta: enumera en segundo plano y reemplaza la cola.
        /// El AddPaths posterior corre en el hilo UI (la cola es observable).
        /// </summary>
        private async Task LoadFolderAsync(string folderPath)
        {
            var (files, total) = await Task.Run(() =>
            {
                var list = AudioConverter.GetSupportedFiles(folderPath, out int found);
                return (list, found);
            });
            AddPaths(files, replace: true, folderPath: folderPath, totalFound: total);
        }

        /// <summary>
        /// Añade rutas a la cola (deduplicadas, tope 50). Sin sonda previa:
        /// todo formato soportado entra "En cola" y, si algo falla, se informa
        /// al convertir con su motivo (así Opus-en-OGG y demás casos que solo
        /// ffmpeg lee no quedan bloqueados como "No legible").
        /// Con <paramref name="replace"/> la cola se reemplaza.
        /// </summary>
        private void AddPaths(List<string> paths, bool replace, string folderPath = "", int totalFound = -1)
        {
            if (replace)
            {
                StopPreviewCore(closeFile: true);
                // Cola nueva: el preview vuelve a estar disponible.
                _hasConverted = false;
                _queue.Clear();
            }

            // Solo formatos soportados, sin repetir los que ya están en cola.
            var supported = paths
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p) && AudioConverter.IsSupportedFile(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => !_queue.Any(q => string.Equals(q.FilePath, p, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Tope de la versión gratuita (AppLimits): el resto se omite.
            int room = AudioConverter.MaxFilesToScan - _queue.Count;
            bool truncated = false;
            if (supported.Count > room)
            {
                supported = supported.Take(Math.Max(0, room)).ToList();
                truncated = true;
            }

            _isLoading = true;
            SetOriginEnabled(false);
            UpdateQueueState();

            try
            {
                foreach (var path in supported)
                {
                    _queue.Add(new ConverterItem
                    {
                        FilePath = path,
                        FileName = Path.GetFileName(path),
                        Status = "En cola",
                        Detail = "Listo para convertir"
                    });
                }
            }
            finally
            {
                _isLoading = false;
                SetOriginEnabled(true);
            }

            // Contador de origen (carpeta o sueltas acumuladas).
            if (!string.IsNullOrEmpty(folderPath))
            {
                FileCountText.Text = totalFound >= 0 && totalFound > _queue.Count
                    ? $"{_queue.Count} archivo(s) de {totalFound} en la carpeta (límite {AudioConverter.MaxFilesToScan})"
                    : $"{_queue.Count} archivo(s) en la carpeta";
                FileCountText.Visibility = Visibility.Visible;
            }
            else if (_queue.Count > 0)
            {
                FileCountText.Text = $"{_queue.Count} canción(es) suelta(s)"
                    + (truncated ? $" (límite {AudioConverter.MaxFilesToScan})" : "");
                FileCountText.Visibility = Visibility.Visible;
            }

            UpdateQueueState();
        }

        /// <summary>
        /// Botón "Limpiar" (siempre visible): cancela lo en curso y resetea la
        /// página al estado inicial (ruta, cola, progreso, preview, resumen).
        /// </summary>
        private void CleanButton_Click(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
            ResetPage();
        }

        /// <summary>Devuelve la página al estado inicial (sin tocar el disco).</summary>
        private void ResetPage()
        {
            StopPreviewCore(closeFile: true);
            _isLoading = false;
            _hasConverted = false;
            _queue.Clear();
            FileCountText.Text = "";
            FileCountText.Visibility = Visibility.Collapsed;
            QueueSummaryText.Text = "Sin archivos. Carga una carpeta o canciones para empezar.";
            ProgressSection.Visibility = Visibility.Collapsed;
            ProgressBar.Value = 0;
            ProgressText.Text = "";
            // Estado final limpio: sin loader ni icono de completado.
            WorkingRing.IsActive = false;
            ProgressTitleText.Text = "Convirtiendo...";
            CompletedIcon.Visibility = Visibility.Collapsed;
            ResultSummaryText.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            SetOriginEnabled(true);
            UpdateQueueState();
        }

        /// <summary>Habilita/deshabilita los botones de origen.</summary>
        private void SetOriginEnabled(bool enabled)
        {
            BrowseFolderButton.IsEnabled = enabled;
            BrowseFilesButton.IsEnabled = enabled;
        }

        // ================================================================
        // Arrastre nativo (overlay local, sin DropTargetControl)
        // ================================================================

        /// <summary>Muestra el overlay al entrar con archivos arrastrados.</summary>
        private void Root_DragEnter(object sender, DragEventArgs e)
        {
            if (_isWorking || _isLoading) return;
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                _hideTimer.Stop();
                Overlay.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Mantiene el overlay visible mientras se arrastra encima.</summary>
        private void Root_DragOver(object sender, DragEventArgs e)
        {
            if (_isWorking || _isLoading) return;
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                _hideTimer.Stop();
                Overlay.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Oculta el overlay con retardo anti-parpadeo al salir.</summary>
        private void Root_DragLeave(object sender, DragEventArgs e)
        {
            _hideTimer.Start();
        }

        /// <summary>Tick del anti-parpadeo: oculta el overlay.</summary>
        private void HideTimer_Tick(object? sender, object e)
        {
            _hideTimer.Stop();
            Overlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Suelta: una sola carpeta reemplaza la cola (camino manual idéntico);
        /// canciones sueltas se acumulan hasta 50. Durante el trabajo se ignora.
        /// </summary>
        private async void Root_Drop(object sender, DragEventArgs e)
        {
            _hideTimer.Stop();
            Overlay.Visibility = Visibility.Collapsed;
            if (_isWorking || _isLoading) return;

            IReadOnlyList<IStorageItem>? items = null;
            try
            {
                items = await e.DataView.GetStorageItemsAsync();
            }
            catch (Exception ex)
            {
                QueueSummaryText.Text = $"No se pudo leer lo arrastrado: {ex.Message}";
                return;
            }
            if (items == null || items.Count == 0) return;

            if (items.Count == 1 && items[0] is StorageFolder folder)
            {
                await LoadFolderAsync(folder.Path);
                return;
            }

            var files = items.OfType<StorageFile>().Select(f => f.Path).ToList();
            if (files.Count == 0)
            {
                QueueSummaryText.Text = "Arrastra una carpeta o canciones de audio.";
                return;
            }
            AddPaths(files, replace: false);
        }

        // ================================================================
        // Conversión del lote
        // ================================================================

        /// <summary>
        /// Botón "Convertir": procesa las filas "En cola" con el formato y las
        /// mejoras elegidas, con progreso en vivo y resumen al terminar.
        /// </summary>
        private async void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isWorking) return;
            var pending = _queue.Where(q => q.Status == "En cola").ToList();
            if (pending.Count == 0) return;

            _isWorking = true;
            _cts = new CancellationTokenSource();
            SetOriginEnabled(false);
            ConvertButton.IsEnabled = false;
            CancelButton.Visibility = Visibility.Visible;
            StopPreviewCore(closeFile: true);
            ProgressSection.Visibility = Visibility.Visible;
            ResultSummaryText.Visibility = Visibility.Collapsed;
            // Loader activo y sin marca de completado mientras trabaja.
            WorkingRing.IsActive = true;
            ProgressTitleText.Text = "Convirtiendo...";
            CompletedIcon.Visibility = Visibility.Collapsed;

            // Opciones desde la sección "Formato de salida".
            var options = new ConverterOptions
            {
                Format = GetSelectedFormat(),
                RemoveNoise = RemoveNoiseCheck.IsChecked != false,
                EnhanceVoice = EnhanceVoiceCheck.IsChecked != false
            };

            string formatText = options.Format == ConverterFormat.Wav ? "WAV" : "MP3 320";
            var progress = new Progress<ConverterProgress>(p =>
            {
                ProgressBar.Value = p.Percentage;
                ProgressText.Text = $"{p.CurrentIndex}/{p.TotalCount} · {p.CurrentFile}";
                if (p.Result != null)
                {
                    var item = _queue.FirstOrDefault(q =>
                        string.Equals(q.FilePath, p.Result.InputPath, StringComparison.OrdinalIgnoreCase));
                    if (item != null)
                    {
                        item.Status = p.Result.Success ? "Listo" : "Error";
                        item.Detail = p.Result.Message;
                        item.Success = p.Result.Success;
                        item.OutputPath = p.Result.OutputPath;
                    }
                }
            });

            try
            {
                await _converter.ConvertAsync(
                    pending.Select(q => q.FilePath).ToList(),
                    options,
                    progress,
                    _cts.Token).ConfigureAwait(false);

                DispatcherQueue.TryEnqueue(() =>
                {
                    int ok = _queue.Count(q => q.Status == "Listo");
                    int errors = _queue.Count(q => q.Status == "Error");
                    // Conversión terminada: se cierra el preview del original
                    // (solo existía antes de convertir) y queda el ítem convertido.
                    StopPreviewCore(closeFile: true);
                    _hasConverted = ok > 0;
                    // Estado final "Completado" con icono (verde todo bien,
                    // ámbar si hubo errores), como en Normalización.
                    WorkingRing.IsActive = false;
                    if (errors == 0)
                    {
                        ProgressTitleText.Text = "Completado";
                        CompletedIcon.Icon = Icon.CheckmarkCircle;
                        CompletedIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.Green);
                        ResultSummaryText.Text = $"{ok} archivo(s) a {formatText} en OneDj_Convertidos. Pico y LUFS medidos en cada fila.";
                    }
                    else
                    {
                        ProgressTitleText.Text = "Completado con errores";
                        CompletedIcon.Icon = Icon.Warning;
                        CompletedIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.DarkOrange);
                        ResultSummaryText.Text = $"{ok} listo(s) · {errors} con error. Revisa el detalle de cada fila.";
                    }
                    CompletedIcon.Visibility = Visibility.Visible;
                    ResultSummaryText.Visibility = Visibility.Visible;
                });
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    WorkingRing.IsActive = false;
                    ProgressTitleText.Text = "Cancelado";
                    ResultSummaryText.Text = "Conversión cancelada.";
                    ResultSummaryText.Visibility = Visibility.Visible;
                });
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ResultSummaryText.Text = $"Error: {ex.Message}";
                    ResultSummaryText.Visibility = Visibility.Visible;
                });
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _isWorking = false;
                    SetOriginEnabled(true);
                    CancelButton.Visibility = Visibility.Collapsed;
                    UpdateQueueState();
                });
            }
        }

        /// <summary>Botón "Cancelar": detiene la conversión en curso.</summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
        }

        /// <summary>
        /// "x" de una fila: la quita de la cola sin tocar el disco. Si era la
        /// que sonaba, detiene el preview. No disponible durante el trabajo.
        /// </summary>
        private void RemoveQueueItem_Click(object sender, RoutedEventArgs e)
        {
            if (_isWorking || _isLoading) return;
            if (sender is not FrameworkElement { Tag: string path }) return;
            var item = _queue.FirstOrDefault(q =>
                string.Equals(q.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;
            if (string.Equals(_previewPlayer.CurrentFilePath, path, StringComparison.OrdinalIgnoreCase))
                StopPreviewCore(closeFile: true);
            _queue.Remove(item);
            UpdateQueueState();
        }

        /// <summary>
        /// Recalcula el resumen de la cola y el estado de Convertir. Mientras
        /// se cargan archivos (_isLoading) Convertir queda deshabilitado para
        /// que ninguna fila se quede sin procesar.
        /// </summary>
        private void UpdateQueueState()
        {
            int ready = _queue.Count(q => q.Status == "En cola");
            int checking = _queue.Count(q => q.Status == "Revisando…");
            int bad = _queue.Count(q => q.Status is "No legible" or "Error");
            QueueSummaryText.Text = _queue.Count == 0
                ? "Sin archivos. Carga una carpeta o canciones para empezar."
                : $"{ready} en cola"
                    + (checking > 0 ? $" · {checking} revisando" : "")
                    + (bad > 0 ? $" · {bad} omitido(s)" : "")
                    + $" (máx. {AudioConverter.MaxFilesToScan})";
            ConvertButton.IsEnabled = ready > 0 && !_isWorking && !_isLoading;
            UpdateCleanButtonVisibility();
        }

        /// <summary>
        /// "Limpiar" solo aparece si hay algo que limpiar: archivos en cola o
        /// un resumen de resultados visible. Con la página vacía se oculta.
        /// </summary>
        private void UpdateCleanButtonVisibility()
        {
            CleanButton.Visibility = _queue.Count > 0 || ResultSummaryText.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // ================================================================
        // Preview del original (módulo compartido con el resto de la app)
        // ================================================================

        /// <summary>Botón play de una fila: abre el mini reproductor.</summary>
        private async void PreviewRowButton_Click(object sender, RoutedEventArgs e)
        {
            // El preview del original solo existe antes de convertir: tras la
            // conversión solo queda visible el ítem convertido.
            if (_hasConverted) return;
            if (sender is not FrameworkElement { Tag: string path }) return;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!AudioPreviewPlayer.IsSupportedAudio(path)) return;
            await BeginPreviewAsync(path);
        }

        /// <summary>
        /// Carga el archivo en el previsualizador (liberando el anterior),
        /// extrae la onda y reproduce. Un solo preview activo a la vez.
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

        /// <summary>Actualiza los botones de transporte según el estado.</summary>
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

        /// <summary>Botón Play/Pausa del mini reproductor.</summary>
        private void PreviewPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (!_previewPlayer.IsLoaded) return;
            _previewPlayer.TogglePlay();
            UpdateTransportControls();
        }

        /// <summary>Botón Stop: detiene y vuelve al inicio.</summary>
        private void PreviewStop_Click(object sender, RoutedEventArgs e)
        {
            if (!_previewPlayer.IsLoaded) return;
            _previewPlayer.Stop();
            PreviewWaveform.SetPlayhead(0);
            PreviewTimeText.Text = $"0:00 / {FormatTs(_previewPlayer.Duration)}";
            UpdateTransportControls();
        }

        /// <summary>Cierra la tarjeta del mini reproductor.</summary>
        private void PreviewClose_Click(object sender, RoutedEventArgs e)
        {
            StopPreviewCore(closeFile: true);
        }

        /// <summary>Scrub en la onda: salta a la fracción indicada.</summary>
        private void PreviewWaveform_SeekRequested(double fraction)
        {
            if (!_previewPlayer.IsLoaded) return;
            _previewPlayer.SeekToFraction(fraction);
        }

        /// <summary>Tick del timer: refresca playhead y reloj.</summary>
        private void PreviewTimer_Tick(object? sender, object e)
        {
            if (!_previewPlayer.IsLoaded) return;
            PreviewWaveform.SetPlayhead(_previewPlayer.PositionFraction);
            PreviewTimeText.Text = $"{FormatTs(_previewPlayer.Position)} / {FormatTs(_previewPlayer.Duration)}";
        }

        /// <summary>Fin natural de la reproducción: vuelve al inicio.</summary>
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

        /// <summary>Detiene/libera el preview y oculta la tarjeta.</summary>
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

        /// <summary>Al salir de la página: cancela lo en curso y libera el audio.</summary>
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
            StopPreviewCore(closeFile: true);
        }
    }
}

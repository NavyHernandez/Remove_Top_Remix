using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Remove_Top.Features.Normalization;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Remove_Top.Features.Downloader
{
    /// <summary>
    /// Página de Descarga: toma enlaces de YouTube, baja el mejor audio (Opus) y,
    /// opcionalmente, lo masteriza reutilizando <see cref="AudioNormalizer"/>.
    /// El motor (Python + yt-dlp + deno + ffmpeg) se aprovisiona on-demand.
    /// </summary>
    public sealed partial class DownloaderPage : Page
    {
        private readonly ObservableCollection<DownloadItem> _items = [];
        private readonly ObservableCollection<NormalizationResult> _masterResults = [];

        private CancellationTokenSource? _cts;
        private bool _isBusy;

        private readonly YtDlpService _service = new();

        /// <summary>Sesión de YouTube rechazada en esta tanda (muestra "caducada" al terminar).</summary>
        private bool _cookiesRejectedThisRun;

        /// <summary>Algún fallo de la tanda es autenticable (sugiere conectar cuenta).</summary>
        private bool _authFixableFailure;

        /// <summary>Carpeta de los archivos finales de la última tanda (para "Abrir ubicación").</summary>
        private string _lastOutputFolder = "";
        private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(250);
        private readonly DispatcherTimer _hideTimer;

        public DownloaderPage()
        {
            InitializeComponent();
            ItemsListView.ItemsSource = _items;
            ResultsListView.ItemsSource = _masterResults;

            AddButton.Content = UiHelpers.Content(Icon.Add, "Agregar", foreground: AddButton.Foreground);
            BrowseButton.Content = UiHelpers.Content(Icon.FolderOpen, "Examinar...", foreground: BrowseButton.Foreground);
            ClearButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: ClearButton.Foreground);
            OpenFolderButton.Content = UiHelpers.Content(Icon.FolderOpen, "Abrir ubicación", semibold: false, foreground: OpenFolderButton.Foreground);

            PageTitleText.Text = AppLimits.DownloaderPageTitle;
            PageSubtitleText.Text = AppLimits.DownloaderPageSubtitle;
            BrandText.Text = AppLimits.AppName;
            DownloaderFreeBadge.Text = AppLimits.FreeBadgeText;
            DownloaderLimitText.Text = AppLimits.DownloaderLimitMessage;
            AccountTitleText.Text = AppLimits.YouTubeAccountTitle;
            AccountNoteText.Text = AppLimits.YouTubeAccountNote;

            // Sin Runtime de WebView2 no hay login embebido: se oculta la tarjeta.
            if (!YouTubeSession.IsRuntimeAvailable())
            {
                AccountSection.Visibility = Visibility.Collapsed;
                App.Log("DownloaderPage.Account", "WebView2 Runtime no disponible: tarjeta de cuenta oculta.");
            }
            RefreshAccountState();

            UpdateStartButtonText();

            // Se engancha aquí (y no en XAML) para evitar que el evento se dispare
            // durante InitializeComponent, antes de que existan los demás controles.
            MasterizeCheckBox.Checked += Masterize_Toggled;
            MasterizeCheckBox.Unchecked += Masterize_Toggled;

            FolderPathBox.Text = GetDefaultDestinationFolder();

            _hideTimer = new DispatcherTimer { Interval = HideDelay };
            _hideTimer.Tick += HideTimer_Tick;
        }

        private static string GetDefaultDestinationFolder()
        {
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrEmpty(music))
                music = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return music;
        }

        // ==================================================================
        // ENTRADA DE ENLACES
        // ==================================================================

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddUrlsFromBox();

        private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
            => AddButton.IsEnabled = !_isBusy && !string.IsNullOrWhiteSpace(UrlBox.Text);

        /// <summary>Valida las líneas del cuadro y las agrega a la cola (marca las inválidas).</summary>
        private void AddUrlsFromBox()
        {
            var lines = YouTubeUrlParser.SplitLines(UrlBox.Text).ToList();
            if (lines.Count == 0)
                return;

            int room = AppLimits.DownloaderMaxLinksPerBatch - _items.Count;
            if (room <= 0)
            {
                UrlBox.Text = "";
                UpdateListState();
                SetInfo(InfoBarSeverity.Warning, "Límite alcanzado", AppLimits.DownloaderLimitMessage);
                return;
            }

            int added = 0;
            int addedInvalid = 0;
            int skippedByLimit = 0;
            foreach (var line in lines)
            {
                if (YouTubeUrlParser.TryNormalize(line, out var url))
                {
                    if (_items.Any(i => string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (_items.Count >= AppLimits.DownloaderMaxLinksPerBatch)
                    {
                        skippedByLimit++;
                        continue;
                    }
                    _items.Add(new DownloadItem(url)
                    {
                        Status = DownloadStatus.Pending,
                        Message = "En espera"
                    });
                    added++;
                }
                else
                {
                    if (_items.Count >= AppLimits.DownloaderMaxLinksPerBatch)
                    {
                        skippedByLimit++;
                        continue;
                    }
                    _items.Add(new DownloadItem(line)
                    {
                        Status = DownloadStatus.Invalid,
                        Message = "Enlace no reconocido"
                    });
                    addedInvalid++;
                }
            }

            UrlBox.Text = "";
            UpdateListState();

            if (skippedByLimit > 0)
                SetInfo(InfoBarSeverity.Warning, "Límite alcanzado",
                    $"Se agregaron {added + addedInvalid} enlace(s). {AppLimits.DownloaderLimitMessage}");
            else if (added == 0 && lines.Count > 0)
                SetInfo(InfoBarSeverity.Warning, "Sin enlaces nuevos", "No se agregó ningún enlace válido (revisa que sean de YouTube).");
            else
                ResetInfo();
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            if ((sender as FrameworkElement)?.Tag is DownloadItem item)
            {
                _items.Remove(item);
                UpdateListState();
            }
        }

        private void UpdateListState()
        {
            ListSection.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ListCountText.Text = $"{_items.Count}/{AppLimits.DownloaderMaxLinksPerBatch} enlace(s)";
            var hasValid = _items.Any(i => i.Status != DownloadStatus.Invalid);
            StartButton.IsEnabled = !_isBusy && hasValid && !string.IsNullOrWhiteSpace(FolderPathBox.Text);
            UpdateStartButtonText();
        }

        // ==================================================================
        // CARPETA DESTINO
        // ==================================================================

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
                UpdateListState();
            }
        }

        // ==================================================================
        // NORMALIZAR AL DESCARGAR (check superior)
        // Al normalizar se usa el perfil fijo HardLimiter a -1.0 dBFS.
        // ==================================================================

        private void Masterize_Toggled(object sender, RoutedEventArgs e)
        {
            // Sin normalización no hay intermedio que borrar: el check no aplica.
            KeepOriginalCheckBox.IsEnabled = MasterizeCheckBox.IsChecked == true;
            UpdateStartButtonText();
        }

        private void UpdateStartButtonText()
        {
            if (_isBusy || StartButton == null) return;
            var text = MasterizeCheckBox.IsChecked == true ? "Descargar y normalizar" : "Descargar audio";
            StartButton.Content = UiHelpers.Content(Icon.ArrowDownload, text, foreground: StartButton.Foreground);
        }

        // ==================================================================
        // CUENTA DE YOUTUBE (opcional)
        // Si hay sesión vigente, las descargas la usan automáticamente.
        // ==================================================================

        private void RefreshAccountState()
        {
            if (AccountSection.Visibility == Visibility.Collapsed)
                return;

            if (YouTubeSession.HasFreshCookies() && !_cookiesRejectedThisRun)
            {
                AccountStatusText.Text = AppLimits.YouTubeAccountConnected;
                AccountButton.Content = UiHelpers.Content(Icon.PersonAdd, AppLimits.YouTubeAccountDisconnect, foreground: AccountButton.Foreground);
            }
            else
            {
                AccountStatusText.Text = File.Exists(YouTubeSession.CookiesPath)
                    ? AppLimits.YouTubeAccountExpired
                    : AppLimits.YouTubeAccountDisconnected;
                AccountButton.Content = UiHelpers.Content(Icon.PersonAdd, AppLimits.YouTubeAccountConnect, foreground: AccountButton.Foreground);
            }
        }

        private async void AccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
                return;

            if (YouTubeSession.HasFreshCookies())
            {
                YouTubeSession.Clear();
                RefreshAccountState();
                return;
            }

            var dialog = new YouTubeLoginDialog { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
            if (dialog.Connected)
                _cookiesRejectedThisRun = false;
            RefreshAccountState();
        }

        /// <summary>
        /// Detecta si un error crudo es autenticable (edad/miembros/login/bot):
        /// de esos, una cuenta conectada puede rescatar la descarga.
        /// </summary>
        private static bool IsAuthFixableRaw(string raw)
        {
            var m = raw ?? "";
            return m.Contains("age", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("members-only", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("login required", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("sign in to confirm", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("not a bot", StringComparison.OrdinalIgnoreCase);
        }

        // ==================================================================
        // FLUJO PRINCIPAL
        // ==================================================================

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                _cts?.Cancel();
                return;
            }

            var pending = _items.Where(i => i.Status != DownloadStatus.Invalid)
                .Take(AppLimits.DownloaderMaxLinksPerBatch).ToList();
            if (pending.Count == 0)
                return;

            var folder = FolderPathBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(folder))
            {
                SetInfo(InfoBarSeverity.Warning, "Falta la carpeta de destino", "Elige dónde guardar los archivos.");
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                SetInfo(InfoBarSeverity.Error, "Carpeta no válida", DownloadErrorText.Friendly(ex.Message));
                return;
            }

            // La normalización usa el perfil fijo Hard Limiter a -1.0 dBFS
            // (constantes en RunFlowAsync, que ejecuta el flujo compartido).
            _masterResults.Clear();
            await RunFlowAsync(pending, folder);
        }

        /// <summary>
        /// "Intentar de nuevo" del popup de errores: re-ejecuta el mismo flujo
        /// solo con los enlaces que quedaron en estado Error.
        /// </summary>
        private async void ErrorPopupRetryButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorPopupOverlay.Visibility = Visibility.Collapsed;
            if (_isBusy)
                return;

            var failed = _items.Where(i => i.Status == DownloadStatus.Error).ToList();
            if (failed.Count == 0)
                return;

            var folder = FolderPathBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                SetInfo(InfoBarSeverity.Warning, "Falta la carpeta de destino", "Elige dónde guardar los archivos.");
                return;
            }

            await RunFlowAsync(failed, folder);
        }

        private void ErrorPopupCloseButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorPopupOverlay.Visibility = Visibility.Collapsed;
        }

        private void ErrorPopupOverlay_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ErrorPopupOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Ejecuta el flujo completo (motor → descarga → normalización opcional)
        /// sobre la lista indicada. Lo comparten "Descargar y normalizar" y
        /// "Intentar de nuevo" del popup de errores.
        /// </summary>
        private async Task RunFlowAsync(List<DownloadItem> pending, string folder)
        {
            // La normalización usa el perfil fijo Hard Limiter a -1.0 dBFS.
            const double targetDb = -1.0;
            const MasteringIntensity intensity = MasteringIntensity.HardLimiter;
            var masterize = MasterizeCheckBox.IsChecked == true;

            // Sesión de YouTube vigente: se usa automáticamente en cada descarga.
            string? cookiesPath = YouTubeSession.HasFreshCookies() ? YouTubeSession.CookiesPath : null;
            _cookiesRejectedThisRun = false;
            _authFixableFailure = false;

            foreach (var it in pending)
            {
                it.Status = DownloadStatus.Pending;
                it.Percentage = 0;
                it.Message = "En espera";
            }

            _isBusy = true;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            EndActionsSection.Visibility = Visibility.Collapsed;
            _lastOutputFolder = "";
            AddButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;
            UrlBox.IsEnabled = false;
            MasterizeCheckBox.IsEnabled = false;
            KeepOriginalCheckBox.IsEnabled = false;
            ProgressSection.Visibility = Visibility.Visible;
            ResultsSection.Visibility = Visibility.Collapsed;
            ProcessingRing.IsActive = true;
            CompletedIcon.Visibility = Visibility.Collapsed;
            ProgressBar.Value = 0;
            ProgressTitleText.Text = "Preparando...";
            ProgressText.Text = "Comprobando el motor de descarga...";
            ResetInfo();

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try
            {
                // ---- FASE 0: motor de descarga (Python + yt-dlp + deno + ffmpeg)
                var toolProgress = new Progress<ToolProgress>(p =>
                {
                    ProgressBar.Value = p.Percentage;
                    ProgressText.Text = p.Status;
                });
                await new ToolManager().EnsureToolsAsync(toolProgress, ct);

                if (!ToolManager.IsPythonReady)
                {
                    ProgressTitleText.Text = "Error";
                    ProgressText.Text = "Motor de descarga no disponible.";
                    ProcessingRing.IsActive = false;
                    CompletedIcon.Icon = Icon.Warning;
                    CompletedIcon.Visibility = Visibility.Visible;
                    ShowEndActions();
                    ShowErrorPopup("No se pudo preparar el motor de descarga. Revisa tu conexi\u00f3n a internet e int\u00e9ntalo de nuevo.");
                    return;
                }

                var ffmpegPath = ToolManager.FfmpegExe;
                var useFfmpeg = !string.IsNullOrEmpty(ffmpegPath);

                // ---- FASE 1: descarga secuencial
                ProgressTitleText.Text = "Descargando...";
                var downloaded = new List<(DownloadItem Item, string Path)>();

                for (int i = 0; i < pending.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = pending[i];
                    int index = i + 1;

                    item.Status = DownloadStatus.Downloading;
                    item.Message = "Descargando...";
                    ProgressText.Text = $"Descargando {index}/{pending.Count}: {item.DisplayName}";

                    double basePercent = (index - 1) * 100.0 / pending.Count;
                    double span = 100.0 / pending.Count;
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        if (!string.IsNullOrEmpty(p.Title))
                            item.Title = p.Title;
                        item.Percentage = p.Percentage;
                        item.Message = p.Message;
                        ProgressBar.Value = basePercent + span * p.Percentage / 100.0;
                        ProgressText.Text = $"Descargando {index}/{pending.Count}: {item.DisplayName} ({p.Percentage:F0}%)";
                    });

                    DownloadResult result;
                    var attemptStartedAt = DateTime.UtcNow;
                    try
                    {
                        result = await _service.DownloadAsync(item.Url, folder, useFfmpeg, ffmpegPath, progress, ct, cookiesPath);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result = new DownloadResult { Success = false, Message = $"ERROR: {ex.Message}" };
                    }

                    // yt-dlp reportó éxito pero la ruta puede no resolverse
                    // (tildes/emojis del título): se revalida con búsqueda tolerante
                    // antes de declarar el fallo.
                    string? resolvedPath = null;
                    if (result.Success && !string.IsNullOrEmpty(result.OutputPath))
                    {
                        resolvedPath = File.Exists(result.OutputPath)
                            ? result.OutputPath
                            : YtDlpService.FindNewestOutput(folder, attemptStartedAt.AddMinutes(-1));
                    }

                    if (resolvedPath != null)
                    {
                        if (!string.IsNullOrEmpty(result.Title))
                            item.Title = result.Title;
                        item.Status = DownloadStatus.Downloaded;
                        item.Percentage = 100;
                        item.Message = result.Message;
                        downloaded.Add((item, resolvedPath));
                        App.Log("DownloaderPage.Download", item.Url + " :: OK: " + resolvedPath);
                    }
                    else if (result.Success)
                    {
                        // El motor terminó bien pero el archivo no se localizó:
                        // mensaje específico (no el genérico) + diagnóstico en el log.
                        App.Log("DownloaderPage.Download", item.Url + " :: OUTPUT-NOT-FOUND: " + (result.OutputPath ?? ""));
                        item.Status = DownloadStatus.Error;
                        item.Percentage = 0;
                        item.Message = DownloadErrorText.Friendly("ERROR: output-not-found");
                    }
                    else
                    {
                        // El detalle crudo va al log; en la fila solo el texto simple.
                        App.Log("DownloaderPage.Download", item.Url + " :: " + result.Message);
                        item.Status = DownloadStatus.Error;
                        item.Percentage = 0;
                        item.Message = DownloadErrorText.Friendly(result.Message);
                        if (result.CookiesRejected)
                            _cookiesRejectedThisRun = true;
                        if (IsAuthFixableRaw(result.Message))
                            _authFixableFailure = true;
                    }
                }

                // ---- FASE 2: masterización (opcional)
                // Carpeta de los archivos finales (para "Abrir ubicación").
                if (downloaded.Count > 0)
                    _lastOutputFolder = folder;
                if (masterize && downloaded.Count > 0)
                {
                    ProgressTitleText.Text = "Normalizando...";
                    ProgressBar.Value = 0;
                    ResultsSection.Visibility = Visibility.Visible;

                    foreach (var d in downloaded)
                    {
                        d.Item.Status = DownloadStatus.Processing;
                        d.Item.Percentage = 0;
                        d.Item.Message = "Normalizando...";
                    }

                    var normalizer = new AudioNormalizer();
                    var files = downloaded.Select(d => d.Path).ToArray();

                    var masterProgress = new Progress<NormalizationProgress>(p =>
                    {
                        ProgressBar.Value = p.Percentage;
                        ProgressText.Text = $"Normalizando {p.CurrentIndex}/{p.TotalCount}: {p.CurrentFile}";

                        if (p.Result != null)
                        {
                            _masterResults.Add(p.Result);
                            ResultsListView.ScrollIntoView(p.Result);
                            UpdateSummary();

                            var match = downloaded.FirstOrDefault(d => string.Equals(
                                Path.GetFileNameWithoutExtension(d.Path),
                                Path.GetFileNameWithoutExtension(p.Result.FileName),
                                StringComparison.OrdinalIgnoreCase));
                            if (match.Item != null)
                            {
                                match.Item.Status = p.Result.Success ? DownloadStatus.Done : DownloadStatus.Error;
                                match.Item.Percentage = 100;
                                match.Item.Message = p.Result.Success ? "Descargado y normalizado" : "Descargado · error al normalizar";
                            }
                        }
                    });

                    await normalizer.ProcessFilesAsync(files, targetDb, intensity, masterProgress, ct);

                    // Corrige tildes en los nombres de salida y refresca la lista.
                    AudioNormalizer.CorrectOutputNames(_masterResults);
                    var corrected = _masterResults.ToList();
                    _masterResults.Clear();
                    foreach (var r in corrected)
                        _masterResults.Add(r);
                    UpdateSummary();

                    foreach (var d in downloaded)
                    {
                        if (d.Item.Status == DownloadStatus.Processing)
                        {
                            d.Item.Status = DownloadStatus.Done;
                            d.Item.Percentage = 100;
                            d.Item.Message = "Descargado y normalizado";
                        }
                    }

                    // Solo queda la canción normalizada: se borra el WAV descargado
                    // para no confundir al usuario (solo si se masterizó bien y
                    // el usuario no pidió conservar el original).
                    ProgressText.Text = "Limpiando archivos temporales...";
                    bool keepOriginal = KeepOriginalCheckBox.IsChecked == true;
                    foreach (var d in downloaded)
                    {
                        var name = Path.GetFileNameWithoutExtension(d.Path);
                        var res = corrected.FirstOrDefault(r => string.Equals(
                            Path.GetFileNameWithoutExtension(r.FileName), name,
                            StringComparison.OrdinalIgnoreCase));
                        if (res != null && res.Success && !keepOriginal)
                        {
                            try { File.Delete(d.Path); }
                            catch (Exception ex) { App.Log("DownloaderPage.Cleanup", ex.Message); }
                        }
                    }
                }
                else
                {
                    foreach (var d in downloaded)
                    {
                        d.Item.Status = DownloadStatus.Done;
                        d.Item.Percentage = 100;
                        d.Item.Message = "Descargado";
                    }
                }

                // ---- FASE 3: cierre
                // Si se normalizó, los archivos finales están en OneDj_Normalized.
                var normalizedDir = Path.Combine(folder, "OneDj_Normalized");
                if (masterize && _masterResults.Any(r => r.Success) && Directory.Exists(normalizedDir))
                    _lastOutputFolder = normalizedDir;
                ProgressBar.Value = 100;
                ProcessingRing.IsActive = false;
                int ok = _items.Count(i => i.Status == DownloadStatus.Done);
                int fail = _items.Count(i => i.Status == DownloadStatus.Error);

                CompletedIcon.Icon = fail > 0 ? Icon.Warning : Icon.CheckmarkCircle;
                CompletedIcon.Foreground = new SolidColorBrush(fail > 0
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 230, 126, 34)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 39, 174, 96));
                CompletedIcon.Visibility = Visibility.Visible;

                ProgressTitleText.Text = "Completado";
                var message = $"Completado \u2014 {ok} de {pending.Count} enlace(s) listo(s)";
                if (fail > 0)
                    message += $", {fail} con error";
                if (masterize && _masterResults.Count > 0)
                    message += $" \u00b7 {_masterResults.Count} normalizado(s)";
                ProgressText.Text = message;

                ShowEndActions();

                // Control de errores: si hubo fallos en esta tanda, abre el popup
                // con un mensaje simple (sin códigos) y opción de reintentar.
                var runFail = pending.Count(i => i.Status == DownloadStatus.Error);
                if (runFail > 0)
                {
                    var first = pending.Where(i => i.Status == DownloadStatus.Error)
                        .Select(i => i.Message).FirstOrDefault() ?? "";
                    var text = $"Fallaron {runFail} de {pending.Count} enlace(s). {first}".Trim();
                    if (_authFixableFailure && !YouTubeSession.HasFreshCookies())
                        text += " " + AppLimits.YouTubeAccountSuggest;
                    ShowErrorPopup(text);
                }

                // Refresca el estado de la cuenta (pudo caducar en esta tanda).
                RefreshAccountState();
            }
            catch (OperationCanceledException)
            {
                ProcessingRing.IsActive = false;
                ProgressTitleText.Text = "Cancelado";
                ProgressText.Text = "Proceso cancelado por el usuario.";
                foreach (var it in _items.Where(i => i.Status is DownloadStatus.Downloading or DownloadStatus.Processing))
                {
                    it.Status = DownloadStatus.Canceled;
                    it.Percentage = 0;
                    it.Message = "Cancelado";
                }
                ShowEndActions();
            }
            catch (Exception ex)
            {
                App.Log("DownloaderPage.Start", ex.Message, ex.StackTrace);
                var friendly = DownloadErrorText.Friendly(ex.Message);
                ProcessingRing.IsActive = false;
                ProgressTitleText.Text = "Error";
                ProgressText.Text = friendly;
                CompletedIcon.Icon = Icon.Warning;
                CompletedIcon.Visibility = Visibility.Visible;
                ShowErrorPopup(friendly);
            }
            finally
            {
                _isBusy = false;
                UpdateStartButtonText();
                AddButton.IsEnabled = !string.IsNullOrWhiteSpace(UrlBox.Text);
                BrowseButton.IsEnabled = true;
                UrlBox.IsEnabled = true;
                MasterizeCheckBox.IsEnabled = true;
                KeepOriginalCheckBox.IsEnabled = MasterizeCheckBox.IsChecked == true;
                UpdateListState();
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void UpdateSummary()
        {
            int ok = _masterResults.Count(r => r.Success);
            int fail = _masterResults.Count(r => !r.Success);
            SummaryText.Text = $"{ok} correctos \u00b7 {fail} errores \u00b7 {_masterResults.Count} total";
        }

        // ==================================================================
        // LIMPIAR / RESET
        // ==================================================================

        private void ClearButton_Click(object sender, RoutedEventArgs e) => ResetPageState();

        /// <summary>
        /// Muestra la barra final: siempre Limpiar; "Abrir ubicación" solo si
        /// la tanda dejó archivos en una carpeta que existe.
        /// </summary>
        private void ShowEndActions()
        {
            OpenFolderButton.Visibility = Directory.Exists(_lastOutputFolder)
                ? Visibility.Visible : Visibility.Collapsed;
            EndActionsSection.Visibility = Visibility.Visible;
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(_lastOutputFolder))
            {
                SetInfo(InfoBarSeverity.Warning, "Carpeta no disponible", "La carpeta de destino ya no existe.");
                return;
            }
            try
            {
                Process.Start("explorer.exe", _lastOutputFolder);
            }
            catch (Exception ex)
            {
                SetInfo(InfoBarSeverity.Error, "No se pudo abrir la carpeta", DownloadErrorText.Friendly(ex.Message));
            }
        }

        private void ResetPageState()
        {
            try { _cts?.Cancel(); } catch { }

            _items.Clear();
            _masterResults.Clear();
            UrlBox.Text = "";
            SummaryText.Text = "";

            ListSection.Visibility = Visibility.Collapsed;
            ProgressSection.Visibility = Visibility.Collapsed;
            ResultsSection.Visibility = Visibility.Collapsed;
            EndActionsSection.Visibility = Visibility.Collapsed;
            _lastOutputFolder = "";
            StartButton.IsEnabled = false;

            ProcessingRing.IsActive = false;
            ProgressBar.Value = 0;
            CompletedIcon.Visibility = Visibility.Collapsed;

            ResetInfo();
        }

        // ==================================================================
        // ARRASTRE (.txt con enlaces)
        // ==================================================================

        private void Root_DragEnter(object sender, DragEventArgs e) => Root_DragOver(sender, e);

        private void Root_DragOver(object sender, DragEventArgs e)
        {
            if (_isBusy) return;
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Suelta para cargar enlaces";
                Overlay.Visibility = Visibility.Visible;
                _hideTimer.Stop();
            }
            else
            {
                Overlay.Visibility = Visibility.Collapsed;
            }
        }

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

        private async void Root_Drop(object sender, DragEventArgs e)
        {
            _hideTimer.Stop();
            Overlay.Visibility = Visibility.Collapsed;
            if (_isBusy) return;

            try
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                    return;

                var storageItems = await e.DataView.GetStorageItemsAsync();
                var text = new StringBuilder();
                bool anyTxt = false;

                foreach (var si in storageItems)
                {
                    if (si is StorageFile file && file.FileType.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        text.AppendLine(await FileIO.ReadTextAsync(file));
                        anyTxt = true;
                    }
                }

                if (!anyTxt)
                {
                    SetInfo(InfoBarSeverity.Warning, "Formato no admitido", "Arrastra un archivo .txt con un enlace de YouTube por línea.");
                    return;
                }

                var current = UrlBox.Text;
                UrlBox.Text = string.IsNullOrWhiteSpace(current) ? text.ToString() : current + Environment.NewLine + text;
                AddUrlsFromBox();
            }
            catch (Exception ex)
            {
                App.Log("DownloaderPage.Drop", ex.Message, ex.StackTrace);
                SetInfo(InfoBarSeverity.Error, "No se pudo leer el archivo", ex.Message);
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _hideTimer.Stop();
        }

        // ==================================================================
        // InfoBar
        // ==================================================================

        /// <summary>Oculta el aviso contextual (la página queda limpia en estado normal).</summary>
        private void ResetInfo()
        {
            InfoBar.Visibility = Visibility.Collapsed;
        }

        /// <summary>Muestra el aviso contextual con una advertencia o error.</summary>
        private void SetInfo(InfoBarSeverity severity, string title, string message)
        {
            InfoBar.Severity = severity;
            InfoBar.Title = title;
            InfoBar.Message = message;
            InfoBar.IsOpen = true;
            InfoBar.Visibility = Visibility.Visible;
        }

        /// <summary>Muestra el popup de error con un mensaje simple (sin códigos).</summary>
        private void ShowErrorPopup(string message)
        {
            ErrorPopupSubtitle.Text = message;
            ErrorPopupOverlay.Visibility = Visibility.Visible;
            ErrorPopupShowStoryboard.Begin();
        }
    }
}

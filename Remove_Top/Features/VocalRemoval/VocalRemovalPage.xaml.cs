using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NAudio.Wave;
using Remove_Top.Controls;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Remove_Top.Features.VocalRemoval
{
    /// <summary>
    /// Página de Extracción de Stems: separa la voz del instrumental con IA
    /// (HT-Demucs FT en ONNX) usando VocalSeparator. Exporta la voz en mono a
    /// la subcarpeta "RemoveTop_Vocals". Máximo 5 canciones estéreo por lote.
    /// </summary>
    public sealed partial class VocalRemovalPage : Page
    {
        private readonly ObservableCollection<string> _queue = [];
        private readonly List<string> _queuePaths = [];
        private readonly ObservableCollection<StemResult> _results = [];
        private CancellationTokenSource? _cts;
        private bool _isProcessing;

        public VocalRemovalPage()
        {
            InitializeComponent();
            QueueListView.ItemsSource = _queue;
            ResultsListView.ItemsSource = _results;
            DownloadButton.Content = UiHelpers.Content(Icon.ArrowDownload, "Descargar modelo", semibold: false, foreground: DownloadButton.Foreground);
            StartButton.Content = UiHelpers.Content(Icon.Mic, "Extraer voces (stems)", foreground: StartButton.Foreground);

            // Configuración del control de origen: solo audio, sin subcarpetas.
            Source.FileFilter = VocalSeparator.IsAudioFile;
            Source.PickerExtensions = [".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma"];

            // Título y descripción del encabezado, centralizados en AppLimits
            // para que el máximo de canciones por lote coincida siempre con el real.
            PageTitleText.Text = AppLimits.VocalRemovalPageTitle;
            PageDescriptionText.Text = AppLimits.VocalRemovalPageSubtitle;
            BrandText.Text = AppLimits.AppName;
            ModelInfoText.Text =
                $"{AppLimits.AppName} necesita descargar un modelo de IA (~316 MB) para separar la voz. " +
                "La descarga ocurre solo una vez. Después funciona sin internet.";

            InitializePage();
        }

        private void InitializePage()
        {
            if (App.VocalSeparator.IsModelLoaded)
            {
                ModelSection.Visibility = Visibility.Collapsed;
                FolderSection.Visibility = Visibility.Visible;
                return;
            }

            if (ModelDownloader.ModelsExist())
            {
                ModelStatusText.Text = "Cargando modelo...";
                ModelInfoText.Text = "Cargando modelo de IA en segundo plano...";
                DownloadButton.IsEnabled = false;
                ModelProgressBar.IsIndeterminate = true;
                _ = LoadModelAsync();
                return;
            }

            ModelStatusText.Text = "No descargado";
            ModelProgressBar.IsIndeterminate = false;
            DownloadButton.IsEnabled = true;
        }

        private async Task LoadModelAsync()
        {
            var path = ModelDownloader.GetModelPath();
            if (await App.VocalSeparator.LoadModelAsync(path))
            {
                ModelProgressBar.IsIndeterminate = false;
                ModelSection.Visibility = Visibility.Collapsed;
                FolderSection.Visibility = Visibility.Visible;
            }
            else
            {
                ModelProgressBar.IsIndeterminate = false;
                ModelStatusText.Text = "Error al cargar";
                ModelInfoText.Text = "No se pudo cargar el modelo. Intenta descargarlo de nuevo.";
                DownloadButton.IsEnabled = true;
                DownloadButton.Content = UiHelpers.Content(Icon.ArrowDownload, "Descargar modelo", semibold: false, foreground: DownloadButton.Foreground);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadButton.IsEnabled = false;
            DownloadButton.Content = UiHelpers.Content(Icon.ArrowClockwise, "Descargando...", semibold: false, foreground: DownloadButton.Foreground);
            ModelProgressBar.IsIndeterminate = false;
            ModelProgressBar.Visibility = Visibility.Visible;

            var progress = new Progress<ModelProgress>(p =>
            {
                ModelProgressBar.Value = p.Percentage;
                ModelStatusText.Text = p.Status;
                ModelInfoText.Text = p.TotalBytes > 0
                    ? $"Descargando... {p.BytesDownloaded / 1024 / 1024} MB de {p.TotalBytes / 1024 / 1024} MB"
                    : $"Descargando... {p.BytesDownloaded / 1024 / 1024} MB";
            });

            try
            {
                var downloader = new ModelDownloader();
                await downloader.DownloadModelsAsync(progress);
                if (await App.VocalSeparator.LoadModelAsync(ModelDownloader.GetModelPath()))
                {
                    ModelStatusText.Text = "Listo";
                    ModelInfoText.Text = "Modelo de IA cargado correctamente.";
                    await Task.Delay(500);
                    ModelSection.Visibility = Visibility.Collapsed;
                    FolderSection.Visibility = Visibility.Visible;
                }
                else
                {
                    ModelStatusText.Text = "Error al cargar";
                    ModelInfoText.Text = "No se pudo cargar el modelo descargado. Intenta de nuevo.";
                    DownloadButton.IsEnabled = true;
                    DownloadButton.Content = UiHelpers.Content(Icon.ArrowClockwise, "Reintentar descarga", semibold: false, foreground: DownloadButton.Foreground);
                }
            }
            catch (Exception ex)
            {
                ModelStatusText.Text = "Error";
                ModelInfoText.Text = $"Error de descarga: {ex.Message}";
                DownloadButton.IsEnabled = true;
                DownloadButton.Content = UiHelpers.Content(Icon.ArrowClockwise, "Reintentar descarga", semibold: false, foreground: DownloadButton.Foreground);
            }
        }

/// <summary>
        /// Al soltar contenido sobre la página (carpetas o archivos) se carga en
        /// el control de origen. Solo se acepta si el modelo ya está cargado y la
        /// app no está procesando.
        /// </summary>
        private void DropTarget_FilesDropped(object? sender, DropFilesEventArgs e)
        {
            if (_isProcessing) return;
            if (FolderSection.Visibility != Visibility.Visible) return;
            Source.LoadSource(e.Paths);
        }

        /// <summary>
        /// Al cambiar el estado del origen (carga/reset) se reconstruye la cola
        /// de canciones estéreo (máx. <see cref="AppLimits.VocalRemovalMaxFilesPerBatch"/>).
        /// </summary>
        private void Source_StateChanged(object? sender, EventArgs e)
        {
            if (_isProcessing) return;
            if (Source.HasFiles)
                LoadQueue();
            else
            {
                _queue.Clear();
                _queuePaths.Clear();
                QueueSection.Visibility = Visibility.Collapsed;
                FileCountText.Visibility = Visibility.Collapsed;
                StartButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Llena la cola con las canciones estéreo de los archivos cargados
        /// (hasta el máximo por lote) y actualiza los contadores de la UI.
        /// </summary>
        private void LoadQueue()
        {
            var stereoFiles = Source.Files
                .Where(f => IsStereo(f))
                .Take(AppLimits.VocalRemovalMaxFilesPerBatch)
                .ToArray();

            _queue.Clear();
            _queuePaths.Clear();
            foreach (var f in stereoFiles)
            {
                _queue.Add(Path.GetFileName(f));
                _queuePaths.Add(f);
            }

            if (_queue.Count > 0)
            {
                QueueSection.Visibility = Visibility.Visible;
                QueueCountText.Text = $"{_queue.Count}/{AppLimits.VocalRemovalMaxFilesPerBatch}";
                FileCountText.Text = $"{Source.Files.Count} archivo(s) de audio encontrado(s) \u00b7 {_queue.Count} compatible(s)";
            }
            else
            {
                QueueSection.Visibility = Visibility.Collapsed;
                FileCountText.Text = $"{Source.Files.Count} archivo(s) de audio encontrado(s) \u00b7 Ninguno compatible (se requiere est\u00e9reo)";
            }
            FileCountText.Visibility = Visibility.Visible;
            StartButton.IsEnabled = _queue.Count > 0 && !_isProcessing;
        }

        private static bool IsStereo(string path)
        {
            try
            {
                using var reader = new MediaFoundationReader(path);
                return reader.WaveFormat.Channels >= 2;
            }
            catch { return false; }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                return;
            }

            if (_queuePaths.Count == 0) return;

            var files = _queuePaths.ToArray();

            _results.Clear();
            _isProcessing = true;
            CompleteBadge.Visibility = Visibility.Collapsed;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            Source.SetEnabled(false);
            ProgressSection.Visibility = Visibility.Visible;
            ResultsSection.Visibility = Visibility.Visible;
            OverallProgressBar.Value = 0;
            FileProgressBar.Value = 0;

            _cts = new CancellationTokenSource();
            var progress = new Progress<StemProgress>(p =>
            {
                OverallProgressBar.Value = p.Percentage;
                ProgressCountText.Text = $"{p.CurrentIndex}/{p.TotalCount}";
                FileProgressBar.Value = p.FileProgress;

                if (p.FileProgress < 100)
                    ProgressText.Text = $"Procesando: {p.CurrentFile} ({p.FileProgress:F0}%)";
                else
                    ProgressText.Text = $"Finalizado: {p.CurrentFile}";

                if (p.Result != null)
                {
                    _results.Add(p.Result);
                    ResultsListView.ScrollIntoView(p.Result);
                    UpdateSummary();
                }
            });

            try
            {
                await App.VocalSeparator.ProcessFilesAsync(files, progress, _cts.Token);

                var ok = _results.Count(r => r.Success);
                var fail = _results.Count(r => !r.Success);
                CompleteText.Text = fail == 0 ? "\u2713 Completado" : "\u2713 Completado con errores";
                CompleteBadge.Visibility = Visibility.Visible;
                OverallProgressBar.Value = 100;
                ProgressText.Text = "Proceso finalizado";
            }
            catch (OperationCanceledException)
            {
                var canceled = new StemResult
                {
                    FileName = "---",
                    Success = false,
                    Message = "Proceso cancelado"
                };
                _results.Add(canceled);
                UpdateSummary();
            }
            catch (Exception ex)
            {
                var error = new StemResult
                {
                    FileName = "ERROR",
                    Success = false,
                    Message = $"Error general: {ex.Message}"
                };
                _results.Add(error);
                UpdateSummary();
            }
            finally
            {
                _isProcessing = false;
                StartButton.Content = UiHelpers.Content(Icon.Mic, "Extraer voces (stems)", foreground: StartButton.Foreground);
                Source.SetEnabled(true);
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
    }
}

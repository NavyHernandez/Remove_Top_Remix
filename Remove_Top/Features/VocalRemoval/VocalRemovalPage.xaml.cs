using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NAudio.Wave;
using Remove_Top.Helpers;
using System;
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
        private const int MaxFiles = 5;

        private readonly ObservableCollection<string> _queue = [];
        private readonly ObservableCollection<StemResult> _results = [];
        private CancellationTokenSource? _cts;
        private bool _isProcessing;

        public VocalRemovalPage()
        {
            InitializeComponent();
            QueueListView.ItemsSource = _queue;
            ResultsListView.ItemsSource = _results;
            BrowseButton.Content = UiHelpers.Content(Icon.FolderOpen, "Examinar...", foreground: BrowseButton.Foreground);
            DownloadButton.Content = UiHelpers.Content(Icon.ArrowDownload, "Descargar modelo", semibold: false, foreground: DownloadButton.Foreground);
            StartButton.Content = UiHelpers.Content(Icon.Mic, "Extraer voces (stems)", foreground: StartButton.Foreground);
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
                var files = VocalSeparator.GetAudioFiles(folder.Path);
                var stereoFiles = files.Where(f => IsStereo(f)).Take(MaxFiles).ToArray();

                _queue.Clear();
                foreach (var f in stereoFiles)
                    _queue.Add(Path.GetFileName(f));

                if (_queue.Count > 0)
                {
                    QueueSection.Visibility = Visibility.Visible;
                    QueueCountText.Text = $"{_queue.Count}/{MaxFiles}";
                    FileCountText.Text = $"{files.Length} archivo(s) de audio encontrado(s) · {_queue.Count} compatible(s)";
                }
                else
                {
                    QueueSection.Visibility = Visibility.Collapsed;
                    FileCountText.Text = $"{files.Length} archivo(s) de audio encontrado(s) · Ninguno compatible (se requiere est\u00e9reo)";
                }
                FileCountText.Visibility = Visibility.Visible;
                StartButton.IsEnabled = _queue.Count > 0 && !_isProcessing;
            }
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

            var folderPath = FolderPathBox.Text;
            if (string.IsNullOrEmpty(folderPath) || _queue.Count == 0) return;

            var files = _queue.Select(f => Path.Combine(folderPath, f)).ToArray();

            _results.Clear();
            _isProcessing = true;
            CompleteBadge.Visibility = Visibility.Collapsed;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            BrowseButton.IsEnabled = false;
            FolderPathBox.IsEnabled = false;
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
    }
}

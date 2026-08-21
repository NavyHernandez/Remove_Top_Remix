using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly ObservableCollection<NormalizationResult> _results = [];
        private CancellationTokenSource? _cts;
        private bool _isProcessing;
        private bool _isAnalyzing;
        private bool _isUpdatingSlider;

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
            {
                FolderPathBox.Text = folder.Path;
                var files = AudioNormalizer.GetAudioFiles(folder.Path, out int totalFound, out int alreadyProcessed);
                FileCountText.Text = BuildFileCountText(files.Length, totalFound, alreadyProcessed);
                FileCountText.Visibility = Visibility.Visible;
                StartButton.IsEnabled = false;
                await AnalyzeFilesAsync(files);
                UpdateStartButtonText();
            }
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

        private async Task AnalyzeFilesAsync(string[] files)
        {
            _analysisResults.Clear();
            _isAnalyzing = true;
            AnalysisSection.Visibility = Visibility.Visible;
            AnalysisProgressBar.IsIndeterminate = true;
            AnalysisStatusText.Text = "Analizando archivos...";
            BrowseButton.IsEnabled = false;
            CancelButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", semibold: false, foreground: CancelButton.Foreground);
            CancelButton.Visibility = Visibility.Visible;

            var progress = new Progress<AnalysisResult>(r =>
            {
                _analysisResults.Add(r);
                AnalysisListView.ScrollIntoView(r);
            });

            _cts = new CancellationTokenSource();

            try
            {
                var normalizer = new AudioNormalizer();
                var results = await normalizer.AnalyzeFilesAsync(files, progress, _cts.Token);

                var valid = results.Where(r => r.Success).ToArray();
                if (valid.Length > 0)
                {
                    double min = valid.Min(r => r.PeakDb);
                    double max = valid.Max(r => r.PeakDb);
                    AnalysisSummaryText.Text = $"Rango: {min:F1} a {max:F1} dBFS \u00b7 {valid.Length} archivos v\u00e1lidos";
                }
                else
                {
                    AnalysisSummaryText.Text = "No se pudieron analizar los archivos";
                }

                StartButton.IsEnabled = valid.Length > 0 && !_isProcessing;
                CancelButton.Visibility = valid.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                AnalysisStatusText.Text = $"Completo \u2014 {results.Length} archivos";
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

            var folderPath = FolderPathBox.Text;
            if (string.IsNullOrEmpty(folderPath)) return;

            var files = AudioNormalizer.GetAudioFiles(folderPath, out int totalFound, out int alreadyProcessed);
            if (files.Length == 0) return;

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
            _results.Clear();
            _analysisResults.Clear();

            FolderPathBox.Text = "";
            FileCountText.Text = "";
            FileCountText.Visibility = Visibility.Collapsed;

            AnalysisSection.Visibility = Visibility.Collapsed;
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
    }
}

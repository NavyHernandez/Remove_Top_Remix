using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Remove_Top.Helpers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Remove_Top.Features.BatchRename
{
    /// <summary>
    /// Página de Renombrado Masivo: elimina patrones de texto de los nombres de
    /// archivos (audio, video, imagen, documentos) directamente sobre los originales.
    /// Los patrones se persisten en %LOCALAPPDATA%\Remove_Top\patterns.json.
    /// </summary>
    public sealed partial class BatchRenamePage : Page
    {
        private static readonly string PatternsFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppLimits.AppDataFolderName, "patterns.json");

        private readonly ObservableCollection<RenamePattern> _patterns = [];
        private readonly ObservableCollection<RenameResult> _results = [];
        private readonly ObservableCollection<PatternSuggestion> _patternSuggestions = [];
        private CancellationTokenSource? _cts;
        private bool _isProcessing;
        private bool _isSuggesting;

        public BatchRenamePage()
        {
            InitializeComponent();
            PatternsItemsControl.ItemsSource = _patterns;
            ResultsListView.ItemsSource = _results;
            PatternSuggestionsListView.ItemsSource = _patternSuggestions;
            BrowseButton.Content = UiHelpers.Content(Icon.FolderOpen, "Examinar...", foreground: BrowseButton.Foreground);
            AddPatternButton.Content = UiHelpers.Content(Icon.Add, "Agregar", semibold: false, foreground: AddPatternButton.Foreground);
            StartButton.Content = UiHelpers.Content(Icon.Delete, "Eliminar patrones de los nombres", foreground: StartButton.Foreground);
            CancelButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", semibold: false, foreground: CancelButton.Foreground);
            SuggestPatternsButton.Content = UiHelpers.Content(Icon.Sparkle, "Sugerir patrones con IA", foreground: SuggestPatternsButton.Foreground);
            RestartButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: RestartButton.Foreground);

            // Título y subtítulo del encabezado, centralizados en AppLimits.
            PageTitleText.Text = AppLimits.BatchRenamePageTitle;
            PageSubtitleText.Text = AppLimits.BatchRenamePageSubtitle;
            FreeBadgeText.Text = AppLimits.FreeBadgeText;
            BrandText.Text = AppLimits.AppName;

            // Aviso de límite de patrones, generado desde AppLimits para que
            // coincida siempre con el máximo real (BatchRenameMaxPatterns).
            LimitInfoText.Text = AppLimits.BatchRenameLimitMessage;

            LoadPatterns();
            UpdateUI();
        }

        // ================================================================
        // PERSISTENCIA DE PATRONES (guardar/cargar como JSON)
        // ================================================================

        private void SavePatterns()
        {
            try
            {
                var dir = Path.GetDirectoryName(PatternsFile);
                if (dir != null) Directory.CreateDirectory(dir);

                var texts = _patterns.Select(p => p.Text).ToArray();
                var json = JsonSerializer.Serialize(texts);
                File.WriteAllText(PatternsFile, json);
            }
            catch { }
        }

        private void LoadPatterns()
        {
            try
            {
                if (!File.Exists(PatternsFile)) return;
                var json = File.ReadAllText(PatternsFile);
                var texts = JsonSerializer.Deserialize<string[]>(json);
                if (texts != null)
                {
                    foreach (var t in texts.Take(AppLimits.BatchRenameMaxPatterns))
                        _patterns.Add(new RenamePattern { Text = t });
                }
            }
            catch { }
        }

        // ================================================================
        // SELECCIÓN DE CARPETA
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
            if (folder != null)
            {
                FolderPathBox.Text = folder.Path;
                ClearSuggestions();
                UpdatePreview();
                UpdateUI();
            }
        }

        // ================================================================
        // GESTIÓN DE PATRONES
        // ================================================================

        private void PatternInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            AddPatternButton.IsEnabled = !string.IsNullOrWhiteSpace(PatternInput.Text)
                                         && _patterns.Count < AppLimits.BatchRenameMaxPatterns;
        }

        private void PatternInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                AddPattern();
            }
        }

        private void AddPatternButton_Click(object sender, RoutedEventArgs e) => AddPattern();

        private void AddPattern()
        {
            if (TryAddPattern(PatternInput.Text))
            {
                PatternInput.Text = "";
                PatternInput.Focus(FocusState.Programmatic);
            }
        }

        /// <summary>
        /// Agrega un patrón a la lista si es válido (no vacío, no duplicado y
        /// dentro del máximo). Persiste, actualiza la UI y la vista previa.
        /// Devuelve false si no se pudo agregar.
        /// </summary>
        private bool TryAddPattern(string? rawText)
        {
            var text = (rawText ?? "").Trim();
            if (string.IsNullOrEmpty(text) || _patterns.Count >= AppLimits.BatchRenameMaxPatterns) return false;
            if (_patterns.Any(p => p.Text.Equals(text, StringComparison.OrdinalIgnoreCase))) return false;

            _patterns.Add(new RenamePattern { Text = text });
            SavePatterns();
            UpdateUI();
            UpdatePreview();
            return true;
        }

        private void RemovePattern_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string text)
            {
                var pattern = _patterns.FirstOrDefault(p => p.Text == text);
                if (pattern != null)
                {
                    _patterns.Remove(pattern);
                    SavePatterns();
                    UpdateUI();
                    UpdatePreview();
                }
            }
        }

        // ================================================================
        // VISTA PREVIA
        // ================================================================

        private void UpdatePreview()
        {
            var folderPath = FolderPathBox.Text;
            var patterns = _patterns.Select(p => p.Text).ToArray();

            if (string.IsNullOrEmpty(folderPath) || patterns.Length == 0)
            {
                PreviewSection.Visibility = Visibility.Collapsed;
                UpdateUI();
                return;
            }

            try
            {
                var files = FileRenamer.GetAffectedFiles(folderPath, patterns);

                if (files.Length > 0)
                {
                    PreviewListView.ItemsSource = files.Select(Path.GetFileName).ToArray();
                    AffectedCountText.Text = $"{files.Length} archivo(s)";
                    PreviewSection.Visibility = Visibility.Visible;
                }
                else
                {
                    PreviewListView.ItemsSource = null;
                    AffectedCountText.Text = "0 archivos afectados";
                    PreviewSection.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                PreviewSection.Visibility = Visibility.Collapsed;
            }
            finally
            {
                UpdateUI();
            }
        }

        // ================================================================
        // ESTADO DE LA UI
        // ================================================================

        private void UpdateUI()
        {
            PatternCountText.Text = $"{_patterns.Count}/{AppLimits.BatchRenameMaxPatterns}";
            PatternsContainer.Visibility = _patterns.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AddPatternButton.IsEnabled = !string.IsNullOrWhiteSpace(PatternInput.Text)
                                         && _patterns.Count < AppLimits.BatchRenameMaxPatterns;
            bool hasResults = _results.Count > 0;
            StartButton.IsEnabled = !string.IsNullOrEmpty(FolderPathBox.Text)
                                    && _patterns.Count > 0 && !_isProcessing && !hasResults;
            StartButton.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
            bool showCancel = !string.IsNullOrEmpty(FolderPathBox.Text) && !_isProcessing && !hasResults;
            CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
            CancelButton.IsEnabled = showCancel;
            AiSection.Visibility = !string.IsNullOrEmpty(FolderPathBox.Text) && _patterns.Count > 0
                && !hasResults
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateAiStatus();
        }

        // ================================================================
        // PROCESO DE RENOMBRADO
        // ================================================================

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                return;
            }

            var folderPath = FolderPathBox.Text;
            var patterns = _patterns.Select(p => p.Text).ToArray();
            if (string.IsNullOrEmpty(folderPath) || patterns.Length == 0) return;

            _results.Clear();
            PreviewListView.ItemsSource = null;
            PreviewSection.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            _isProcessing = true;
            UpdateAiStatus();
            CompleteBadge.Visibility = Visibility.Collapsed;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            BrowseButton.IsEnabled = false;
            FolderPathBox.IsEnabled = false;
            PatternInput.IsEnabled = false;
            AddPatternButton.IsEnabled = false;
            ProgressSection.Visibility = Visibility.Visible;
            ResultsSection.Visibility = Visibility.Visible;

            _cts = new CancellationTokenSource();
            var renamer = new FileRenamer();
            var progress = new Progress<RenameProgress>(p =>
            {
                ProgressBar.Value = p.Percentage;
                ProgressText.Text = p.CurrentFile;
                ProgressCountText.Text = $"{p.CurrentIndex}/{p.TotalCount}";

                if (p.Result != null)
                {
                    _results.Add(p.Result);
                    ResultsListView.ScrollIntoView(p.Result);
                    UpdateSummary();
                }
            });

            try
            {
                await renamer.ProcessFolderAsync(folderPath, patterns, progress, _cts.Token);

                var ok = _results.Count(r => r.Success);
                var fail = _results.Count(r => !r.Success);
                CompleteText.Text = fail == 0 ? "✓ Completado" : "✓ Completado con errores";
                CompleteBadge.Visibility = Visibility.Visible;
                ProgressBar.Value = 100;
                ProgressText.Text = "Proceso finalizado";
            }
            catch (OperationCanceledException)
            {
                _results.Add(new RenameResult
                {
                    OriginalName = "---",
                    Success = false,
                    Message = "Proceso cancelado por el usuario"
                });
                UpdateSummary();
            }
            catch (Exception ex)
            {
                _results.Add(new RenameResult
                {
                    OriginalName = "ERROR",
                    Success = false,
                    Message = $"Error general: {ex.Message}"
                });
                UpdateSummary();
            }
            finally
            {
                _isProcessing = false;
                StartButton.Content = UiHelpers.Content(Icon.Delete, "Eliminar patrones de los nombres", foreground: StartButton.Foreground);
                BrowseButton.IsEnabled = true;
                FolderPathBox.IsEnabled = true;
                PatternInput.IsEnabled = true;
                AddPatternButton.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
                RestartButton.Visibility = Visibility.Visible;
                UpdateUI();
            }
        }

        private void UpdateSummary()
        {
            int ok = _results.Count(r => r.Success);
            int fail = _results.Count(r => !r.Success);
            SummaryText.Text = $"{ok} correctos · {fail} errores · {_results.Count} total";
        }

        /// <summary>
        /// "Limpiar": vuelve la página a su estado inicial tras un
        /// renombrado. Limpia la ruta, los resultados, la vista previa, el
        /// progreso y las sugerencias de IA. Los patrones se conservan porque
        /// son preferencias persistentes (patterns.json).
        /// </summary>
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            FolderPathBox.Text = "";
            _results.Clear();

            PreviewListView.ItemsSource = null;
            PreviewSection.Visibility = Visibility.Collapsed;
            ProgressSection.Visibility = Visibility.Collapsed;
            ResultsSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;

            ProgressBar.Value = 0;
            ProgressText.Text = "";
            ProgressCountText.Text = "";

            ClearSuggestions();
            UpdateUI();
        }

        /// <summary>
        /// "Cancelar": resetea la página al estado inicial tras el análisis
        /// ( limpia ruta, resultados, vista previa, progreso y sugerencias).
        /// Los patrones se conservan.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            FolderPathBox.Text = "";
            _results.Clear();

            PreviewListView.ItemsSource = null;
            PreviewSection.Visibility = Visibility.Collapsed;
            ProgressSection.Visibility = Visibility.Collapsed;
            ResultsSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;

            ProgressBar.Value = 0;
            ProgressText.Text = "";
            ProgressCountText.Text = "";

            ClearSuggestions();
            UpdateUI();
        }

        // ================================================================
        // MEJORA DE PATRONES CON IA
        // ================================================================

        private void UpdateAiStatus()
        {
            bool ready = !string.IsNullOrEmpty(FolderPathBox.Text)
                         && _patterns.Count > 0 && !_isProcessing && !_isSuggesting;

            AiStatusText.Text = ready
                ? "Se enviarán los patrones y los primeros 10 nombres de archivos afectados al servidor Topremix."
                : "Selecciona una carpeta y agrega patrones para habilitar la sugerencia de IA.";
            SuggestPatternsButton.IsEnabled = ready;
        }

        private async void SuggestPatternsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSuggesting) return;

            var patterns = _patterns.Select(p => p.Text).ToArray();
            var fileNames = FileRenamer.GetAffectedFiles(FolderPathBox.Text, patterns)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Cast<string>()
                .Take(10)
                .ToArray();

            if (patterns.Length == 0 || fileNames.Length == 0)
            {
                AiStatusText.Text = "Se necesita una carpeta con archivos afectados y al menos un patrón.";
                return;
            }

            _isSuggesting = true;
            SuggestPatternsButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;
            FolderPathBox.IsEnabled = false;
            PatternInput.IsEnabled = false;
            AddPatternButton.IsEnabled = false;
            AiProgressBar.Visibility = Visibility.Visible;
            AiStatusText.Text = "Enviando datos al servidor Topremix...";

            var provider = new GroqPatternSuggester();

            try
            {
                var suggestions = await provider.SuggestPatternsAsync(patterns, fileNames);

                _patternSuggestions.Clear();
                foreach (var s in suggestions)
                    _patternSuggestions.Add(s);

                ApproveAllCheckBox.IsChecked = false;
                PatternSuggestionsSection.Visibility = _patternSuggestions.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                UpdateApprovedCount();

                AiStatusText.Text = $"{_patternSuggestions.Count} patrón(es) sugerido(s). Marca los que quieras agregar.";
            }
            catch (Exception ex)
            {
                AiStatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isSuggesting = false;
                AiProgressBar.Visibility = Visibility.Collapsed;
                BrowseButton.IsEnabled = true;
                FolderPathBox.IsEnabled = true;
                PatternInput.IsEnabled = true;
                AddPatternButton.IsEnabled = true;
                UpdateAiStatus();
                UpdateUI();
            }
        }

        private void ApproveAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool approve = ApproveAllCheckBox.IsChecked == true;
            foreach (var s in _patternSuggestions)
                s.IsApproved = approve;
            UpdateApprovedCount();
        }

        private void ApplyApprovedButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var s in _patternSuggestions.Where(s => s.IsApproved).ToList())
                TryAddPattern(s.Text);

            ClearSuggestions();
            UpdateAiStatus();
            UpdatePreview();
        }

        private void ClearSuggestions()
        {
            _patternSuggestions.Clear();
            ApproveAllCheckBox.IsChecked = false;
            PatternSuggestionsSection.Visibility = Visibility.Collapsed;
            UpdateApprovedCount();
        }

        private void UpdateApprovedCount()
        {
            int approved = _patternSuggestions.Count(s => s.IsApproved);
            ApprovedCountText.Text = $"{approved} de {_patternSuggestions.Count} aprobados";
            ApplyApprovedButton.IsEnabled = approved > 0;
        }
    }
}

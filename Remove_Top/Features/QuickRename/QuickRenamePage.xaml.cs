using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Remove_Top.Controls;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>
    /// Página de Edición Rápida: selecciona una carpeta o archivos .mp3/.wav
    /// (carpeta, botones o arrastre) y permite editar cada nombre en una caja
    /// de texto inline (con extensión). Aplica los cambios con File.Move
    /// directamente sobre los originales.
    /// </summary>
    public sealed partial class QuickRenamePage : Page
    {
        private readonly ObservableCollection<QuickRenameItem> _items = [];
        private readonly ObservableCollection<NamePart> _reorderChips = [];
        private readonly Stack<NamePart[]> _undoStack = new();
        private CancellationTokenSource? _cts;
        private bool _isProcessing;
        private NameSeparator _separator = NameSeparator.Hyphen;
        private string _guideName = "";
        private NamePart? _joinSource;
        private NamePart[] _lastStableChips = [];

        public QuickRenamePage()
        {
            InitializeComponent();
            FilesListView.ItemsSource = _items;
            ResetButton.Content = UiHelpers.Content(Icon.ArrowUndo, "Restaurar", semibold: false, foreground: ResetButton.Foreground);
            StartButton.Content = UiHelpers.Content(Icon.Checkmark, "Aplicar cambios", foreground: StartButton.Foreground);
            RestartButton.Content = UiHelpers.Content(Icon.Broom, "Limpiar", semibold: false, foreground: RestartButton.Foreground);
            ReorderButton.Content = UiHelpers.Content(Icon.ArrowSwap, "Reordenar", semibold: false, foreground: ReorderButton.Foreground);
            CaseButton.Content = UiHelpers.Icon(Icon.TextChangeCase, foreground: CaseButton.Foreground);
            ApplyReorderButton.Content = UiHelpers.Content(Icon.Checkmark, "Aplicar a todos", foreground: ApplyReorderButton.Foreground, textSize: 13);
            UndoReorderButton.Content = UiHelpers.Icon(Icon.ArrowUndo, foreground: UndoReorderButton.Foreground);
            FreeBadgeText.Text = AppLimits.FreeBadgeText;
            LimitInfoText.Text = AppLimits.QuickRenameLimitMessage;

            // Configuración del control de origen: solo .mp3/.wav, recursivo con subcarpetas.
            // El tope de archivos sale de AppLimits (única fuente de verdad) para
            // que el comportamiento real coincida siempre con el mensaje mostrado.
            Source.FileFilter = QuickRenamer.IsSupportedFile;
            Source.PickerExtensions = [".mp3", ".wav"];
            Source.MaxFiles = AppLimits.QuickRenameMaxFilesToScan;

            // Selector de separador para reordenar partes del nombre.
            SeparatorCombo.Items.Add(new ComboBoxItem { Content = "Espacio ( )" });
            SeparatorCombo.Items.Add(new ComboBoxItem { Content = "Guion (-)" });
            SeparatorCombo.SelectedIndex = 1; // guion por defecto (patrón DJ " - ")

            ReorderList.ItemsSource = _reorderChips;
            _reorderChips.CollectionChanged += ReorderChips_CollectionChanged;

            // Título y subtítulo del encabezado, centralizados en AppLimits.
            PageTitleText.Text = AppLimits.QuickRenamePageTitle;
            PageSubtitleText.Text = AppLimits.QuickRenamePageSubtitle;
            BrandText.Text = AppLimits.AppName;

            // Al salir de la página se cancela cualquier renombrado en curso.
            // Las páginas se cachean en MainWindow, así que sin esto el proceso
            // seguiría corriendo en background al navegar a otra página.
            Unloaded += QuickRenamePage_Unloaded;
        }

        /// <summary>
        /// Al navegar fuera de la página se cancela el renombrado en curso
        /// (si lo hubiera) para no dejar trabajo en background sobre una página
        /// cacheada. El finally de StartButton_Click se encarga de restablecer
        /// el estado de la UI.
        /// </summary>
        private void QuickRenamePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
                _cts?.Cancel();
        }

        // ================================================================
        // ARRASTRE / CARGA DE ORIGEN
        // ================================================================

        /// <summary>
        /// Al soltar contenido sobre la página (toda la página es destino de
        /// arrastre) se carga en el control de origen, que escanea carpetas o
        /// usa archivos sueltos. Si la app está procesando, se ignora.
        /// </summary>
        private void DropTarget_FilesDropped(object? sender, DropFilesEventArgs e)
        {
            if (_isProcessing) return;
            Source.LoadSource(e.Paths);
        }

        /// <summary>
        /// Si el arrastre no se pudo leer (fallo Win10), se muestra el motivo
        /// en la línea de estado del origen en vez de quedarse en silencio.
        /// </summary>
        private void DropTarget_DropFailed(object? sender, DropFailedEventArgs e)
        {
            if (_isProcessing) return;
            Source.SetStatus(e.Reason);
        }

        /// <summary>
        /// Al cambiar el estado del origen (carga/reset) se reconstruye la lista
        /// editable a partir de los archivos seleccionados.
        /// </summary>
        private void Source_StateChanged(object? sender, EventArgs e)
        {
            if (_isProcessing) return;

            ResultSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;
            ShowResultErrors([]);
            CancelReorder();

            if (Source.HasFiles)
                PopulateItems();
            else
                ClearItems();

            UpdateUI();
        }

        /// <summary>Reconstruye la lista editable a partir de los archivos cargados.</summary>
        private void PopulateItems()
        {
            foreach (var item in _items)
                item.PropertyChanged -= Item_PropertyChanged;

            _items.Clear();

            foreach (var file in Source.Files)
            {
                var name = Path.GetFileName(file);
                var item = new QuickRenameItem
                {
                    OriginalPath = file,
                    OriginalName = name,
                    LoadedName = name,
                    CurrentName = name
                };
                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            // Primera canción como guía por defecto (solo si no hay ninguna marcada).
            if (_items.Count > 0 && !_items.Any(i => i.IsGuide))
                _items[0].IsGuide = true;

            FileCountText.Text = Source.Truncated
                ? $"{Source.ScannedFiles} archivo(s) .mp3/.wav (mostrando los primeros {Source.ScannedFiles} de {Source.TotalFound})"
                : $"{_items.Count} archivo(s) .mp3/.wav encontrado(s)";
            FileCountText.Visibility = Visibility.Visible;
            ListSection.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Limpia la lista editable (cuando el origen queda sin archivos).</summary>
        private void ClearItems()
        {
            foreach (var item in _items)
                item.PropertyChanged -= Item_PropertyChanged;
            _items.Clear();
            FileCountText.Visibility = Visibility.Collapsed;
            ListSection.Visibility = Visibility.Collapsed;
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(QuickRenameItem.IsDirty))
                UpdateUI();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
                item.CurrentName = item.OriginalName;
            UpdateUI();
        }

        /// <summary>
        /// Cambia el caso de la base (sin extensión) de TODOS los nombres
        /// cargados según la opción del menú (upper/lower/title). Es solo
        /// pre-llenado en vivo: cada TextBox se actualiza al instante vía
        /// CurrentName y "Restaurar originales" lo revierte; el renombrado
        /// real sigue por "Aplicar cambios".
        /// </summary>
        private void CaseItem_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0 || _isProcessing) return;
            if (sender is not MenuFlyoutItem { Tag: string mode }) return;

            foreach (var item in _items)
            {
                var converted = mode switch
                {
                    "lower" => LowercaseBase(item.CurrentName),
                    "title" => TitleCaseBase(item.CurrentName),
                    _ => UppercaseBase(item.CurrentName),
                };
                if (!string.Equals(converted, item.CurrentName, StringComparison.Ordinal))
                    item.CurrentName = converted;
            }
            UpdateUI();
        }

        /// <summary>
        /// Mayúsculas solo a la base del nombre, conservando la extensión tal
        /// cual (.mp3 no se toca). ToUpperInvariant respeta ñ/tildes.
        /// </summary>
        private static string UppercaseBase(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            return baseName.ToUpperInvariant() + ext;
        }

        /// <summary>Minúsculas solo a la base, extensión intacta.</summary>
        private static string LowercaseBase(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            return baseName.ToLowerInvariant() + ext;
        }

        /// <summary>
        /// Primera letra de cada palabra en mayúscula (cultura es, respeta
        /// ñ/tildes), extensión intacta.
        /// </summary>
        private static string TitleCaseBase(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var culture = new System.Globalization.CultureInfo("es");
            return culture.TextInfo.ToTitleCase(baseName.ToLower(culture)) + ext;
        }

        private void UpdateUI()
        {
            int dirty = _items.Count(i => i.IsDirty);
            DirtyCountText.Text = dirty > 0 ? $"{dirty} modificado(s)" : "Sin cambios";
            ResetButton.IsEnabled = dirty > 0;
            StartButton.IsEnabled = _items.Count > 0 && dirty > 0 && !_isProcessing;
            StartButton.Content = UiHelpers.Content(Icon.Checkmark,
                dirty > 0 ? $"Aplicar cambios ({dirty})" : "Aplicar cambios",
                foreground: StartButton.Foreground);

            ReorderButton.IsEnabled = _items.Count > 0 && !_isProcessing;
            CaseButton.IsEnabled = _items.Count > 0 && !_isProcessing;
        }

        // ================================================================
        // REORDENAR PARTES DEL NOMBRE
        // ================================================================

        /// <summary>
        /// Abre la sección de reorden. Usa como plantilla la canción marcada como
        /// guía (si ninguna, la primera) y divide su nombre ORIGINAL cargado
        /// (LoadedName) en partes según el separador elegido.
        /// </summary>
        private void ReorderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0 || _isProcessing) return;

            var guide = _items.FirstOrDefault(i => i.IsGuide) ?? _items[0];
            if (!guide.IsGuide) guide.IsGuide = true;

            _guideName = guide.LoadedName;
            RebuildReorderChips();

            ReorderSection.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Al cambiar el separador se reconstruyen los chips en su orden
        /// original (cualquier reorden previo se descarta).
        /// </summary>
        private void SeparatorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SeparatorCombo.SelectedIndex < 0 || ReorderSection.Visibility != Visibility.Visible) return;

            _separator = SeparatorCombo.SelectedIndex == 0 ? NameSeparator.Space : NameSeparator.Hyphen;
            RebuildReorderChips();
        }

        /// <summary>
        /// Al marcar/desmarcar el CheckBox de guía se mantiene la selección única
        /// (marcar una canción desmarca las demás) y, si la sección de reorden
        /// está abierta, se reconstruyen los chips con la nueva guía.
        /// </summary>
        private void GuideCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox { DataContext: QuickRenameItem item }) return;

            if (item.IsGuide)
            {
                // Desmarca las demás para mantener una única canción guía.
                foreach (var other in _items)
                {
                    if (other != item && other.IsGuide)
                        other.IsGuide = false;
                }

                // Si la sección de reorden está abierta, aplica la nueva guía.
                if (ReorderSection.Visibility == Visibility.Visible && _guideName != item.LoadedName)
                {
                    _guideName = item.LoadedName;
                    RebuildReorderChips();
                }
            }
            else
            {
                // Se desmarcó la guía: sin guía explícita, ReorderButton usa la primera.
                ReorderSampleText.Text = "Ninguna canción seleccionada como guía. Se usará la primera al abrir.";
            }
        }

        /// <summary>Reconstruye los chips de reorden desde el nombre de la canción guía.</summary>
        private void RebuildReorderChips()
        {
            ClearJoinSource();
            _undoStack.Clear();
            _reorderChips.CollectionChanged -= ReorderChips_CollectionChanged;
            _reorderChips.Clear();

            var chips = NamePartReorderer.CreateChips(_guideName, _separator);
            foreach (var chip in chips) _reorderChips.Add(chip);

            ReorderSampleText.Text = chips.Length > 0
                ? $"Canción guía: {_guideName}"
                : $"El nombre '{_guideName}' no tiene partes separables con este separador.";

            _lastStableChips = _reorderChips.ToArray();

            _reorderChips.CollectionChanged += ReorderChips_CollectionChanged;
            UpdateReorderPreview();
            UpdateUndoState();
        }

        /// <summary>Actualiza el preview del resultado con el orden actual de chips.</summary>
        private void UpdateReorderPreview()
        {
            if (_reorderChips.Count == 0)
            {
                ReorderPreviewListHeader.Visibility = Visibility.Collapsed;
                ReorderPreviewList.Visibility = Visibility.Collapsed;
                ReorderPreviewList.ItemsSource = null;
                return;
            }

            // Vista previa de los primeros archivos (solo el resultado).
            var (sizes, newOrder) = ComputeReorder();

            var items = _items.Take(10)
                .Select(item => new ReorderPreviewItem
                {
                    Result = NamePartReorderer.ReorderName(item.CurrentName, _separator, sizes, newOrder)
                })
                .ToList();

            ReorderPreviewList.ItemsSource = items;
            ReorderPreviewListHeader.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ReorderPreviewList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Calcula los tamaños de bloque (en orden original) y la permutación
        /// (orden actual de chips) que definen el reorden. Es la lógica común
        /// usada tanto por la vista previa como por "Aplicar a todos".
        /// </summary>
        private (int[] sizes, int[] newOrder) ComputeReorder()
        {
            var sizes = NamePartReorderer.BuildSizes(_reorderChips);
            var newOrder = _reorderChips.Select(c => c.BlockIndex).ToArray();
            return (sizes, newOrder);
        }

        /// <summary>
        /// Doble clic en un chip: si no hay fuente de unión seleccionada, este
        /// chip se marca como fuente (se ilumina). Si ya hay una fuente distinta,
        /// se fusionan ambos bloques en uno. Con separador guion la unión no está
        /// disponible (cada parte ya es un bloque).
        /// </summary>
        private void ReorderChip_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (_separator != NameSeparator.Space) return;
            if (sender is not FrameworkElement { DataContext: NamePart chip }) return;

            // Sin fuente seleccionada: se marca este chip como fuente de unión.
            if (_joinSource == null)
            {
                _joinSource = chip;
                chip.IsJoinSource = true;
                ReorderSampleText.Text = "Selecciona otra etiqueta para unirla con este bloque.";
                return;
            }

            // Mismo chip: se cancela la selección de fuente.
            if (_joinSource == chip)
            {
                ClearJoinSource();
                ReorderSampleText.Text = $"Canción guía: {_guideName}";
                return;
            }

            // Fusión de dos bloques.
            var merged = NamePartReorderer.Merge(_joinSource, chip);
            if (merged == null)
            {
                ReorderSampleText.Text = "Solo se pueden unir bloques consecutivos del nombre.";
                ClearJoinSource();
                return;
            }

            // Guarda el estado previo para poder deshacer la unión.
            PushUndoState();

            // Reemplaza los dos chips por el bloque fusionado.
            var sourceIndex = _reorderChips.IndexOf(_joinSource);
            var targetIndex = _reorderChips.IndexOf(chip);
            var low = Math.Min(sourceIndex, targetIndex);
            var high = Math.Max(sourceIndex, targetIndex);

            _reorderChips.CollectionChanged -= ReorderChips_CollectionChanged;
            _reorderChips[low] = merged;
            _reorderChips.RemoveAt(high);
            _reorderChips.CollectionChanged += ReorderChips_CollectionChanged;

            _lastStableChips = _reorderChips.ToArray();
            ClearJoinSource();
            ReorderSampleText.Text = $"Canción guía: {_guideName}";
            UpdateReorderPreview();
        }

        /// <summary>
        /// Clic derecho en un chip: si es un bloque unido (WordCount &gt; 1),
        /// se separa de vuelta en sus palabras individuales.
        /// </summary>
        private void ReorderChip_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (_separator != NameSeparator.Space) return;
            if (sender is not FrameworkElement { DataContext: NamePart chip }) return;
            if (!chip.IsMerged) return;

            var index = _reorderChips.IndexOf(chip);
            if (index < 0) return;

            var parts = NamePartReorderer.Split(chip);

            // Guarda el estado previo para poder deshacer la separación.
            PushUndoState();

            _reorderChips.CollectionChanged -= ReorderChips_CollectionChanged;
            _reorderChips.RemoveAt(index);
            for (int i = 0; i < parts.Length; i++)
                _reorderChips.Insert(index + i, parts[i]);
            _reorderChips.CollectionChanged += ReorderChips_CollectionChanged;

            _lastStableChips = _reorderChips.ToArray();
            ClearJoinSource();
            ReorderSampleText.Text = $"Canción guía: {_guideName}";
            UpdateReorderPreview();
        }

        /// <summary>Limpia la fuente de unión seleccionada (si la hubiera).</summary>
        private void ClearJoinSource()
        {
            if (_joinSource != null)
            {
                _joinSource.IsJoinSource = false;
                _joinSource = null;
            }
        }

        /// <summary>
        /// Al cambiar la colección de chips se refresca el preview en vivo.
        /// Si el cambio es un Move (reorden por arrastre del ListView), se guarda
        /// el estado anterior (<see cref="_lastStableChips"/>) en el historial de
        /// deshacer. Los cambios programáticos (merge/split/undo/rebuild) se
        /// hacen con el handler desconectado, así que aquí solo llegan los
        /// reordenes reales del usuario.
        /// </summary>
        private void ReorderChips_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            {
                _undoStack.Push(_lastStableChips);
                UpdateUndoState();
                _lastStableChips = _reorderChips.ToArray();
            }

            UpdateReorderPreview();
        }

        /// <summary>Guarda un snapshot del estado estable actual para deshacer.</summary>
        private void PushUndoState()
        {
            // Se guarda el último estado confirmado (los chips antes del cambio).
            _undoStack.Push(_lastStableChips);
            UndoReorderButton.IsEnabled = true;
        }

        /// <summary>
        /// Deshace el último cambio en la sección de reorden (unión, separación
        /// o reordenamiento de chips), restaurando el estado anterior.
        /// </summary>
        private void UndoLastChange()
        {
            if (_undoStack.Count == 0) return;

            var state = _undoStack.Pop();

            ClearJoinSource();
            _reorderChips.CollectionChanged -= ReorderChips_CollectionChanged;
            _reorderChips.Clear();
            foreach (var chip in state) _reorderChips.Add(chip);
            _reorderChips.CollectionChanged += ReorderChips_CollectionChanged;

            _lastStableChips = _reorderChips.ToArray();
            ReorderSampleText.Text = $"Canción guía: {_guideName}";
            UpdateReorderPreview();
            UpdateUndoState();
        }

        /// <summary>Refresca el estado habilitado del botón deshacer.</summary>
        private void UpdateUndoState()
        {
            UndoReorderButton.IsEnabled = _undoStack.Count > 0;
        }

        /// <summary>Deshace el último cambio al pulsar el botón deshacer.</summary>
        private void UndoReorderButton_Click(object sender, RoutedEventArgs e)
        {
            UndoLastChange();
        }

        /// <summary>
        /// Aplica el nuevo orden a TODOS los archivos de la lista: pre-llena el
        /// CurrentName de cada ítem con su nombre reordenado (solo a los índices
        /// que existan). Luego el usuario pulsa "Aplicar cambios" para renombrar
        /// en disco con el flujo existente.
        /// </summary>
        private void ApplyReorderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_reorderChips.Count <= 1) return;

            // La plantilla define los tamaños de bloque en orden original y la
            // permutación (orden de BlockIndex) en el orden actual de chips.
            var (sizes, newOrder) = ComputeReorder();

            foreach (var item in _items)
            {
                var reordered = NamePartReorderer.ReorderName(item.CurrentName, _separator, sizes, newOrder);
                if (!string.Equals(reordered, item.CurrentName, StringComparison.Ordinal))
                    item.CurrentName = reordered;
            }

            CancelReorder();
            UpdateUI();
        }

        /// <summary>Cierra la sección de reorden sin aplicar.</summary>
        private void CancelReorderButton_Click(object sender, RoutedEventArgs e)
        {
            CancelReorder();
        }

        /// <summary>Oculta la sección de reorden y limpia los chips.</summary>
        private void CancelReorder()
        {
            ClearJoinSource();
            _undoStack.Clear();
            _reorderChips.CollectionChanged -= ReorderChips_CollectionChanged;
            _reorderChips.Clear();
            _reorderChips.CollectionChanged += ReorderChips_CollectionChanged;

            ReorderSection.Visibility = Visibility.Collapsed;
            UpdateUndoState();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                return;
            }

            if (_items.Count == 0) return;

            _isProcessing = true;
            StartButton.Content = UiHelpers.Content(Icon.Dismiss, "Cancelar", foreground: StartButton.Foreground);
            Source.SetEnabled(false);
            ResetButton.IsEnabled = false;
            CaseButton.IsEnabled = false;
            ReorderButton.IsEnabled = false;

            _cts = new CancellationTokenSource();
            var renamer = new QuickRenamer();
            var pending = _items.Count(i => i.IsDirty);

            try
            {
                var results = await renamer.ApplyRenamesAsync(_items, _cts.Token);

                // Actualiza en el lugar los ítems renombrados: la lista muestra
                // los NOMBRES NUEVOS (ya no depende del snapshot original del
                // origen). Los fallos conservan su nombre para que el usuario
                // pueda corregirlos y reintentar.
                foreach (var result in results)
                {
                    if (!result.Success) continue;

                    result.Item.OriginalPath = result.NewPath;
                    result.Item.OriginalName = result.NewName;
                    result.Item.CurrentName = result.NewName;
                }

                var changed = results.Count(r => r.Success);
                var failed = results.Where(r => !r.Success).ToArray();

                CompleteText.Text = "\u2713 Completado";
                CompleteBadge.Background = new SolidColorBrush(Color.FromArgb(255, 0x70, 0xAD, 0x47)); // verde
                CompleteBadge.Visibility = Visibility.Visible;

                ResultSummaryText.Text = failed.Length > 0
                    ? $"Se cambiaron {changed} de {pending} archivo(s). {failed.Length} no se pudieron renombrar."
                    : $"Se cambiaron {changed} de {pending} archivo(s).";
                ShowResultErrors(failed);

                ResultSection.Visibility = Visibility.Visible;
                RestartButton.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException)
            {
                CompleteText.Text = "\u2715 Cancelado";
                CompleteBadge.Background = new SolidColorBrush(Color.FromArgb(255, 0xE6, 0x7E, 0x22)); // ámbar
                CompleteBadge.Visibility = Visibility.Visible;
                ResultSummaryText.Text = "Proceso cancelado por el usuario.";
                ShowResultErrors([]);
                ResultSection.Visibility = Visibility.Visible;
                RestartButton.Visibility = Visibility.Visible;
            }
            finally
            {
                _isProcessing = false;
                Source.SetEnabled(true);
                _cts?.Dispose();
                _cts = null;
                UpdateUI();
            }
        }

        /// <summary>
        /// Rellena y muestra/oculta la lista de archivos que no se pudieron
        /// renombrar (resultados fallidos). Con una lista vacía se oculta todo.
        /// </summary>
        private void ShowResultErrors(QuickRenameResult[] failed)
        {
            ResultErrorsList.ItemsSource = failed;
            bool hasErrors = failed.Length > 0;
            ResultErrorsTitle.Visibility = hasErrors ? Visibility.Visible : Visibility.Collapsed;
            ResultErrorsList.Visibility = hasErrors ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// "Limpiar": vuelve la página a su estado inicial tras el renombrado.
        /// Limpia el origen, la lista editable y el resultado.
        /// </summary>
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            Source.Reset();
            ClearItems();
            ShowResultErrors([]);
            CancelReorder();

            ResultSection.Visibility = Visibility.Collapsed;
            CompleteBadge.Visibility = Visibility.Collapsed;
            RestartButton.Visibility = Visibility.Collapsed;

            UpdateUI();
        }
    }
}
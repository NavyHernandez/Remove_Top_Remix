using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Remove_Top.Controls
{
    /// <summary>
    /// Selección de origen reutilizable: acepta una carpeta (escaneada de forma
    /// recursiva o solo su nivel superior, según <see cref="ScanRecursive"/>) o
    /// archivos sueltos (filtrados por <see cref="FileFilter"/>), hasta
    /// <see cref="MaxFiles"/>. Expone la lista resultante vía <see cref="Files"/>
    /// y notifica los cambios mediante <see cref="StateChanged"/>.
    ///
    /// La tarjeta cambia de color (tinte de <see cref="AccentColor"/>) cuando hay
    /// archivos cargados, muestra un botón "Limpiar" y una pista de arrastre.
    /// El arrastre lo gestiona la página (DropTargetControl); este control solo
    /// consume las rutas a través de <see cref="LoadSource"/>.
    /// </summary>
    public sealed partial class FileSourceControl : UserControl
    {
        /// <summary>Color de acento de la tarjeta (tinte, botones y pista). Por defecto teal.</summary>
        public static readonly DependencyProperty AccentColorProperty =
            DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(FileSourceControl),
                new PropertyMetadata(Color.FromArgb(255, 0, 168, 143), OnAccentColorChanged));

        /// <summary>Indica si las carpetas se escanean incluyendo subcarpetas.</summary>
        public static readonly DependencyProperty ScanRecursiveProperty =
            DependencyProperty.Register(nameof(ScanRecursive), typeof(bool), typeof(FileSourceControl),
                new PropertyMetadata(true, OnScanRecursiveChanged));

        /// <summary>Máximo de archivos que se cargan por ejecución (límite gratuito).</summary>
        public static readonly DependencyProperty MaxFilesProperty =
            DependencyProperty.Register(nameof(MaxFiles), typeof(int), typeof(FileSourceControl),
                new PropertyMetadata(1000));

        /// <summary>Muestra la marca del sitio centrada en la línea "Origen". Opt-in por página.</summary>
        public static readonly DependencyProperty ShowBrandSiteProperty =
            DependencyProperty.Register(nameof(ShowBrandSite), typeof(bool), typeof(FileSourceControl),
                new PropertyMetadata(false, OnShowBrandSiteChanged));

        private List<string> _files = [];
        private bool _enabled = true;
        private bool _truncated;
        private int _scannedFiles;
        private int _totalFound;
        private Brush _sourceCardBackground = null!;
        private Brush _sourceCardBorder = null!;

        /// <summary>Se dispara cuando cambia el estado (archivos cargados o reset).</summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// Filtro de archivos (por extensión/tipo). Si es null se aceptan todos.
        /// Debe asignarse desde la página antes de cargar.
        /// </summary>
        public Func<string, bool>? FileFilter { get; set; }

        /// <summary>
        /// Extensiones usadas por el selector de archivos ("Archivos..."). Si
        /// está vacío, el selector permite todos los archivos.
        /// </summary>
        public string[] PickerExtensions { get; set; } = [];

        /// <summary>Archivos cargados (hasta <see cref="MaxFiles"/>).</summary>
        public IReadOnlyList<string> Files => _files;

        /// <summary>Indica si hay archivos cargados.</summary>
        public bool HasFiles => _files.Count > 0;

        /// <summary>Indica si el escaneo se truncó (más archivos del límite).</summary>
        public bool Truncated => _truncated;

        /// <summary>Archivos cargados en la última selección.</summary>
        public int ScannedFiles => _scannedFiles;

        /// <summary>Archivos encontrados (antes de aplicar el límite).</summary>
        public int TotalFound => _totalFound;

        /// <summary>Color de acento de la tarjeta.</summary>
        public Color AccentColor
        {
            get => (Color)GetValue(AccentColorProperty);
            set => SetValue(AccentColorProperty, value);
        }

        /// <summary>Indica si las carpetas se escanean incluyendo subcarpetas.</summary>
        public bool ScanRecursive
        {
            get => (bool)GetValue(ScanRecursiveProperty);
            set => SetValue(ScanRecursiveProperty, value);
        }

        /// <summary>Máximo de archivos que se cargan por ejecución.</summary>
        public int MaxFiles
        {
            get => (int)GetValue(MaxFilesProperty);
            set => SetValue(MaxFilesProperty, value);
        }

        /// <summary>Muestra la marca del sitio centrada en la línea "Origen".</summary>
        public bool ShowBrandSite
        {
            get => (bool)GetValue(ShowBrandSiteProperty);
            set => SetValue(ShowBrandSiteProperty, value);
        }

        private static void OnShowBrandSiteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FileSourceControl c) c.ApplyBrandSite();
        }

        public FileSourceControl()
        {
            InitializeComponent();
            _sourceCardBackground = SourceCard.Background;
            _sourceCardBorder = SourceCard.BorderBrush;
            ApplyAccent();
            ApplyBrandSite();
            UpdateState();
        }

        // ================================================================
        // SELECCIÓN DE ORIGEN (carpeta / archivos)
        // ================================================================

        private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_enabled) return;
            var picker = new FolderPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) LoadSource([folder.Path]);
        }

        private async void BrowseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_enabled) return;
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            if (PickerExtensions.Length == 0)
                picker.FileTypeFilter.Add("*");
            else
                foreach (var ext in PickerExtensions)
                    picker.FileTypeFilter.Add(ext);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
                LoadSource(files.Select(f => f.Path));
        }

        /// <summary>"Limpiar": resetea esta carga (si el usuario no quiere ejecutar).</summary>
        private void ClearLoadedButton_Click(object sender, RoutedEventArgs e) => Reset();

        /// <summary>
        /// Carga rutas (carpetas → escaneo según <see cref="ScanRecursive"/>;
        /// archivos → se usan tal cual), aplica <see cref="FileFilter"/> y
        /// <see cref="MaxFiles"/>, y notifica el cambio de estado.
        /// </summary>
        public void LoadSource(IEnumerable<string> paths)
        {
            if (!_enabled) return;

            var list = paths.ToArray();
            if (list.Length == 0) return;

            Reset();
            Collect(list);

            SourcePathBox.Text = list.Length == 1
                ? list[0]
                : $"{list.Length} elemento(s) seleccionado(s)";

            if (_files.Count == 0)
            {
                SourceStatusText.Text = "No se encontraron archivos compatibles.";
            }
            else
            {
                string trunc = _truncated
                    ? $" (se cargaron los primeros {_scannedFiles} de {_totalFound})"
                    : "";
                SourceStatusText.Text = $"{_scannedFiles} archivo(s) listo(s) para procesar.{trunc}";
            }

            UpdateState();
        }

        /// <summary>
        /// Recoge los archivos a procesar desde las rutas indicadas (carpetas
        /// escaneadas de forma recursiva o no; archivos directos filtrados).
        /// </summary>
        private void Collect(IEnumerable<string> paths)
        {
            var files = new List<string>();
            int total = 0;

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var option = ScanRecursive
                            ? SearchOption.AllDirectories
                            : SearchOption.TopDirectoryOnly;
                        foreach (var file in Directory.EnumerateFiles(path, "*.*", option))
                        {
                            if (FileFilter != null && !FileFilter(file)) continue;
                            total++;
                            if (files.Count < MaxFiles) files.Add(file);
                        }
                    }
                    catch
                    {
                        // Carpeta sin permisos de lectura: se omite y se sigue con el resto.
                    }
                }
                else if (System.IO.File.Exists(path))
                {
                    if (FileFilter != null && !FileFilter(path)) continue;
                    total++;
                    if (files.Count < MaxFiles) files.Add(path);
                }
            }

            _files = files;
            _totalFound = total;
            _scannedFiles = files.Count;
            _truncated = total > MaxFiles;
        }

        /// <summary>Muestra un mensaje en la línea de estado del origen.</summary>
        public void SetStatus(string message) => SourceStatusText.Text = message;

        // ================================================================
        // ESTADO
        // ================================================================

        /// <summary>Reinicia el control: limpia la selección y el estado.</summary>
        public void Reset()
        {
            _files = [];
            _truncated = false;
            _scannedFiles = 0;
            _totalFound = 0;
            SourcePathBox.Text = "";
            SourceStatusText.Text = "";
            UpdateState();
        }

        /// <summary>Habilita/deshabilita los controles (se usa durante el procesamiento).</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            UpdateState();
        }

        /// <summary>
        /// Refresca el aspecto de la tarjeta (tinte de acento cuando hay archivos
        /// cargados) y notifica a la página mediante <see cref="StateChanged"/>.
        /// </summary>
        private void UpdateState()
        {
            bool loaded = HasFiles && _enabled;
            var accent = AccentColor;

            SourceCard.Background = loaded
                ? new SolidColorBrush(Color.FromArgb(0x1F, accent.R, accent.G, accent.B))
                : _sourceCardBackground;
            SourceCard.BorderBrush = loaded
                ? new SolidColorBrush(accent)
                : _sourceCardBorder;

            ClearLoadedButton.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
            BrowseFolderButton.IsEnabled = _enabled;
            BrowseFilesButton.IsEnabled = _enabled;

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Aplica el color de acento a botones, pista de arrastre y tinte.</summary>
        private void ApplyAccent()
        {
            var accent = AccentColor;
            var brush = new SolidColorBrush(accent);
            BrowseFolderButton.BorderBrush = brush;
            BrowseFolderButton.Foreground = brush;
            BrowseFilesButton.BorderBrush = brush;
            BrowseFilesButton.Foreground = brush;
            ClearLoadedButton.BorderBrush = brush;
            ClearLoadedButton.Foreground = brush;
            DropZone.Background = new SolidColorBrush(Color.FromArgb(0x0A, accent.R, accent.G, accent.B));
            DropZoneIcon.Foreground = brush;
            DropZoneText.Text = ScanRecursive
                ? "Arrastra aquí carpetas o archivos (los archivos se usan tal cual; las carpetas se escanean incluyendo subcarpetas)."
                : "Arrastra aquí carpetas o archivos (los archivos se usan tal cual; las carpetas se escanean sin subcarpetas).";
        }

        /// <summary>Muestra u oculta la marca del sitio en la línea "Origen".</summary>
        private void ApplyBrandSite()
        {
            BrandSiteText.Text = AppLimits.AppBrandSite;
            BrandSiteText.Visibility = ShowBrandSite ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Al cambiar el color de acento se recolorean los elementos de la tarjeta.</summary>
        private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((FileSourceControl)d).ApplyAccent();
        }

        /// <summary>Al cambiar la recursión se actualiza el texto de la pista de arrastre.</summary>
        private static void OnScanRecursiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((FileSourceControl)d).ApplyAccent();
        }
    }
}
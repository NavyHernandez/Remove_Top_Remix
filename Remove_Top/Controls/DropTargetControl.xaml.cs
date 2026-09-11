using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace Remove_Top.Controls
{
    /// <summary>
    /// Destino de arrastre reutilizable para toda una página: acepta carpetas y
    /// archivos soltados desde el explorador, muestra un overlay oscuro mientras
    /// se arrastra y notifica las rutas recibidas mediante <see cref="FilesDropped"/>.
    ///
    /// El contenido que envuelve se asigna a la propiedad <see cref="Host"/>
    /// (el contenido de la página que lo usa) y el overlay se dibuja encima de
    /// todo. El timer anti-parpadeo evita que el overlay titile al pasar por
    /// los elementos hijos.
    /// </summary>
    public sealed partial class DropTargetControl : UserControl
    {
        /// <summary>Título del overlay (configurable desde XAML).</summary>
        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register(nameof(Caption), typeof(string), typeof(DropTargetControl),
                new PropertyMetadata("Suelta para cargar"));

        /// <summary>Subtítulo del overlay (configurable desde XAML).</summary>
        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(DropTargetControl),
                new PropertyMetadata("Arrastra carpetas o archivos"));

        /// <summary>
        /// Contenido que envuelve el control (el contenido de la página que lo
        /// usa). Se aloja en un ContentPresenter y el overlay se dibuja encima.
        /// </summary>
        public static readonly DependencyProperty HostProperty =
            DependencyProperty.Register(nameof(Host), typeof(UIElement), typeof(DropTargetControl),
                new PropertyMetadata(null));

        /// <summary>
        /// Color de acento del overlay (círculo e icono). Por defecto teal,
        /// el color de la página de Etiquetas. Cada página puede poner el suyo.
        /// </summary>
        public static readonly DependencyProperty AccentColorProperty =
            DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(DropTargetControl),
                new PropertyMetadata(Color.FromArgb(255, 0, 168, 143), OnAccentColorChanged));

        /// <summary>Retardo para ocultar el overlay al salir del área (evita parpadeos).</summary>
        private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(250);

        private readonly DispatcherTimer _hideTimer;

        /// <summary>Se dispara al soltar carpetas y/o archivos sobre la página.</summary>
        public event EventHandler<DropFilesEventArgs>? FilesDropped;

        /// <summary>
        /// Se dispara cuando el arrastre se aceptó (overlay visible) pero no se
        /// pudo leer lo soltado: GetStorageItemsAsync lanzó excepción (fallo
        /// conocido en algunos Windows 10) o ninguna ruta venía legible
        /// (carpeta virtual, ZIP, etc.). La página muestra <see cref="DropFailedEventArgs.Reason"/>
        /// en su línea de estado para no quedarse en silencio.
        /// </summary>
        public event EventHandler<DropFailedEventArgs>? DropFailed;

        /// <summary>Título del overlay de arrastre.</summary>
        public string Caption
        {
            get => (string)GetValue(CaptionProperty);
            set => SetValue(CaptionProperty, value);
        }

        /// <summary>Subtítulo del overlay de arrastre.</summary>
        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        /// <summary>Contenido envuelto por el control (se muestra bajo el overlay).</summary>
        public UIElement? Host
        {
            get => (UIElement?)GetValue(HostProperty);
            set => SetValue(HostProperty, value);
        }

        /// <summary>Color de acento del overlay de arrastre.</summary>
        public Color AccentColor
        {
            get => (Color)GetValue(AccentColorProperty);
            set => SetValue(AccentColorProperty, value);
        }

        public DropTargetControl()
        {
            InitializeComponent();
            _hideTimer = new DispatcherTimer { Interval = HideDelay };
            _hideTimer.Tick += HideTimer_Tick;
            ApplyAccent();
        }

        /// <summary>Al cambiar el color de acento se recolorean el círculo y el icono del overlay.</summary>
        private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((DropTargetControl)d).ApplyAccent();
        }

        /// <summary>
        /// Aplica el color de acento al círculo (con un 20 % de opacidad) y al
        /// icono del overlay. Se invoca al crear el control y al cambiar el color.
        /// </summary>
        private void ApplyAccent()
        {
            var c = AccentColor;
            OverlayBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
            OverlayIcon.Foreground = new SolidColorBrush(c);
        }

        /// <summary>DragEnter y DragOver comparten la misma lógica de aceptación.</summary>
        private void OnDragEnter(object sender, DragEventArgs e) => OnDragOver(sender, e);

        /// <summary>
        /// Mientras se arrastra sobre la página: si el contenido incluye
        /// elementos del sistema de archivos se acepta la operación y se muestra
        /// el overlay; si no, se oculta el overlay pero NO se rechaza la operación.
        /// Dejar AcceptedOperation sin tocar permite que los drags internos de
        /// controles hijos (p. ej. el reorder con CanReorderItems de un ListView)
        /// sigan funcionando, en vez de cancelarse por este ancestro.
        /// Reinicia el timer anti-parpadeo en cada paso para mantener el overlay
        /// visible mientras se está encima.
        /// </summary>
        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = Caption;
                ShowOverlay();
                _hideTimer.Stop();
            }
            else
            {
                HideOverlay();
            }
        }

        /// <summary>
        /// Al salir del área se programa la ocultación del overlay: sin este
        /// retardo, pasar por los elementos hijos haría parpadear el overlay.
        /// </summary>
        private void OnDragLeave(object sender, DragEventArgs e)
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        /// <summary>Oculta el overlay cuando vence el retardo programado en DragLeave.</summary>
        private void HideTimer_Tick(object? sender, object e)
        {
            _hideTimer.Stop();
            HideOverlay();
        }

        /// <summary>
        /// Suelta del arrastre: oculta el overlay e intenta leer las rutas
        /// recibidas (carpetas y archivos), notificándolas mediante
        /// <see cref="FilesDropped"/>. El consumidor decide si aceptarlas
        /// (p. ej. ignorarlas si está ocupado).
        ///
        /// Robustez Win10: GetStorageItemsAsync puede fallar aunque el overlay
        /// se haya mostrado; en ese caso (o si ninguna ruta viene legible) se
        /// registra en crash.log y se notifica <see cref="DropFailed"/> con un
        /// mensaje mostrable, en vez de quedarse en silencio. La ruta de éxito
        /// queda intacta.
        /// </summary>
        private async void OnDrop(object sender, DragEventArgs e)
        {
            _hideTimer.Stop();
            HideOverlay();

            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            IReadOnlyList<Windows.Storage.IStorageItem>? items;
            try
            {
                items = await e.DataView.GetStorageItemsAsync();
            }
            catch (Exception ex)
            {
                App.Log("DropTarget", $"GetStorageItemsAsync: {ex.Message}", ex.StackTrace);
                DropFailed?.Invoke(this, new DropFailedEventArgs(
                    "No se pudo leer lo soltado. Prueba con los botones Carpeta/Archivos."));
                return;
            }

            if (items == null || items.Count == 0) return;

            var paths = items
                .Select(i => i.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();
            if (paths.Length == 0)
            {
                DropFailed?.Invoke(this, new DropFailedEventArgs(
                    "Lo soltado no trae rutas legibles (p. ej. carpeta virtual o comprimido). Prueba con los botones Carpeta/Archivos."));
                return;
            }

            FilesDropped?.Invoke(this, new DropFilesEventArgs(paths));
        }

        /// <summary>Muestra el overlay oscuro de arrastre.</summary>
        private void ShowOverlay() => Overlay.Visibility = Visibility.Visible;

        /// <summary>Oculta el overlay oscuro de arrastre.</summary>
        private void HideOverlay() => Overlay.Visibility = Visibility.Collapsed;

        /// <summary>Al desmontarse se detiene el timer anti-parpadeo.</summary>
        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _hideTimer.Stop();
        }
    }
}
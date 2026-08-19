using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;

namespace Remove_Top.Features.AudioPreview
{
    /// <summary>
    /// Control de forma de onda con playhead y scrub (arrastrar para adelantar/
    /// atrasar). Se construye SIN dependencias externas: los picos se dibujan
    /// con una sola Path (un segmento vertical por columna) y el playhead con
    /// una Line.
    ///
    /// API pública:
    ///   - SetData(WaveformData): asigna los picos y redibuja la onda.
    ///   - SetPlayhead(fracción 0..1): mueve el playhead (lo ignora al arrastrar).
    ///   - SetAccentColor(Color): cambia el color de la onda.
    ///   - SeekRequested: se dispara al soltar un scrub, con la fracción elegida.
    /// </summary>
    public sealed partial class WaveformView : UserControl
    {
        private WaveformData? _data;
        private bool _isPointerDown;
        private bool _isScrubbing;

        public WaveformView()
        {
            InitializeComponent();
            // Color por defecto: naranja (el acento de Duplicados). La página
            // puede cambiarlo con SetAccentColor si reutiliza el control.
            WavePath.Stroke = CreateAccentBrush();
            WavePath.StrokeThickness = 1.5;
        }

        /// <summary>Se dispara al soltar un scrub, con la fracción (0..1) de la posición elegida.</summary>
        public event Action<double>? SeekRequested;

        /// <summary>Asigna los peaks de la onda y redibuja la geometría.</summary>
        public void SetData(WaveformData data)
        {
            _data = data;
            _isPointerDown = false;
            _isScrubbing = false;

            if (data.IsEmpty)
            {
                WavePath.Data = null;
                PlayheadLine.Visibility = Visibility.Collapsed;
                FutureOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            PlayheadLine.Visibility = Visibility.Visible;
            FutureOverlay.Visibility = Visibility.Visible;
            RebuildGeometry();
            SetPlayhead(0);
        }

        /// <summary>Mueve el playhead a la fracción indicada (ignorado durante un scrub).</summary>
        public void SetPlayhead(double fraction)
        {
            if (_isScrubbing || _data == null || _data.IsEmpty || ActualWidth <= 0) return;
            UpdatePlayhead(Math.Clamp(fraction, 0, 1));
        }

        /// <summary>Cambia el color de la onda.</summary>
        public void SetAccentColor(Windows.UI.Color color)
        {
            WavePath.Stroke = new SolidColorBrush(color);
        }

        /// <summary>
        /// Construye la geometría de la onda: un PathFigure por columna con un
        /// único segmento vertical desde el mínimo hasta el máximo, relativo al
        /// centro. Se reconstruye al cambiar el tamaño o los datos.
        /// </summary>
        private void RebuildGeometry()
        {
            if (_data == null || _data.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0) return;

            double width = ActualWidth;
            double height = ActualHeight;
            double mid = height / 2.0;
            double amp = Math.Max(1, height / 2.0 - 2);
            int columns = _data.Columns;
            double columnWidth = width / columns;
            double xCenter = columnWidth / 2.0;

            var geometry = new PathGeometry();
            for (int i = 0; i < columns; i++)
            {
                float min = Math.Clamp(_data.MinPeaks[i], -1f, 1f);
                float max = Math.Clamp(_data.MaxPeaks[i], -1f, 1f);
                double x = i * columnWidth + xCenter;
                double yTop = mid + min * amp;
                double yBottom = mid + max * amp;

                var figure = new PathFigure
                {
                    StartPoint = new Point(x, yTop),
                    IsFilled = false
                };
                figure.Segments.Add(new LineSegment { Point = new Point(x, yBottom) });
                geometry.Figures.Add(figure);
            }
            WavePath.Data = geometry;
        }

        /// <summary>Coloca el playhead y ajusta el overlay que atenúa el tramo futuro.</summary>
        private void UpdatePlayhead(double fraction)
        {
            double x = fraction * Math.Max(0, ActualWidth);
            PlayheadLine.X1 = x;
            PlayheadLine.X2 = x;
            PlayheadLine.Y1 = 0;
            PlayheadLine.Y2 = ActualHeight;
            FutureOverlay.Width = Math.Max(0, ActualWidth - x);
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RebuildGeometry();
        }

        // ================================================================
        // Scrub: arrastrar sobre la onda para adelantar/atrasar
        // ================================================================

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_data == null || _data.IsEmpty) return;
            _isPointerDown = true;
            _isScrubbing = true;
            RootGrid.CapturePointer(e.Pointer);
            UpdateScrub(e);
        }

        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            // Durante el arrastre solo se mueve el playhead (vista previa);
            // el seek real se confirma al soltar.
            if (_isPointerDown) UpdateScrub(e);
        }

        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPointerDown) return;
            _isPointerDown = false;
            RootGrid.ReleasePointerCapture(e.Pointer);
            double fraction = PositionFromEvent(e);
            _isScrubbing = false;
            SeekRequested?.Invoke(fraction);
        }

        private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isPointerDown = false;
            _isScrubbing = false;
        }

        /// <summary>Convierte la posición X del puntero en una fracción 0..1 y actualiza el playhead.</summary>
        private void UpdateScrub(PointerRoutedEventArgs e)
        {
            UpdatePlayhead(PositionFromEvent(e));
        }

        private double PositionFromEvent(PointerRoutedEventArgs e)
        {
            if (ActualWidth <= 0) return 0;
            var position = e.GetCurrentPoint(RootGrid).Position.X;
            return Math.Clamp(position / ActualWidth, 0, 1);
        }

        private static SolidColorBrush CreateAccentBrush()
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(230, 230, 126, 34));
        }
    }
}
using FluentIcons.Common;
using Microsoft.UI.Xaml;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Remove_Top.Features.FormatConverter
{
    /// <summary>Formato de salida del Convertidor Voz (siempre estéreo 44.1 kHz).</summary>
    public enum ConverterFormat
    {
        /// <summary>WAV PCM 16-bit (máxima calidad, para editar/archivar).</summary>
        Wav,

        /// <summary>MP3 320 kbps (máxima calidad MP3, para compartir/reproducir).</summary>
        Mp3_320
    }

    /// <summary>Opciones elegidas en el paso 2 de la página (formato + mejoras).</summary>
    public sealed class ConverterOptions
    {
        public ConverterFormat Format { get; set; } = ConverterFormat.Mp3_320;
        /// <summary>Quitar ruido de fondo (afftdn si hay ffmpeg, si no gate managed).</summary>
        public bool RemoveNoise { get; set; } = true;
        /// <summary>Mejora de voz (EQ presencia + compresor + sonoridad a −16 LUFS).</summary>
        public bool EnhanceVoice { get; set; } = true;
    }

    /// <summary>
    /// Ítem de la cola de conversión (bindeo del ListView de la página).
    /// Implementa <see cref="INotifyPropertyChanged"/> para que los cambios
    /// de estado ("Revisando…" → "En cola" → "Listo") se reflejen en la fila
    /// sin recargar la lista (sin esto la UI se quedaba congelada).
    /// </summary>
    public sealed class ConverterItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";

        private string _status = "En cola";
        /// <summary>Estado corto: "En cola", "Revisando…", "Convirtiendo…", "Listo", "Error", "No legible".</summary>
        public string Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(CanPreview));
                    OnPropertyChanged(nameof(PreviewVisibility));
                }
            }
        }

        private string _detail = "";
        /// <summary>Detalle (pico/LUFS de salida o motivo del error).</summary>
        public string Detail
        {
            get => _detail;
            set => SetProperty(ref _detail, value);
        }

        private string _outputPath = "";
        /// <summary>Ruta del archivo de salida cuando terminó bien (para el play).</summary>
        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        private bool _success;
        public bool Success
        {
            get => _success;
            set
            {
                if (SetProperty(ref _success, value))
                    OnPropertyChanged(nameof(StatusIcon));
            }
        }

        public Icon StatusIcon => Success ? Icon.CheckmarkCircle : Icon.Clock;

        /// <summary>
        /// El play por fila solo existe antes de convertir (como el análisis de
        /// Normalización): tras la conversión la fila es solo resultado.
        /// </summary>
        public bool CanPreview => Status is "En cola" or "Revisando…";

        /// <summary>Visibilidad del play por fila (WinUI 3 no trae conversor bool→Visibility).</summary>
        public Visibility PreviewVisibility => CanPreview ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Asigna el campo y notifica si cambió. Devuelve si hubo cambio.</summary>
        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Resultado de convertir un archivo.</summary>
    public sealed class ConverterResult
    {
        public string FileName { get; set; } = "";
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public double OutputPeakDb { get; set; }
        public double OutputLufs { get; set; }
        public Icon StatusIcon => Success ? Icon.CheckmarkCircle : Icon.DismissCircle;
    }

    /// <summary>Progreso por archivo para la UI (patrón IProgress como el resto de features).</summary>
    public sealed class ConverterProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentFile { get; set; } = "";
        public ConverterResult? Result { get; set; }
        public double Percentage => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0;
    }
}

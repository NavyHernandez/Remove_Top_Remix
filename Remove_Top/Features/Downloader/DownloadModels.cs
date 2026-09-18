using FluentIcons.Common;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Remove_Top.Features.Downloader
{
    /// <summary>Estado de un enlace dentro del flujo de descarga + masterización.</summary>
    public enum DownloadStatus
    {
        Pending,
        Invalid,
        Downloading,
        Downloaded,
        Processing,
        Done,
        Error,
        Canceled
    }

    /// <summary>
    /// Fila de la lista de la página de Descarga. Notifica cambios para que el
    /// ListView refresque el nombre, el porcentaje y el estado en vivo mientras
    /// yt-dlp descarga y AudioNormalizer masteriza.
    /// </summary>
    public sealed class DownloadItem : INotifyPropertyChanged
    {
        private string _title = "";
        private DownloadStatus _status = DownloadStatus.Pending;
        private double _percentage;
        private string _message = "";

        public DownloadItem(string url)
        {
            Url = url;
        }

        /// <summary>URL normalizada del enlace de YouTube.</summary>
        public string Url { get; }

        /// <summary>Título detectado por yt-dlp (vacío hasta que empieza la descarga).</summary>
        public string Title
        {
            get => _title;
            set { if (_title == value) return; _title = value; OnChanged(); OnChanged(nameof(DisplayName)); }
        }

        /// <summary>Texto principal de la fila (título si existe, si no la URL).</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(_title) ? Url : _title;

        public DownloadStatus Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; OnChanged(); OnChanged(nameof(StatusIcon)); }
        }

        /// <summary>Progreso 0-100 de la descarga o de la masterización del archivo.</summary>
        public double Percentage
        {
            get => _percentage;
            set { if (System.Math.Abs(_percentage - value) < 0.01) return; _percentage = value; OnChanged(); OnChanged(nameof(PercentageDisplay)); }
        }

        /// <summary>Mensaje secundario de la fila (estado, errores, etc.).</summary>
        public string Message
        {
            get => _message;
            set { if (_message == value) return; _message = value; OnChanged(); }
        }

        /// <summary>Icono Fluent que representa el estado actual de la fila.</summary>
        public Icon StatusIcon => _status switch
        {
            DownloadStatus.Invalid => Icon.Warning,
            DownloadStatus.Downloading => Icon.ArrowDownload,
            DownloadStatus.Downloaded => Icon.CheckmarkCircle,
            DownloadStatus.Processing => Icon.ArrowSyncCircle,
            DownloadStatus.Done => Icon.CheckmarkCircle,
            DownloadStatus.Error => Icon.DismissCircle,
            DownloadStatus.Canceled => Icon.DismissCircle,
            _ => Icon.Link
        };

        /// <summary>Porcentaje formateado, visible solo mientras hay trabajo en curso.</summary>
        public string PercentageDisplay => _status is DownloadStatus.Downloading or DownloadStatus.Processing
            ? $"{_percentage:F0}%"
            : "";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Progreso de una descarga individual (reportado por YtDlpService).</summary>
    public sealed class DownloadProgress
    {
        /// <summary>Título detectado (puede cambiar al inicio de la descarga).</summary>
        public string Title { get; set; } = "";

        /// <summary>Progreso 0-100 de la descarga del stream.</summary>
        public double Percentage { get; set; }

        /// <summary>Línea de estado para la UI (p. ej. "Descargando audio..." o "Convirtiendo a WAV...").</summary>
        public string Message { get; set; } = "";
    }

    /// <summary>Resultado de una descarga (ruta del WAV generado o error).</summary>
    public sealed class DownloadResult
    {
        public string Title { get; set; } = "";
        public string? OutputPath { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = "";

        /// <summary>El fallo fue el bloqueo anti-bot de YouTube (permite reintentar con otra estrategia).</summary>
        public bool IsBotCheck { get; set; }

        /// <summary>Este intento usó las cookies de la cuenta de YouTube del usuario.</summary>
        public bool UsedCookies { get; set; }

        /// <summary>Las cookies fueron rechazadas (sesión muerta: pide re-login, no más reintentos con ellas).</summary>
        public bool CookiesRejected { get; set; }
    }

    /// <summary>Progreso del aprovisionamiento del motor (Python/yt-dlp/deno/ffmpeg).</summary>
    public sealed class ToolProgress
    {
        public string Status { get; set; } = "";
        public double Percentage { get; set; }
    }
}

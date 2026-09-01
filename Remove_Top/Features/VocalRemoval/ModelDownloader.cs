using Remove_Top.Helpers;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.VocalRemoval
{
    /// <summary>Datos de progreso de la descarga del modelo para la UI.</summary>
    public class ModelProgress
    {
        public string Status { get; set; } = "";
        public double Percentage { get; set; }
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
    }

    /// <summary>
    /// Descarga el modelo HT-Demucs FT (ONNX) desde HuggingFace a
    /// %LOCALAPPDATA%\Remove_Top\models. Reporta progreso mediante IProgress
    /// y soporta cancelación. Una descarga se considera válida solo si el
    /// archivo supera un tamaño mínimo (evita aceptar archivos corruptos o vacíos).
    /// </summary>
    public class ModelDownloader
    {
        private const long MinValidSize = 100_000_000;

        private static readonly string ModelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppLimits.AppDataFolderName, "models");

        private const string ModelFile = "htdemucs_ft_vocals.onnx";
        private const string ModelUrl =
            "https://huggingface.co/StemSplitio/htdemucs-ft-vocals-onnx/resolve/main/htdemucs_ft_vocals.onnx";

        /// <summary>Devuelve true si el modelo ya está descargado y con tamaño válido.</summary>
        public static bool ModelsExist()
        {
            var path = Path.Combine(ModelsDir, ModelFile);
            var info = new FileInfo(path);
            return info.Exists && info.Length > MinValidSize;
        }

        /// <summary>Ruta completa al archivo del modelo ONNX (aunque no exista todavía).</summary>
        public static string GetModelPath()
        {
            return Path.Combine(ModelsDir, ModelFile);
        }

        /// <summary>
        /// Descarga el modelo completo con streaming a disco. Reporta el progreso
        /// en porcentaje y MB descargados; si el servidor no informa el tamaño total,
        /// estima el progreso contra un valor fijo (350 MB).
        /// </summary>
        public async Task DownloadModelsAsync(
            IProgress<ModelProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(ModelsDir);
            var outputPath = Path.Combine(ModelsDir, ModelFile);

            progress?.Report(new ModelProgress
            {
                Status = "Descargando HT-Demucs FT (316 MB)...",
                Percentage = 0,
                BytesDownloaded = 0,
                TotalBytes = 0
            });

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30);

            using var response = await httpClient.GetAsync(
                ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var totalBytesKnown = totalBytes > 0;

            await using var contentStream = await response.Content
                .ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192 * 16];
            long bytesRead = 0;
            int bytesJustRead;

            while ((bytesJustRead = await contentStream.ReadAsync(
                buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesJustRead,
                    cancellationToken);
                bytesRead += bytesJustRead;

                var pct = totalBytesKnown
                    ? (double)bytesRead / totalBytes * 100.0
                    : (double)bytesRead / (350 * 1024 * 1024) * 100.0;

                double mb = bytesRead / (1024.0 * 1024.0);
                progress?.Report(new ModelProgress
                {
                    Status = totalBytesKnown
                        ? $"Descargando... {mb:F0} MB de {totalBytes / (1024 * 1024)} MB"
                        : $"Descargando... {mb:F0} MB",
                    Percentage = Math.Min(pct, 99.9),
                    BytesDownloaded = bytesRead,
                    TotalBytes = totalBytes
                });
            }

            progress?.Report(new ModelProgress
            {
                Status = "Modelo listo",
                Percentage = 100,
                BytesDownloaded = 1,
                TotalBytes = 1
            });
        }
    }
}

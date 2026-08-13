using FluentIcons.Common;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.Normalization
{
    /// <summary>Resultado del análisis de un archivo (medición de pico dBFS).</summary>
    public class AnalysisResult
    {
        public string FileName { get; set; } = "";
        public double PeakDb { get; set; }
        public string PeakDbDisplay => $"{PeakDb:F1} dBFS";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public Icon StatusIcon => Success ? Icon.SoundWaveCircle : Icon.Warning;
    }

    /// <summary>
    /// Representa el resultado de la normalización de un archivo de audio.
    /// Se usa como ItemSource del ListView en la UI.
    /// </summary>
    public class NormalizationResult
    {
        public string FileName { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public double OriginalPeakDb { get; set; }
        public double AppliedGainDb { get; set; }
        public string OutputPath { get; set; } = "";
        /// <summary>Muestra la ganancia aplicada con formato: "+2.5 dB" o "-1.3 dB"</summary>
        public string GainDisplay => AppliedGainDb >= 0
            ? $"+{AppliedGainDb:F1} dB"
            : $"{AppliedGainDb:F1} dB";
        /// <summary>Icono visual de éxito/error (usado en el ListView)</summary>
        public Icon StatusIcon => Success ? Icon.CheckmarkCircle : Icon.DismissCircle;
    }

    /// <summary>
    /// Servicio de normalización de audio.
    /// Lee archivos de audio con NAudio (MediaFoundationReader), calcula el pico,
    /// aplica ganancia lineal para alcanzar el dBFS objetivo y exporta a WAV.
    /// Sobre el audio ya normalizado aplica además una cadena de masterización
    /// ligera (paso alto → EQ → compresor → limitador) para mejorar la calidad.
    /// </summary>
    public class AudioNormalizer
    {
        private static readonly string[] AudioExtensions =
        [
            ".mp3", ".wav", ".flac", ".aac", ".m4a",
            ".ogg", ".wma", ".aiff", ".aif", ".wv"
        ];

        /// <summary>Verifica si la extensión del archivo corresponde a un formato de audio soportado.</summary>
        public static bool IsAudioFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext != null && AudioExtensions.Contains(ext);
        }

        /// <summary>
        /// Límite de archivos analizados/procesados por ejecución (versión gratuita).
        /// Valor centralizado en <see cref="AppLimits.NormalizationMaxFilesToScan"/>.
        /// </summary>
        public const int MaxFilesToScan = AppLimits.NormalizationMaxFilesToScan;

        /// <summary>
        /// Límite mostrado en la UI (versión gratuita). El procesamiento real
        /// sigue usando <see cref="MaxFilesToScan"/>. Valor centralizado en
        /// <see cref="AppLimits.NormalizationFreeLimitDisplay"/>.
        /// </summary>
        public const int FreeLimitDisplay = AppLimits.NormalizationFreeLimitDisplay;

        /// <summary>Nombre de la subcarpeta donde se guardan los archivos procesados.</summary>
        public const string OutputFolderName = "RemoveTop_Normalized";

        /// <summary>
        /// Busca los archivos de audio dentro de una carpeta (búsqueda recursiva)
        /// aplicando el límite de la versión gratuita (MaxFilesToScan).
        /// Omite los archivos que ya tienen una salida procesada válida en
        /// "RemoveTop_Normalized" (no se vuelven a normalizar).
        /// Devuelve un array vacío si la carpeta no existe o hay error de permisos.
        /// </summary>
        /// <param name="folderPath">Carpeta a escanear.</param>
        /// <param name="totalFound">Archivos de audio pendientes de procesar (antes de aplicar el límite).</param>
        /// <param name="alreadyProcessed">Archivos omitidos porque ya tienen una salida procesada.</param>
        public static string[] GetAudioFiles(string folderPath, out int totalFound, out int alreadyProcessed)
        {
            totalFound = 0;
            alreadyProcessed = 0;
            if (!Directory.Exists(folderPath))
                return [];

            try
            {
                var pending = new List<string>();
                foreach (var file in EnumerateAudioFilesRecursive(folderPath))
                {
                    if (HasProcessedOutput(file))
                        alreadyProcessed++;
                    else
                        pending.Add(file);
                }

                totalFound = pending.Count;
                return pending.Take(MaxFilesToScan).ToArray();
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Calcula la ruta donde se guardaría la salida procesada de un archivo:
        /// la subcarpeta "RemoveTop_Normalized" junto al origen, con el nombre
        /// base del archivo y extensión .wav.
        /// </summary>
        private static string GetExpectedOutputPath(string sourcePath)
        {
            return Path.Combine(
                Path.GetDirectoryName(sourcePath)!,
                OutputFolderName,
                Path.GetFileNameWithoutExtension(sourcePath) + ".wav");
        }

        /// <summary>
        /// Calcula la ruta corregida (ortografía) donde se guardaría la salida
        /// procesada de un archivo, aplicando SpanishNameCorrector al nombre base.
        /// </summary>
        private static string GetCorrectedOutputPath(string sourcePath)
        {
            var baseName = Path.GetFileNameWithoutExtension(sourcePath);
            var corrected = SpanishNameCorrector.CorrectTitle(baseName);
            return Path.Combine(
                Path.GetDirectoryName(sourcePath)!,
                OutputFolderName,
                corrected + ".wav");
        }

        /// <summary>
        /// Indica si un archivo ya fue procesado. Para considerarlo procesado,
        /// la salida esperada (con nombre original o corregido ortográficamente)
        /// debe existir y ser un WAV válido (cabecera RIFF/WAVE y tamaño mínimo).
        /// Si el WAV está parcial o corrupto (p.ej. por un proceso cortado),
        /// devuelve false para que se reprocese.
        /// </summary>
        private static bool HasProcessedOutput(string sourcePath)
        {
            // Verificar salida con nombre original
            var outputPath = GetExpectedOutputPath(sourcePath);
            if (IsValidWav(outputPath))
                return true;

            // Verificar salida con nombre corregido ortográficamente
            var correctedPath = GetCorrectedOutputPath(sourcePath);
            if (string.Equals(outputPath, correctedPath, StringComparison.OrdinalIgnoreCase))
                return false; // No hay variante corregida distinta

            return IsValidWav(correctedPath);
        }

        /// <summary>
        /// Verifica si un archivo existe y es un WAV válido (cabecera RIFF/WAVE).
        /// </summary>
        private static bool IsValidWav(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                var info = new FileInfo(path);
                if (info.Length < 44)
                    return false;

                using var stream = File.OpenRead(path);
                Span<byte> header = stackalloc byte[12];
                if (stream.Read(header) != 12)
                    return false;

                return header[0] == (byte)'R' && header[1] == (byte)'I' &&
                       header[2] == (byte)'F' && header[3] == (byte)'F' &&
                       header[8] == (byte)'W' && header[9] == (byte)'A' &&
                       header[10] == (byte)'V' && header[11] == (byte)'E';
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Enumera los archivos de audio de forma recursiva, sin entrar en la
        /// carpeta de resultados "RemoveTop_Normalized". Así no se vuelven a
        /// procesar archivos ya normalizados en ejecuciones anteriores.
        /// Tolerante a errores de permisos por carpeta.
        /// </summary>
        private static IEnumerable<string> EnumerateAudioFilesRecursive(string folderPath)
        {
            // Archivos de la carpeta actual
            string[] files;
            try
            {
                files = Directory.GetFiles(folderPath);
            }
            catch
            {
                files = [];
            }

            foreach (var file in files)
            {
                if (IsAudioFile(file))
                    yield return file;
            }

            // Subcarpetas (se omite la carpeta de resultados)
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(folderPath);
            }
            catch
            {
                directories = [];
            }

            foreach (var directory in directories)
            {
                if (string.Equals(Path.GetFileName(directory), OutputFolderName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var file in EnumerateAudioFilesRecursive(directory))
                    yield return file;
            }
        }

        /// <summary>
        /// Analiza un único archivo de audio: lee la señal completa, encuentra el pico
        /// máximo en floats y lo convierte a dBFS. No modifica el archivo.
        /// </summary>
        public static AnalysisResult AnalyzeFile(string path)
        {
            try
            {
                using var reader = new MediaFoundationReader(path);
                var format = reader.WaveFormat;
                var sampleProvider = reader.ToSampleProvider();
                int bufferSize = format.SampleRate * format.Channels;
                var sampleBuffer = new float[bufferSize];

                float peak = 0f;
                int samplesRead;
                while ((samplesRead = sampleProvider.Read(sampleBuffer, 0, bufferSize)) > 0)
                {
                    for (int j = 0; j < samplesRead; j++)
                    {
                        float abs = Math.Abs(sampleBuffer[j]);
                        if (abs > peak) peak = abs;
                    }
                }

                if (peak <= 0f)
                    return new AnalysisResult
                    {
                        FileName = Path.GetFileName(path),
                        Success = false,
                        Message = "Silencio (pico cero)"
                    };

                double peakDb = 20.0 * Math.Log10(peak);
                return new AnalysisResult
                {
                    FileName = Path.GetFileName(path),
                    Success = true,
                    PeakDb = peakDb,
                    Message = "OK"
                };
            }
            catch (Exception ex)
            {
                return new AnalysisResult
                {
                    FileName = Path.GetFileName(path),
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Analiza múltiples archivos de audio de forma asíncrona.
        /// Reporta cada resultado mediante IProgress a medida que se completa.
        /// </summary>
        public async Task<AnalysisResult[]> AnalyzeFilesAsync(
            string[] files,
            IProgress<AnalysisResult> progress,
            CancellationToken cancellationToken = default)
        {
            var results = new AnalysisResult[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[i] = await Task.Run(() => AnalyzeFile(files[i]), cancellationToken);
                progress.Report(results[i]);
            }
            return results;
        }

        /// <summary>
        /// Procesa múltiples archivos de audio de forma asíncrona.
        /// Reporta progreso mediante IProgress y soporta cancelación via CancellationToken.
        /// Cada archivo se procesa en una tarea separada (Task.Run) para no bloquear la UI.
        /// </summary>
        public async Task ProcessFilesAsync(
            string[] files,
            double targetDbFs,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken = default)
        {
            int total = files.Length;
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = files[i];
                NormalizationResult result;

                try
                {
                    result = await Task.Run(() => NormalizeFile(file, targetDbFs), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result = new NormalizationResult
                    {
                        FileName = Path.GetFileName(file),
                        Success = false,
                        Message = $"ERROR: {ex.Message}"
                    };
                }

                progress.Report(new NormalizationProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = total,
                    CurrentFile = Path.GetFileName(file),
                    Result = result
                });
            }
        }

        /// <summary>
        /// Normaliza un único archivo de audio.
        ///   
        ///   Paso 1: Lee el archivo, encuentra el pico máximo (en floats).
        ///   Paso 2: Calcula la ganancia necesaria: gain = targetDbFs - peakDb.
        ///   Paso 3: Vuelve a leer el archivo, aplica la ganancia y, sobre ese
        ///           audio ya normalizado, una cadena de masterización ligera
        ///           (paso alto → EQ → compresor → limitador a -0.3 dB).
        ///   
        /// El archivo procesado se guarda en una subcarpeta "RemoveTop_Normalized"
        /// con el sufijo "_normalized.wav".
        /// </summary>
        private NormalizationResult NormalizeFile(string inputPath, double targetDbFs)
        {
            var outputDir = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                OutputFolderName);
            Directory.CreateDirectory(outputDir);

            //var outputName = Path.GetFileNameWithoutExtension(inputPath) + "_normalized.wav";
            var outputName = Path.GetFileNameWithoutExtension(inputPath) + ".wav";
            var outputPath = Path.Combine(outputDir, outputName);

            using var reader = new MediaFoundationReader(inputPath);
            var format = reader.WaveFormat;

            var sampleProvider = reader.ToSampleProvider();
            int bufferSize = format.SampleRate * format.Channels;
            var sampleBuffer = new float[bufferSize];
            int samplesRead;

            // --- Paso 1: Encontrar el pico máximo en la señal ---
            float peak = 0f;
            while ((samplesRead = sampleProvider.Read(sampleBuffer, 0, bufferSize)) > 0)
            {
                for (int j = 0; j < samplesRead; j++)
                {
                    float abs = Math.Abs(sampleBuffer[j]);
                    if (abs > peak) peak = abs;
                }
            }

            if (peak <= 0f)
                throw new InvalidOperationException("El archivo está en silencio (pico cero).");

            // --- Paso 2: Calcular ganancia ---
            double originalPeakDb = 20.0 * Math.Log10(peak);
            double gainDb = targetDbFs - originalPeakDb;
            double gainLinear = Math.Pow(10.0, gainDb / 20.0);

            // --- Paso 3: Aplicar ganancia + masterización ligera y escribir ---
            reader.Position = 0;
            sampleProvider = reader.ToSampleProvider();

            // Primero la ganancia de normalización definida por el usuario...
            var gained = new VolumeSampleProvider(sampleProvider)
            {
                Volume = (float)gainLinear
            };

            // ...y sobre ese audio ya normalizado, la cadena de masterización ligera
            // (paso alto → EQ paramétrico → compresor → limitador a -0.3 dB).
            var mastered = MasteringChain.Build(gained, format, new MasteringSettings());

            using var writer = new WaveFileWriter(outputPath, format);
            while ((samplesRead = mastered.Read(sampleBuffer, 0, bufferSize)) > 0)
            {
                // El limitador ya evita superar el techo (-0.3 dB); este clamp es
                // una red de seguridad para no escribir floats fuera de rango.
                for (int j = 0; j < samplesRead; j++)
                {
                    if (sampleBuffer[j] > 1f) sampleBuffer[j] = 1f;
                    else if (sampleBuffer[j] < -1f) sampleBuffer[j] = -1f;
                }
                writer.WriteSamples(sampleBuffer, 0, samplesRead);
            }

            return new NormalizationResult
            {
                FileName = Path.GetFileName(inputPath),
                Success = true,
                Message = $"Normalizado a {targetDbFs:F1} dBFS · masterizado ligero",
                OriginalPeakDb = originalPeakDb,
                AppliedGainDb = gainDb,
                OutputPath = outputPath
            };
        }

        /// <summary>
        /// Corrige ortográficamente los nombres de las salidas procesadas.
        /// Por cada resultado exitoso, calcula el nombre corregido con
        /// <see cref="SpanishNameCorrector.CorrectTitle"/> y renombra el archivo
        /// en disco si difiere del actual. Si el destino ya existe, agrega
        /// sufijo " (1)", "(2)", etc.
        /// Actualiza FileName, OutputPath y Message del resultado.
        /// </summary>
        public static void CorrectOutputNames(IReadOnlyList<NormalizationResult> results)
        {
            foreach (var result in results)
            {
                if (!result.Success || string.IsNullOrEmpty(result.OutputPath))
                    continue;

                var currentDir = Path.GetDirectoryName(result.OutputPath)!;
                var currentNameWithoutExt = Path.GetFileNameWithoutExtension(result.OutputPath);
                var ext = Path.GetExtension(result.OutputPath);

                var correctedNameWithoutExt = SpanishNameCorrector.CorrectTitle(currentNameWithoutExt);

                // Si el nombre ya es correcto, no hacer nada
                if (string.Equals(currentNameWithoutExt, correctedNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                    continue;

                var correctedPath = Path.Combine(currentDir, correctedNameWithoutExt + ext);

                // Si el destino corregido ya existe, agregar sufijo numérico
                if (File.Exists(correctedPath))
                {
                    var baseName = correctedNameWithoutExt;
                    int counter = 1;
                    while (File.Exists(Path.Combine(currentDir, baseName + $" ({counter})" + ext)))
                        counter++;
                    correctedPath = Path.Combine(currentDir, baseName + $" ({counter})" + ext);
                }

                try
                {
                    File.Move(result.OutputPath, correctedPath);
                    result.FileName = Path.GetFileNameWithoutExtension(result.OutputPath) + ext;
                    result.OutputPath = correctedPath;
                    result.Message += " · nombre corregido";
                }
                catch
                {
                    // Si falla el renombrado, se deja el nombre original
                }
            }
        }
    }

    /// <summary>
    /// Datos de progreso para la UI.
    /// La propiedad Percentage se calcula automáticamente.
    /// </summary>
    public class NormalizationProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentFile { get; set; } = "";
        public NormalizationResult? Result { get; set; }
        public double Percentage => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0;
    }
}

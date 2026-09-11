using FluentIcons.Common;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.Normalization
{
    /// <summary>Resultado del análisis de un archivo (medición de pico dBFS).</summary>
    public class AnalysisResult
    {
        public string FileName { get; set; } = "";
        /// <summary>Ruta completa del archivo analizado (previsualización y quitar de la lista).</summary>
        public string FilePath { get; set; } = "";
        public double PeakDb { get; set; }
        /// <summary>
        /// Pico en dBFS para mostrar. En archivos que no se pudieron analizar
        /// devuelve "—" (no "0.0 dBFS", que se confundía con una lectura real).
        /// </summary>
        public string PeakDbDisplay => Success ? $"{PeakDb:F1} dBFS" : "\u2014";
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
        public const string OutputFolderName = "OneDj_Normalized";

        /// <summary>
        /// Busca los archivos de audio dentro de una carpeta (búsqueda recursiva)
        /// aplicando el límite de la versión gratuita (MaxFilesToScan).
        /// Omite los archivos que ya tienen una salida procesada válida en
        /// "OneDj_Normalized" (no se vuelven a normalizar).
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
        /// Filtra una lista ya obtenida de archivos de audio, dejando solo los
        /// pendientes de procesar (los que ya tienen salida válida se omiten).
        /// Aplica el límite de <see cref="MaxFilesToScan"/>. Mismos conteos que
        /// <see cref="GetAudioFiles(string, out int, out int)"/> pero sobre una
        /// lista (p. ej. archivos seleccionados o arrastrados directamente).
        /// </summary>
        public static string[] GetAudioFiles(
            IEnumerable<string> files,
            out int totalFound,
            out int alreadyProcessed)
        {
            alreadyProcessed = 0;
            var pending = new List<string>();
            foreach (var file in files)
            {
                if (HasProcessedOutput(file))
                    alreadyProcessed++;
                else
                    pending.Add(file);
            }

            totalFound = pending.Count;
            return pending.Take(MaxFilesToScan).ToArray();
        }

        /// <summary>
        /// Calcula la ruta donde se guardaría la salida procesada de un archivo:
        /// la subcarpeta "OneDj_Normalized" junto al origen, con el nombre
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
            var cleaned = CleanOutputName(baseName);
            var corrected = SpanishNameCorrector.CorrectTitle(cleaned);
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
        /// carpeta de resultados "OneDj_Normalized". Así no se vuelven a
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
        /// El token se comprueba en cada bloque para que Cancelar/Limpiar
        /// interrumpa también un archivo con lectura lenta o atascada.
        /// </summary>
        public static AnalysisResult AnalyzeFile(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var reader = new MediaFoundationReader(path);
                var sampleProvider = reader.ToSampleProvider();
                // Búfer fijo: el anterior (sampleRate * channels) variaba por
                // archivo y podía reservar de más con rates inusuales.
                const int bufferSize = 32768;
                var sampleBuffer = new float[bufferSize];

                float peak = 0f;
                int samplesRead;
                while ((samplesRead = sampleProvider.Read(sampleBuffer, 0, bufferSize)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                        FilePath = path,
                        Success = false,
                        Message = "Silencio (pico cero)"
                    };

                double peakDb = 20.0 * Math.Log10(peak);
                return new AnalysisResult
                {
                    FileName = Path.GetFileName(path),
                    FilePath = path,
                    Success = true,
                    PeakDb = peakDb,
                    Message = "OK"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new AnalysisResult
                {
                    FileName = Path.GetFileName(path),
                    FilePath = path,
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Tiempo máximo de análisis por archivo. Si un MP3 se atasca en la
        /// lectura nativa (MediaFoundation no siempre responde al token), el
        /// WaitAsync libera la UI y el archivo se reporta como omitido en vez
        /// de dejar la página palpitando en "Analizando...".
        /// </summary>
        private static readonly TimeSpan AnalyzeTimeoutPerFile = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Analiza múltiples archivos de audio de forma asíncrona.
        /// Reporta cada resultado mediante IProgress a medida que se completa.
        /// Un archivo que exceda el timeout no bloquea al resto.
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
                var path = files[i];
                var task = Task.Run(() => AnalyzeFile(path, cancellationToken), CancellationToken.None);
                try
                {
                    results[i] = await task.WaitAsync(AnalyzeTimeoutPerFile, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw;
                    results[i] = new AnalysisResult
                    {
                        FileName = Path.GetFileName(path),
                        FilePath = path,
                        Success = false,
                        Message = "Análisis cancelado"
                    };
                }
                catch (TimeoutException)
                {
                    App.Log("Normalization", $"Timeout analizando {path} (> {AnalyzeTimeoutPerFile.TotalSeconds}s), se omite.");
                    results[i] = new AnalysisResult
                    {
                        FileName = Path.GetFileName(path),
                        FilePath = path,
                        Success = false,
                        Message = "Tiempo agotado analizando (archivo omitido)"
                    };
                }
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
            MasteringIntensity intensity,
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
                    result = await Task.Run(() => NormalizeFile(file, targetDbFs, intensity, cancellationToken), CancellationToken.None);
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

        /// <summary>Ganancia máxima (dB) aplicada por la normalización por loudness.</summary>
        private const double MaxLoudnessGainDb = 18.0;

        /// <summary>Ganancia mínima (dB) aplicada por la normalización por loudness.</summary>
        private const double MinLoudnessGainDb = -12.0;

        /// <summary>Corrección máxima de la 2.ª pasada de loudness (dB).</summary>
        private const double MaxLufsCorrectionDb = 6.0;

        /// <summary>
        /// Normaliza y masteriza un único archivo.
        ///
        ///   Paso 1: mide el pico y la sonoridad percibida (LUFS Integrated,
        ///           K-weighting BS.1770) del archivo de entrada.
        ///   Paso 2: calcula la ganancia.
        ///             - Ligera: por pico (target dBFS - pico), una sola pasada.
        ///             - Hard/EDM: por loudness (target LUFS - LUFS de entrada)
        ///               en DOS pasadas: la primera mide el LUFS real de salida y
        ///               la segunda aplica la corrección para clavar el objetivo.
        ///   Paso 3: aplica ganancia + cadena de masterización + dither TPDF y
        ///           escribe el WAV en la subcarpeta "OneDj_Normalized".
        /// </summary>
        private NormalizationResult NormalizeFile(
            string inputPath, double targetDbFs, MasteringIntensity intensity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputDir = Path.Combine(Path.GetDirectoryName(inputPath)!, OutputFolderName);
            Directory.CreateDirectory(outputDir);

            var outputName = Path.GetFileNameWithoutExtension(inputPath) + ".wav";
            var outputPath = Path.Combine(outputDir, outputName);

            // --- Paso 1: medir pico y LUFS de entrada ---
            double originalPeakDb;
            double inputLufs;
            WaveFormat format;
            using (var reader = new MediaFoundationReader(inputPath))
            {
                format = reader.WaveFormat;
                var sampleProvider = reader.ToSampleProvider();
                var meter = new LoudnessMeter(format.Channels, format.SampleRate);
                const int bufferSize = 32768;
                var sampleBuffer = new float[bufferSize];

                float peak = 0f;
                int samplesRead;
                while ((samplesRead = sampleProvider.Read(sampleBuffer, 0, bufferSize)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int j = 0; j < samplesRead; j++)
                    {
                        float abs = Math.Abs(sampleBuffer[j]);
                        if (abs > peak) peak = abs;
                    }
                    meter.AddSamples(sampleBuffer, 0, samplesRead);
                }

                if (peak <= 0f)
                    throw new InvalidOperationException("El archivo está en silencio (pico cero).");

                originalPeakDb = 20.0 * Math.Log10(peak);
                inputLufs = meter.IntegratedLufs;
            }

            // --- Paso 2a: Ligera normaliza por pico (comportamiento clásico) ---
            if (intensity == MasteringIntensity.Ligera)
            {
                double gainDb = targetDbFs - originalPeakDb;
                double gainLinear = Math.Pow(10.0, gainDb / 20.0);
                return Render(inputPath, outputPath, format, gainLinear, intensity, gainDb, originalPeakDb, cancellationToken);
            }

            // --- Paso 2b: Hard/EDM normalizan por loudness (LUFS) ---
            double targetLufs = MasteringChain.TargetLufs(intensity);

            double provisionalDb;
            if (double.IsNegativeInfinity(inputLufs) || double.IsNaN(inputLufs))
                provisionalDb = targetDbFs - originalPeakDb; // archivo muy corto: recae en pico
            else
                provisionalDb = targetLufs - inputLufs;

            provisionalDb = Math.Clamp(provisionalDb, MinLoudnessGainDb, MaxLoudnessGainDb);

            // Primera pasada: mide el LUFS real que produce la cadena.
            double outputLufs = MeasureChainLufs(
                inputPath, format, Math.Pow(10.0, provisionalDb / 20.0), intensity, cancellationToken);

            double correctionDb = double.IsNegativeInfinity(outputLufs) || double.IsNaN(outputLufs)
                ? 0.0
                : Math.Clamp(targetLufs - outputLufs, -MaxLufsCorrectionDb, MaxLufsCorrectionDb);

            double finalGainDb = Math.Clamp(provisionalDb + correctionDb, MinLoudnessGainDb, MaxLoudnessGainDb);

            // --- Paso 3: render final con la ganancia corregida ---
            return Render(inputPath, outputPath, format, Math.Pow(10.0, finalGainDb / 20.0),
                intensity, finalGainDb, originalPeakDb, cancellationToken);
        }

        /// <summary>
        /// Ejecuta la cadena de masterización sin escribir y devuelve el LUFS
        /// Integrated de la salida (medición de la 1.ª pasada por loudness).
        /// </summary>
        private static double MeasureChainLufs(
            string inputPath, WaveFormat format, double gainLinear, MasteringIntensity intensity, CancellationToken ct)
        {
            using var reader = new MediaFoundationReader(inputPath);
            var chain = BuildChain(reader.ToSampleProvider(), format, gainLinear, intensity);
            var meter = new LoudnessMeter(format.Channels, format.SampleRate);

            const int bufferSize = 32768;
            var buffer = new float[bufferSize];
            int samplesRead;
            while ((samplesRead = chain.Read(buffer, 0, bufferSize)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                meter.AddSamples(buffer, 0, samplesRead);
            }
            return meter.IntegratedLufs;
        }

        /// <summary>
        /// Aplica ganancia + cadena + dither TPDF y escribe el WAV. Devuelve el
        /// resultado con el pico y el LUFS reales del archivo de salida.
        /// </summary>
        private static NormalizationResult Render(
            string inputPath,
            string outputPath,
            WaveFormat format,
            double gainLinear,
            MasteringIntensity intensity,
            double appliedGainDb,
            double originalPeakDb,
            CancellationToken ct)
        {
            using var reader = new MediaFoundationReader(inputPath);
            var rendered = BuildChain(reader.ToSampleProvider(), format, gainLinear, intensity);

            // Dither TPDF como último paso, adaptado a la profundidad de salida.
            var dithered = new TpdfDitherSampleProvider(rendered, format.BitsPerSample);

            var meter = new LoudnessMeter(format.Channels, format.SampleRate);
            const int bufferSize = 32768;
            var buffer = new float[bufferSize];
            int samplesRead;
            float finalPeak = 0f;

            using var writer = new WaveFileWriter(outputPath, format);
            while ((samplesRead = dithered.Read(buffer, 0, bufferSize)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (int j = 0; j < samplesRead; j++)
                {
                    float s = buffer[j];
                    if (s > 1f) s = 1f;
                    else if (s < -1f) s = -1f;

                    buffer[j] = s;
                    float abs = Math.Abs(s);
                    if (abs > finalPeak) finalPeak = abs;
                }
                meter.AddSamples(buffer, 0, samplesRead);
                writer.WriteSamples(buffer, 0, samplesRead);
            }

            double finalPeakDb = finalPeak > 0f
                ? 20.0 * Math.Log10(finalPeak)
                : double.NegativeInfinity;
            double outputLufs = meter.IntegratedLufs;
            string lufsText = double.IsNegativeInfinity(outputLufs) ? "\u2014" : $"{outputLufs:F1}";

            return new NormalizationResult
            {
                FileName = Path.GetFileName(inputPath),
                Success = true,
                Message = $"{MasteringChain.DisplayName(intensity)} \u00b7 Pico {finalPeakDb:F1} dB \u00b7 LUFS {lufsText}",
                OriginalPeakDb = originalPeakDb,
                AppliedGainDb = appliedGainDb,
                OutputPath = outputPath
            };
        }

        /// <summary>
        /// Construye la pila: ganancia de normalización → cadena de masterización.
        /// </summary>
        private static ISampleProvider BuildChain(
            ISampleProvider source, WaveFormat format, double gainLinear, MasteringIntensity intensity)
        {
            var gained = new VolumeSampleProvider(source)
            {
                Volume = (float)gainLinear
            };
            return MasteringChain.Build(gained, format, intensity);
        }

        /// <summary>
        /// Limpia y formatea un nombre de archivo de salida:
        /// 1. Elimina paréntesis () y corchetes [] junto con su contenido.
        /// 2. Elimina las palabras "audio", "video" e "oficial" (cualquier caso).
        /// 3. Convierte a Title Case (primera letra mayúscula, resto minúscula por palabra).
        /// </summary>
        public static string CleanOutputName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;

            // 1. Eliminar contenido entre paréntesis o corchetes: (...), [...], ( [...] )
            var cleaned = Regex.Replace(name, @"\s*[\(\[][^\)\]]*[\)\]]\s*", " ");

            // 2. Eliminar palabras clave: audio, video, oficial (case-insensitive)
            cleaned = Regex.Replace(cleaned, @"\b(audio|video|oficial)\b", " ",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // 3. Limpiar espacios múltiples resultantes
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

            if (string.IsNullOrWhiteSpace(cleaned))
                return name; // Si quedó vacío, devolver el original

            // 4. Title Case: primera letra mayúscula, resto minúscula por palabra
            var sb = new StringBuilder(cleaned.Length);
            bool capitalizeNext = true;
            foreach (var c in cleaned)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == ',' || c == '.' || c == '&' || c == '\'')
                {
                    sb.Append(c);
                    capitalizeNext = true;
                }
                else if (capitalizeNext)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
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

                // Primero limpia: quita paréntesis/corchetes, palabras clave y aplica Title Case
                var cleanedName = CleanOutputName(currentNameWithoutExt);
                // Luego corrige ortografía (tildes)
                var correctedNameWithoutExt = SpanishNameCorrector.CorrectTitle(cleanedName);

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

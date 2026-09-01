using FluentIcons.Common;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.VocalRemoval
{
    /// <summary>Resultado de la extracción de stems de un archivo de audio.</summary>
    public class StemResult
    {
        public string FileName { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public Icon StatusIcon => Success ? Icon.CheckmarkCircle : Icon.DismissCircle;
    }

    /// <summary>
    /// Datos de progreso por archivo para la UI.
    /// Percentage es global (archivos completados) y FileProgress del archivo actual.
    /// </summary>
    public class StemProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentFile { get; set; } = "";
        public StemResult? Result { get; set; }
        public double Percentage =>
            TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0;
        public double FileProgress { get; set; }
    }

    /// <summary>
    /// Separador de stems con IA (modelo HT-Demucs FT en ONNX).
    /// Separa la voz del instrumental y exporta la voz en mono a una subcarpeta
    /// "RemoveTop_Vocals". Usa chunking con overlap-add y ventana de transición
    /// para evitar artefactos en los bordes de los segmentos.
    /// </summary>
    public class VocalSeparator
    {
        private const int TargetSampleRate = 44100;
        private const double SegmentDuration = 7.8;
        private const int NSamples = (int)(SegmentDuration * TargetSampleRate);
        private const double OverlapFraction = 0.25;
        private const int Overlap = (int)(NSamples * OverlapFraction);
        private const int Stride = NSamples - Overlap;
        private const int VocalsIndex = 3;
        /// <summary>
        /// Máximo de canciones estéreo por lote. Valor centralizado en
        /// <see cref="AppLimits.VocalRemovalMaxFilesPerBatch"/>.
        /// </summary>
        private const int MaxFiles = AppLimits.VocalRemovalMaxFilesPerBatch;

        private static readonly string[] AudioExtensions =
            [".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma"];

        private readonly float[] _transitionWindow;
        private readonly object _lock = new();
        private InferenceSession? _session;

        public bool IsModelLoaded { get; private set; }

        /// <summary>Crea el separador y precalcula la ventana de transición para overlap-add.</summary>
        public VocalSeparator()
        {
            _transitionWindow = BuildTransitionWindow();
        }

        /// <summary>
        /// Carga el modelo ONNX en memoria. El session se crea con un único hilo
        /// de inferencia para mantener determinismo. Devuelve false si falla.
        /// </summary>
        public bool LoadModel(string modelPath)
        {
            try
            {
                var opts = new SessionOptions();
                opts.InterOpNumThreads = 1;
                opts.IntraOpNumThreads = 1;

                lock (_lock)
                {
                    _session?.Dispose();
                    _session = new InferenceSession(modelPath, opts);
                }
                IsModelLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                IsModelLoaded = false;
                System.Diagnostics.Debug.WriteLine(
                    $"VocalSeparator.LoadModel error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Versión asíncrona de LoadModel (ejecuta la carga en un hilo de background).</summary>
        public Task<bool> LoadModelAsync(string modelPath)
        {
            return Task.Run(() => LoadModel(modelPath));
        }

        /// <summary>
        /// Construye una ventana de transición (fade-in/fade-out) usada en el
        /// overlap-add para suavizar la unión entre segmentos.
        /// </summary>
        private static float[] BuildTransitionWindow()
        {
            var w = new float[NSamples];
            Array.Fill(w, 1f);
            for (int i = 0; i < Overlap; i++)
            {
                float fade = (float)i / Overlap;
                w[i] = fade;
                w[NSamples - 1 - i] = fade;
            }
            return w;
        }

        /// <summary>Verifica si la extensión del archivo corresponde a un formato de audio soportado.</summary>
        public static bool IsAudioFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext != null && AudioExtensions.Contains(ext);
        }

        /// <summary>
        /// Lista los archivos de audio de la carpeta (sin recursión), limitado a
        /// un máximo de 5 archivos estéreo por lote. Devuelve un array vacío si
        /// la carpeta no existe o hay error de permisos.
        /// </summary>
        public static string[] GetAudioFiles(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return [];
            try
            {
                return Directory.EnumerateFiles(
                    folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsAudioFile)
                    .Take(MaxFiles)
                    .ToArray();
            }
            catch { return []; }
        }

        /// <summary>
        /// Procesa una lista de archivos de audio extrayendo la voz de cada uno.
        /// Reporta progreso por archivo mediante IProgress y soporta cancelación.
        /// Los errores por archivo no detienen el lote: se reportan como StemResult fallido.
        /// </summary>
        public async Task ProcessFilesAsync(
            string[] files,
            IProgress<StemProgress> progress,
            CancellationToken cancellationToken = default)
        {
            int total = Math.Min(files.Length, MaxFiles);
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];
                try
                {
                    var result = await Task.Run(() =>
                        ProcessFile(file, cancellationToken), cancellationToken);

                    progress.Report(new StemProgress
                    {
                        CurrentIndex = i + 1,
                        TotalCount = total,
                        CurrentFile = Path.GetFileName(file),
                        Result = result,
                        FileProgress = 100
                    });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    progress.Report(new StemProgress
                    {
                        CurrentIndex = i + 1,
                        TotalCount = total,
                        CurrentFile = Path.GetFileName(file),
                        Result = new StemResult
                        {
                            FileName = Path.GetFileName(file),
                            Success = false,
                            Message = $"ERROR: {ex.Message}"
                        },
                        FileProgress = 100
                    });
                }
            }
        }

        /// <summary>
        /// Extrae la voz de un único archivo estéreo:
        ///   1. Lee y opcionalmente resamplea la señal a 44.1 kHz.
        ///   2. Organiza la señal como [canal, muestra] y la divide en segmentos
        ///      con solape (chunking).
        ///   3. Ejecuta la inferencia ONNX por segmento y acumula con overlap-add
        ///      aplicando la ventana de transición.
        ///   4. Normaliza por el peso acumulado, mezcla a mono (L+R)/2 y limita el pico.
        ///   5. Guarda el resultado como WAV float mono en "RemoveTop_Vocals".
        /// </summary>
        private StemResult ProcessFile(string inputPath, CancellationToken ct)
        {
            var outputDir = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                "RemoveTop_Vocals");
            Directory.CreateDirectory(outputDir);

            var stemName = Path.GetFileNameWithoutExtension(inputPath) + "_vocals.wav";
            var outputPath = Path.Combine(outputDir, stemName);

            ct.ThrowIfCancellationRequested();

            using var reader = new MediaFoundationReader(inputPath);

            if (reader.WaveFormat.Channels < 2)
            {
                return new StemResult
                {
                    FileName = Path.GetFileName(inputPath),
                    Success = false,
                    Message = "El archivo debe ser est\u00e9reo"
                };
            }

            var sampleProvider = reader.ToSampleProvider();
            if (reader.WaveFormat.SampleRate != TargetSampleRate)
            {
                sampleProvider = new WdlResamplingSampleProvider(
                    sampleProvider, TargetSampleRate);
            }

            var allSamples = new List<float>();
            var readBuf = new float[TargetSampleRate * 10];
            int samplesRead;
            while ((samplesRead = sampleProvider.Read(readBuf, 0, readBuf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (int j = 0; j < samplesRead; j++)
                    allSamples.Add(readBuf[j]);
            }

            InferenceSession session;
            lock (_lock)
            {
                session = _session!;
            }

            var samples = allSamples.ToArray();
            int totalSamples = samples.Length / 2;

            // 2. Organizar como [2, totalSamples]
            var mix = new float[2][];
            mix[0] = new float[totalSamples];
            mix[1] = new float[totalSamples];
            for (int j = 0; j < totalSamples; j++)
            {
                mix[0][j] = samples[j * 2];
                mix[1][j] = samples[j * 2 + 1];
            }

            // 3. Chunking con overlap-add
            int nChunks = Math.Max(1,
                (totalSamples + Stride - 1) / Stride);

            var outL = new float[totalSamples];
            var outR = new float[totalSamples];
            var weight = new float[totalSamples];

            ct.ThrowIfCancellationRequested();

            var inputName = session.InputNames.First();

            for (int chunk = 0; chunk < nChunks; chunk++)
            {
                ct.ThrowIfCancellationRequested();

                int start = chunk * Stride;
                int end = Math.Min(start + NSamples, totalSamples);
                int chunkLen = end - start;

                var inputValue = new DenseTensor<float>(
                    new[] { 1, 2, NSamples });
                var inputSpan = inputValue.Buffer.Span;

                for (int ch = 0; ch < 2; ch++)
                {
                    var dest = inputSpan.Slice(
                        ch * NSamples, NSamples);
                    var src = mix[ch].AsSpan(start, chunkLen);
                    src.CopyTo(dest);
                }

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("mix", inputValue)
                };
                using var results = session.Run(inputs);
                var stems = results.First().AsTensor<float>();
                // stems shape: [1, 4, 2, NSamples]

                // Extraer vocals (index 3) y aplicar ventana
                for (int j = 0; j < chunkLen; j++)
                {
                    float w = _transitionWindow[j];
                    int pos = start + j;
                    if (pos < totalSamples)
                    {
                        outL[pos] += stems[0, VocalsIndex, 0, j] * w;
                        outR[pos] += stems[0, VocalsIndex, 1, j] * w;
                        weight[pos] += w;
                    }
                }
            }

            // 4. Normalizar por overlap
            for (int j = 0; j < totalSamples; j++)
            {
                if (weight[j] > 1e-8f)
                {
                    outL[j] /= weight[j];
                    outR[j] /= weight[j];
                }
            }

            // 5. Mezclar a mono (L+R)/2
            var mono = new float[totalSamples];
            float peak = 0;
            for (int j = 0; j < totalSamples; j++)
            {
                float val = (outL[j] + outR[j]) * 0.5f;
                mono[j] = val;
                float abs = Math.Abs(val);
                if (abs > peak) peak = abs;
            }

            if (peak > 0.99f)
            {
                float scale = 0.99f / peak;
                for (int j = 0; j < totalSamples; j++)
                    mono[j] *= scale;
            }

            // 6. Guardar WAV
            var outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                TargetSampleRate, 1);
            using var writer = new WaveFileWriter(outputPath, outputFormat);
            writer.WriteSamples(mono, 0, totalSamples);

            return new StemResult
            {
                FileName = Path.GetFileName(inputPath),
                Success = true,
                Message = $"Voz extra\u00edda \u2192 {stemName}",
                OutputPath = outputPath
            };
        }
    }
}

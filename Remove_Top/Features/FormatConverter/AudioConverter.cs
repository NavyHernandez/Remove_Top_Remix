using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Remove_Top.Features.Normalization;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.FormatConverter
{
    /// <summary>
    /// Servicio de conversión OGG/MPEG → WAV o MP3 320 kbps, siempre en
    /// estéreo 44.1 kHz, con reducción de ruido y mejora de voz opcionales.
    ///
    /// Etapas por archivo: (1) denoise <c>afftdn</c> a nivel archivo si hay
    /// ffmpeg; (2) decodificación + subida a estéreo + resample 44.1 kHz
    /// calidad 60 a un WAV temporal estándar; (3) ganancia por sonoridad a
    /// −16 LUFS en 2 pasadas (mide la salida real y corrige, como el
    /// normalizador); (4) cadena de voz + dither + escritura WAV o MP3 320k;
    /// (5) medición del pico y LUFS reales del archivo final.
    /// </summary>
    public sealed class AudioConverter
    {
        private static readonly string[] SupportedExtensions =
        [
            ".ogg", ".oga", ".opus", ".mp3", ".wav", ".flac", ".aac", ".m4a",
            ".mp4", ".wma", ".aiff", ".aif", ".wv",
            ".mpeg", ".mpg", ".mp1", ".mp2", ".mpa", ".m1a", ".m2a"
        ];

        /// <summary>Límite REAL y publicado de archivos por lote (versión gratuita).</summary>
        public const int MaxFilesToScan = AppLimits.ConverterMaxFilesToScan;

        /// <summary>Nombre de la subcarpeta de salida (junto a cada archivo de entrada).</summary>
        public const string OutputFolderName = AppLimits.ConverterOutputFolderName;

        private const double MinGainDb = -20.0;
        private const double MaxGainDb = 20.0;
        private const double MaxCorrectionDb = 3.0;

        public static bool IsSupportedFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext != null && SupportedExtensions.Contains(ext);
        }

        /// <summary>Archivos soportados de una carpeta (recursivo), topados a 50.</summary>
        public static List<string> GetSupportedFiles(string folder, out int totalFound)
        {
            var all = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedFile)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            totalFound = all.Count;
            return all.Take(MaxFilesToScan).ToList();
        }

        /// <summary>Convierte un lote con progreso por archivo y cancelación.</summary>
        public async Task ConvertAsync(
            IReadOnlyList<string> inputPaths,
            ConverterOptions options,
            IProgress<ConverterProgress>? progress,
            CancellationToken ct)
        {
            var files = inputPaths.Where(File.Exists).Take(MaxFilesToScan).ToList();
            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var result = await Task.Run(() => ConvertOne(files[i], options, ct), ct).ConfigureAwait(false);
                progress?.Report(new ConverterProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = files.Count,
                    CurrentFile = Path.GetFileName(files[i]),
                    Result = result
                });
            }
        }

        private static ConverterResult ConvertOne(string inputPath, ConverterOptions options, CancellationToken ct)
        {
            var temps = new List<string>();
            try
            {
                ct.ThrowIfCancellationRequested();

                // --- Paso 1: reducción de ruido afftdn (solo si hay ffmpeg) ---
                string workPath = inputPath;
                bool usedAfftdn = false;
                if (options.RemoveNoise)
                {
                    var denoised = VoiceDenoiser.TryDenoiseAsync(inputPath, ct).GetAwaiter().GetResult();
                    if (denoised.UsedAfftdn)
                    {
                        workPath = denoised.Path;
                        temps.Add(workPath);
                        usedAfftdn = true;
                    }
                }
                bool useGate = options.RemoveNoise && !usedAfftdn;

                // --- Paso 2: WAV temporal estándar (estéreo 44.1 kHz / 16-bit) ---
                string? standard = DecodeToStandardAsync(workPath, ct).GetAwaiter().GetResult();
                if (standard == null)
                {
                    // Sin ffmpeg no hay rescate posible para lo que Windows no lee
                    // (p. ej. Opus-en-OGG): se sugiere activar el motor en Descargas.
                    bool hasFfmpeg = false;
                    try { hasFfmpeg = Downloader.ToolManager.FfmpegExe != null; } catch { }
                    string hint = hasFfmpeg
                        ? ""
                        : " Abre Descargas una vez para activar el motor ffmpeg.";
                    return Fail(inputPath, "No se pudo leer (formato no soportado en este equipo)." + hint);
                }
                temps.Add(standard);

                // --- Paso 3: medición de entrada ---
                if (!TryMeasure(standard, ct, out double peakDb, out double inputLufs))
                    return Fail(inputPath, "No se pudo analizar el audio.");

                // --- Paso 4: ganancia por sonoridad a −16 LUFS (2 pasadas) ---
                double gainDb;
                if (options.EnhanceVoice)
                {
                    double provisional = double.IsNegativeInfinity(inputLufs) || double.IsNaN(inputLufs)
                        ? -3.0 - peakDb          // muy corto/silencioso: recae en pico
                        : VoiceChain.TargetLufs - inputLufs;
                    provisional = Math.Clamp(provisional, MinGainDb, MaxGainDb);

                    double chainLufs = MeasureChainLufs(standard, provisional, useGate, ct);
                    double correction = double.IsNegativeInfinity(chainLufs) || double.IsNaN(chainLufs)
                        ? 0.0
                        : Math.Clamp(VoiceChain.TargetLufs - chainLufs, -MaxCorrectionDb, MaxCorrectionDb);
                    gainDb = Math.Clamp(provisional + correction, MinGainDb, MaxGainDb);
                }
                else
                {
                    gainDb = 0.0;
                }

                // --- Paso 5: render final a WAV temporal ---
                string finalTemp = Path.Combine(Path.GetTempPath(), $"onedj_final_{Guid.NewGuid():N}.wav");
                temps.Add(finalTemp);
                var render = Render(standard, finalTemp, gainDb, options.EnhanceVoice, useGate, ct);
                if (!render.Ok)
                    return Fail(inputPath, render.Error);

                // --- Paso 6: salida WAV o MP3 320k ---
                string outPath = BuildOutputPath(inputPath, options.Format);
                if (options.Format == ConverterFormat.Wav)
                {
                    if (File.Exists(outPath)) File.Delete(outPath);
                    File.Move(finalTemp, outPath);
                    temps.Remove(finalTemp);
                }
                else if (!TryEncodeMp3(finalTemp, outPath, ct, out string mp3Error))
                {
                    return Fail(inputPath, mp3Error);
                }

                // --- Paso 7: medición real de la salida ---
                string peakText = "—", lufsText = "—";
                if (TryMeasure(outPath, ct, out double outPeak, out double outLufs))
                {
                    peakText = $"{outPeak:F1} dB";
                    lufsText = double.IsNegativeInfinity(outLufs) ? "—" : $"{outLufs:F1}";
                }

                string formatText = options.Format == ConverterFormat.Wav ? "WAV" : "MP3 320";
                string message = $"{formatText} · Voz · Pico {peakText} · LUFS {lufsText}"
                    + (options.RemoveNoise ? (usedAfftdn ? " · ruido afftdn" : " · gate") : "")
                    + (!options.EnhanceVoice ? " · sin mejora" : "");

                return new ConverterResult
                {
                    FileName = Path.GetFileName(outPath),
                    InputPath = inputPath,
                    OutputPath = outPath,
                    Success = true,
                    Message = message,
                    OutputPeakDb = outPeak,
                    OutputLufs = outLufs
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Fail(inputPath, $"Error: {ex.Message}");
            }
            finally
            {
                foreach (var t in temps) VoiceDenoiser.QuietDelete(t);
            }
        }

        private static ConverterResult Fail(string inputPath, string message) => new()
        {
            FileName = Path.GetFileName(inputPath),
            InputPath = inputPath,
            Success = false,
            Message = message
        };

        /// <summary>Decodifica a WAV temporal estéreo 44.1 kHz/16-bit (rescate
        /// ffmpeg si MediaFoundation no tiene el codec). Null si es ilegible.</summary>
        private static async Task<string?> DecodeToStandardAsync(string inputPath, CancellationToken ct)
        {
            string? readable = inputPath;
            if (!TryOpenReader(inputPath))
            {
                readable = await VoiceDenoiser.DecodeViaFfmpegAsync(inputPath, ct).ConfigureAwait(false);
                if (readable == null) return null;
            }

            string temp = Path.Combine(Path.GetTempPath(), $"onedj_std_{Guid.NewGuid():N}.wav");
            bool tempIsOurs = true;
            try
            {
                using var reader = new MediaFoundationReader(readable);
                var outFormat = new WaveFormat(44100, 16, 2);
                ISampleProvider sp = reader.ToSampleProvider();
                sp = StereoUpmix(sp, reader.WaveFormat);
                using var resampler = new MediaFoundationResampler(sp.ToWaveProvider16(), outFormat)
                {
                    ResamplerQuality = 60
                };
                WaveFileWriter.CreateWaveFile(temp, resampler);
                return temp;
            }
            catch
            {
                VoiceDenoiser.QuietDelete(temp);
                tempIsOurs = false;
                return null;
            }
            finally
            {
                // Si el rescate ffmpeg generó el legible, era temporal: borrar.
                if (!string.Equals(readable, inputPath, StringComparison.OrdinalIgnoreCase))
                    VoiceDenoiser.QuietDelete(readable);
                if (!tempIsOurs) VoiceDenoiser.QuietDelete(temp);
            }
        }

        private static bool TryOpenReader(string path)
        {
            try { using var r = new MediaFoundationReader(path); return true; }
            catch { return false; }
        }

        /// <summary>Sube a estéreo: mono duplica el canal; &gt;2 mezcla a 2.</summary>
        private static ISampleProvider StereoUpmix(ISampleProvider source, WaveFormat inFormat)
        {
            if (inFormat.Channels == 2) return source;
            return new StereoUpmixProvider(source, inFormat.Channels, inFormat.SampleRate);
        }

        private sealed class StereoUpmixProvider : ISampleProvider
        {
            private readonly ISampleProvider _source;
            private readonly int _inChannels;
            public StereoUpmixProvider(ISampleProvider source, int inChannels, int sampleRate)
            {
                _source = source;
                _inChannels = Math.Max(1, inChannels);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
            }
            public WaveFormat WaveFormat { get; }
            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                var tmp = new float[frames * _inChannels];
                int read = _source.Read(tmp, 0, tmp.Length);
                int framesRead = read / _inChannels;
                for (int f = 0; f < framesRead; f++)
                {
                    float l, r;
                    if (_inChannels == 1)
                    {
                        l = r = tmp[f];
                    }
                    else
                    {
                        l = tmp[f * _inChannels];
                        float rr = 0f;
                        for (int c = 1; c < _inChannels; c++) rr += tmp[f * _inChannels + c];
                        r = rr / (_inChannels - 1);
                    }
                    buffer[offset + f * 2] = l;
                    buffer[offset + f * 2 + 1] = r;
                }
                return framesRead * 2;
            }
        }

        private static bool TryMeasure(string path, CancellationToken ct, out double peakDb, out double lufs)
        {
            peakDb = double.NegativeInfinity;
            lufs = double.NegativeInfinity;
            try
            {
                using var reader = new MediaFoundationReader(path);
                var meter = new LoudnessMeter(reader.WaveFormat.Channels, reader.WaveFormat.SampleRate);
                var sp = reader.ToSampleProvider();
                var buf = new float[32768];
                float peak = 0f;
                int n;
                while ((n = sp.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int i = 0; i < n; i++)
                    {
                        float a = Math.Abs(buf[i]);
                        if (a > peak) peak = a;
                    }
                    meter.AddSamples(buf, 0, n);
                }
                if (peak <= 0f) return false;
                peakDb = 20.0 * Math.Log10(peak);
                lufs = meter.IntegratedLufs;
                return true;
            }
            catch { return false; }
        }

        private static ISampleProvider BuildVoiceChain(ISampleProvider source, bool enhance, bool useGate)
        {
            int ch = source.WaveFormat.Channels;
            int rate = source.WaveFormat.SampleRate;
            if (!enhance)
            {
                ISampleProvider chain = new DcBlockerSampleProvider(source, ch, rate, 10.0);
                if (useGate)
                    chain = new NoiseGateSampleProvider(chain, ch, rate, -45.0, 10.0, 200.0);
                return chain;
            }
            return VoiceChain.Build(source, ch, rate, useGate);
        }

        private static double MeasureChainLufs(string standardPath, double gainDb, bool useGate, CancellationToken ct)
        {
            using var reader = new MediaFoundationReader(standardPath);
            var gained = new VolumeSampleProvider(reader.ToSampleProvider())
            {
                Volume = (float)Math.Pow(10.0, gainDb / 20.0)
            };
            var chain = BuildVoiceChain(gained, enhance: true, useGate);
            var meter = new LoudnessMeter(reader.WaveFormat.Channels, reader.WaveFormat.SampleRate);
            var buf = new float[32768];
            int n;
            while ((n = chain.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                meter.AddSamples(buf, 0, n);
            }
            return meter.IntegratedLufs;
        }

        private static (bool Ok, string Error, float Peak, double Lufs) Render(
            string standardPath, string outputPath, double gainDb, bool enhance, bool useGate, CancellationToken ct)
        {
            try
            {
                using var reader = new MediaFoundationReader(standardPath);
                var format = reader.WaveFormat;
                var gained = new VolumeSampleProvider(reader.ToSampleProvider())
                {
                    Volume = (float)Math.Pow(10.0, gainDb / 20.0)
                };
                var voiced = BuildVoiceChain(gained, enhance, useGate);
                var dithered = new TpdfDitherSampleProvider(voiced, format.BitsPerSample);
                var meter = new LoudnessMeter(format.Channels, format.SampleRate);
                var buf = new float[32768];
                float peak = 0f;
                using var writer = new WaveFileWriter(outputPath, format);
                int n;
                while ((n = dithered.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int i = 0; i < n; i++)
                    {
                        float s = buf[i];
                        if (s > 1f) s = 1f;
                        else if (s < -1f) s = -1f;
                        buf[i] = s;
                        float a = Math.Abs(s);
                        if (a > peak) peak = a;
                    }
                    meter.AddSamples(buf, 0, n);
                    writer.WriteSamples(buf, 0, n);
                }
                return (true, "", peak, meter.IntegratedLufs);
            }
            catch (Exception ex)
            {
                return (false, $"No se pudo procesar: {ex.Message}", 0f, double.NegativeInfinity);
            }
        }

        private static string BuildOutputPath(string inputPath, ConverterFormat format)
        {
            string? dir = Path.GetDirectoryName(inputPath) ?? "";
            string outDir = Path.Combine(dir, OutputFolderName);
            Directory.CreateDirectory(outDir);

            string ext = format == ConverterFormat.Wav ? ".wav" : ".mp3";
            string rawBase = Path.GetFileNameWithoutExtension(inputPath);
            string clean = Normalization.SpanishNameCorrector.CorrectTitle(rawBase.Trim());
            foreach (char c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c, ' ');
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s{2,}", " ").Trim();
            if (string.IsNullOrWhiteSpace(clean)) clean = rawBase.Trim();

            string candidate = Path.Combine(outDir, clean + ext);
            int counter = 1;
            while (File.Exists(candidate))
            {
                counter++;
                candidate = Path.Combine(outDir, $"{clean} ({counter}){ext}");
            }
            return candidate;
        }

        /// <summary>Codifica a MP3 320 kbps con el codec de Windows; si no está
        /// disponible, rescata con ffmpeg (libmp3lame).</summary>
        private static bool TryEncodeMp3(string wavPath, string mp3Path, CancellationToken ct, out string error)
        {
            error = "";
            try
            {
                using var reader = new WaveFileReader(wavPath);
                MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, 320_000);
                if (File.Exists(mp3Path)) return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EncodeToMp3 falló: {ex.Message}");
            }

            // Rescate con ffmpeg (libmp3lame 320k).
            string? ffmpeg = null;
            try { ffmpeg = Downloader.ToolManager.FfmpegExe; } catch { ffmpeg = null; }
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
            {
                error = "MP3 no disponible en este equipo (codec ausente y sin ffmpeg). Prueba con WAV.";
                return false;
            }
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpeg,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(wavPath);
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("libmp3lame");
                psi.ArgumentList.Add("-b:a");
                psi.ArgumentList.Add("320k");
                psi.ArgumentList.Add("-ar");
                psi.ArgumentList.Add("44100");
                psi.ArgumentList.Add("-ac");
                psi.ArgumentList.Add("2");
                psi.ArgumentList.Add(mp3Path);

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null) { error = "No se pudo iniciar ffmpeg para MP3."; return false; }
                using var reg = ct.Register(() => { try { process.Kill(true); } catch { } });
                process.WaitForExit();
                if (process.ExitCode == 0 && File.Exists(mp3Path)) return true;
                error = "ffmpeg no pudo codificar el MP3.";
                return false;
            }
            catch (Exception ex)
            {
                error = $"MP3 falló: {ex.Message}";
                return false;
            }
        }
    }
}

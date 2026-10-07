using Remove_Top.Features.Downloader;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.FormatConverter
{
    /// <summary>
    /// Reducción de ruido con el filtro <c>afftdn</c> de ffmpeg (FFT, el
    /// estándar para voz/podcast): <c>highpass 80 Hz → lowpass 15 kHz →
    /// afftdn nf=−25 con tracking</c>. Es el denoiser potente/eficiente
    /// (nativo C) sin entrenar ni descargar modelos.
    ///
    /// Solo actúa si el motor ffmpeg on-demand YA está provisionado
    /// (<see cref="ToolManager.FfmpegExe"/>); nunca fuerza la descarga:
    /// si no hay ffmpeg, devuelve la entrada intacta y la cadena managed
    /// (<see cref="NoiseGateSampleProvider"/>) hace el trabajo.
    /// </summary>
    public static class VoiceDenoiser
    {
        /// <summary>
        /// Aplica afftdn a <paramref name="inputPath"/> y devuelve la ruta del
        /// WAV limpio (44.1 kHz estéreo PCM 16-bit). <c>UsedAfftdn</c> indica si
        /// se aplicó; si es false, <c>Path</c> es la entrada original.
        /// El archivo temporal debe borrarlo el llamador.
        /// </summary>
        public static async Task<(string Path, bool UsedAfftdn)> TryDenoiseAsync(
            string inputPath, CancellationToken ct)
        {
            string? ffmpeg = null;
            try { ffmpeg = ToolManager.FfmpegExe; } catch { ffmpeg = null; }
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
                return (inputPath, false);

            string temp = Path.Combine(Path.GetTempPath(), $"onedj_denoise_{Guid.NewGuid():N}.wav");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                // ArgumentList evita inyección (nunca concatenar la ruta).
                psi.ArgumentList.Add("-hide_banner");
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add("-af");
                psi.ArgumentList.Add("highpass=f=80,lowpass=f=15000,afftdn=nf=-25:nr=12:tn=1");
                psi.ArgumentList.Add("-ar");
                psi.ArgumentList.Add("44100");
                psi.ArgumentList.Add("-ac");
                psi.ArgumentList.Add("2");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("pcm_s16le");
                psi.ArgumentList.Add(temp);

                using var process = Process.Start(psi);
                if (process == null)
                    return (inputPath, false);

                using var registration = ct.Register(() =>
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                });

                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                var info = new FileInfo(temp);
                if (process.ExitCode == 0 && info.Exists && info.Length > 1024)
                    return (temp, true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App_Log($"VoiceDenoiser afftdn: {ex.Message}");
            }

            QuietDelete(temp);
            return (inputPath, false);
        }

        /// <summary>Decodifica con ffmpeg a WAV 16-bit/44.1 kHz estéreo (rescate
        /// cuando MediaFoundation no tiene el codec, p. ej. OGG en Win10 N).</summary>
        public static async Task<string?> DecodeViaFfmpegAsync(string inputPath, CancellationToken ct)
        {
            string? ffmpeg = null;
            try { ffmpeg = ToolManager.FfmpegExe; } catch { ffmpeg = null; }
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
                return null;

            string temp = Path.Combine(Path.GetTempPath(), $"onedj_decode_{Guid.NewGuid():N}.wav");
            try
            {
                var psi = new ProcessStartInfo
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
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add("-ar");
                psi.ArgumentList.Add("44100");
                psi.ArgumentList.Add("-ac");
                psi.ArgumentList.Add("2");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("pcm_s16le");
                psi.ArgumentList.Add(temp);

                using var process = Process.Start(psi);
                if (process == null)
                    return null;

                using var registration = ct.Register(() =>
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                });

                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                var info = new FileInfo(temp);
                if (process.ExitCode == 0 && info.Exists && info.Length > 1024)
                    return temp;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App_Log($"VoiceDenoiser decode: {ex.Message}");
            }

            QuietDelete(temp);
            return null;
        }

        public static void QuietDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void App_Log(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Helpers.AppLimits.AppDataFolderName);
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}

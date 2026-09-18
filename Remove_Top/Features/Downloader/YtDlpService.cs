using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.Downloader
{
    /// <summary>
    /// Ejecuta yt-dlp (como módulo de Python: <c>python.exe -m yt_dlp</c>) para
    /// descargar el mejor audio de un enlace de YouTube y convertirlo a WAV.
    /// Expone progreso mediante <see cref="IProgress{T}"/> y soporta cancelación
    /// (mata el árbol de procesos). El progreso se lee de la salida estándar con
    /// <c>--newline</c>.
    ///
    /// Estrategia anti-bot: YouTube exige un "proof-of-origin" (PO token) que
    /// genera el plugin bgutil con deno (ver <see cref="ToolManager"/>): con PO
    /// tokens válidos el cliente <c>web</c> vuelve a ser viable y da los mejores
    /// formatos. La descarga se intenta en cascada por cadenas de clientes (si
    /// una cae en bloqueo, se prueba la siguiente tras una pausa), con
    /// reintentos de red, IPv4 forzado y pausas para no disparar el check.
    /// </summary>
    public sealed class YtDlpService
    {
        /// <summary>Prefijo con el que yt-dlp imprime la ruta final (evita ambigüedad con espacios).</summary>
        private const string PathSentinel = "onedj:";

        /// <summary>Cadena principal de clientes (con PO tokens el web rinde mejor).</summary>
        private const string PrimaryClientChain = "web,web_embedded,tv,web_safari,mweb";

        /// <summary>Cadenas de respaldo si la principal cae en bloqueo anti-bot.</summary>
        private static readonly string[] FallbackClientChains = ["tv,web_safari,mweb", "android"];

        private static readonly Regex PercentRegex =
            new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);

        private static readonly Regex DestinationRegex =
            new(@"\[(?:download|ExtractAudio)\] Destination:\s+(.+)$", RegexOptions.Compiled);

        private static readonly string[] OutputExtensions = [".wav", ".m4a", ".webm", ".opus", ".mp3"];

        /// <summary>
        /// Descarga <paramref name="url"/> a <paramref name="outputDir"/>. Si
        /// <paramref name="convertToWav"/> es true usa ffmpeg para decodificar el
        /// mejor stream (Opus) a WAV; si no, baja el mejor M4A/AAC directo (sin
        /// ffmpeg) y deja que AudioNormalizer lo decodifique.
        /// </summary>
        public async Task<DownloadResult> DownloadAsync(
            string url,
            string outputDir,
            bool convertToWav,
            string? ffmpegLocation,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken,
            string? cookiesPath = null)
        {
            Directory.CreateDirectory(outputDir);
            cancellationToken.ThrowIfCancellationRequested();

            return await RunAsync(
                url, outputDir, convertToWav, ffmpegLocation, progress, cancellationToken, cookiesPath);
        }

        /// <summary>
        /// Ejecuta la descarga en cascada: primero con las cookies de la cuenta
        /// del usuario (si hay sesión vigente), luego la cadena principal de
        /// clientes y, si cae en bloqueo anti-bot, las cadenas de respaldo con
        /// una pausa entre intentos. Los errores definitivos (video no
        /// disponible, región, edad) no reintentan: se devuelven al primer fallo.
        /// </summary>
        private async Task<DownloadResult> RunAsync(
            string url,
            string outputDir,
            bool convertToWav,
            string? ffmpegLocation,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken,
            string? cookiesPath = null)
        {
            if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            {
                var authed = await RunOnceAsync(
                    url, outputDir, convertToWav, ffmpegLocation, PrimaryClientChain,
                    progress, cancellationToken, cookiesPath);
                authed.UsedCookies = true;
                if (authed.Success || IsDefinitiveFailure(authed))
                    return authed;
                if (IsSessionDead(authed))
                    authed.CookiesRejected = true;
                var anonymous = await RunAnonymousAsync(
                    url, outputDir, convertToWav, ffmpegLocation, progress, cancellationToken);
                anonymous.UsedCookies = true;
                if (authed.CookiesRejected)
                    anonymous.CookiesRejected = true;
                return anonymous;
            }

            return await RunAnonymousAsync(
                url, outputDir, convertToWav, ffmpegLocation, progress, cancellationToken);
        }

        /// <summary>Cascada anónima: cadena principal + respaldos ante bot-check.</summary>
        private async Task<DownloadResult> RunAnonymousAsync(
            string url,
            string outputDir,
            bool convertToWav,
            string? ffmpegLocation,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            var result = await RunOnceAsync(
                url, outputDir, convertToWav, ffmpegLocation, PrimaryClientChain, progress, cancellationToken);
            if (result.Success || !result.IsBotCheck)
                return result;

            foreach (var chain in FallbackClientChains)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

                result = await RunOnceAsync(
                    url, outputDir, convertToWav, ffmpegLocation, chain, progress, cancellationToken);
                if (result.Success || !result.IsBotCheck)
                    return result;
            }

            return result;
        }

        /// <summary>Un intento de descarga con una cadena de clientes dada (y cookies opcionales).</summary>
        private async Task<DownloadResult> RunOnceAsync(
            string url,
            string outputDir,
            bool convertToWav,
            string? ffmpegLocation,
            string clientChain,
            IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken,
            string? cookiesPath = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ToolManager.PythonExe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            void Arg(string value) => startInfo.ArgumentList.Add(value);

            // Módulo y comportamiento general.
            Arg("-m"); Arg("yt_dlp");
            Arg("--no-playlist");
            Arg("--newline");
            Arg("--no-warnings");
            Arg("--no-part");
            Arg("--no-simulate");
            Arg("--cache-dir"); Arg(ToolManager.YtDlpCacheDir);

            // Resiliencia de red: IPv4 (el IPv6 recibe más 403), reintentos con
            // espera exponencial y pausas para no disparar el anti-bot.
            Arg("--force-ipv4");
            Arg("--retries"); Arg("5");
            Arg("--fragment-retries"); Arg("5");
            Arg("--retry-sleep"); Arg("exp=1:20");
            Arg("--sleep-interval"); Arg("2");

            // Sesión del usuario (opcional): si hay cookies vigentes, la petición
            // llega como su cuenta (desbloquea edad/miembros/IP marcada).
            if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            {
                Arg("--cookies"); Arg(cookiesPath);
            }

            // Destino: carpeta + nombre por título.
            Arg("--paths"); Arg(outputDir);
            Arg("-o"); Arg("%(title)s.%(ext)s");

            // Formato y conversión. Se permite caer a "best" (muxed) si no hay
            // audio-only, para no fallar en vídeos no embebibles.
            if (convertToWav && !string.IsNullOrEmpty(ffmpegLocation))
            {
                Arg("-f"); Arg("bestaudio/best");
                Arg("-x");
                Arg("--audio-format"); Arg("wav");
                Arg("--audio-quality"); Arg("0");
                Arg("--ffmpeg-location"); Arg(ffmpegLocation);
            }
            else
            {
                Arg("-f"); Arg("bestaudio[ext=m4a]/bestaudio/best");
            }

            // Cadena de clientes de YouTube (estrategia anti-bot) + pacing para no disparar el check.
            Arg("--extractor-args"); Arg($"youtube:player_client={clientChain}");
            Arg("--sleep-requests"); Arg("1");

            // Proveedor de PO tokens (bgutil + deno): hace que las peticiones
            // parezcan originadas en un navegador real. Si no está aprovisionado,
            // yt-dlp simplemente trabaja sin PO tokens (como antes).
            if (ToolManager.IsBgUtilReady)
            {
                Arg("--extractor-args");
                Arg($"youtubepot-bgutilscript:server_home={ToolManager.BgUtilServerDir}");
            }

            // Caché propia de deno (paquetes del generador de PO tokens) y caché
            // XDG contenida en tools/ (tokens PO del plugin). Todo queda bajo
            // %LOCALAPPDATA%\Remove_Top\tools, sin tocar el perfil del usuario.
            startInfo.Environment["DENO_DIR"] = ToolManager.DenoCacheDir;
            startInfo.Environment["XDG_CACHE_HOME"] = ToolManager.ToolsRoot;

            // Runtime JS + scripts EJS (challenges de YouTube).
            var denoExe = ToolManager.DenoExe;
            if (File.Exists(denoExe))
            {
                Arg("--js-runtimes"); Arg($"deno:{denoExe}");
                Arg("--remote-components"); Arg("ejs:github");
            }

            // Imprime la ruta final del archivo generado, con centinela propio.
            Arg("--print"); Arg($"after_move:{PathSentinel}%(filepath)s");

            Arg(url);

            string? outputPath = null;
            string? title = null;
            var errors = new List<string>();
            var startedAt = DateTime.UtcNow.AddSeconds(-3);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            try
            {
                process.Start();

                var stdoutTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        ParseLine(line, ref title, ref outputPath, out var pct);
                        if (pct.HasValue)
                        {
                            progress?.Report(new DownloadProgress
                            {
                                Title = title ?? "",
                                Percentage = pct.Value,
                                Message = "Descargando audio..."
                            });
                        }
                        else if (outputPath != null)
                        {
                            progress?.Report(new DownloadProgress
                            {
                                Title = title ?? "",
                                Percentage = 100,
                                Message = convertToWav ? "Audio listo (WAV)" : "Audio listo (M4A)"
                            });
                        }
                    }
                });

                var stderrTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardError.ReadLineAsync()) != null)
                    {
                        if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                        {
                            lock (errors)
                            {
                                errors.Add(line.Trim());
                                if (errors.Count > 5) errors.RemoveAt(0);
                            }
                        }
                    }
                });

                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(stdoutTask, stderrTask);

                // Salvaguarda: si yt-dlp terminó bien pero no imprimió la ruta,
                // se toma el audio más reciente creado en la carpeta destino.
                if (process.ExitCode == 0 && string.IsNullOrEmpty(outputPath))
                    outputPath = FindNewestOutput(outputDir, startedAt);

                var isBotCheck = IsBotCheck(errors);
                var success = process.ExitCode == 0 && !string.IsNullOrEmpty(outputPath);

                return new DownloadResult
                {
                    Title = title ?? Path.GetFileNameWithoutExtension(outputPath ?? ""),
                    OutputPath = outputPath,
                    Success = success,
                    IsBotCheck = isBotCheck,
                    Message = success
                        ? (convertToWav ? "Descargado y convertido a WAV" : "Descargado (M4A)")
                        : BuildError(errors, process.ExitCode, isBotCheck)
                };
            }
            catch (OperationCanceledException)
            {
                KillTree(process);
                throw;
            }
            catch (Exception ex)
            {
                KillTree(process);
                App.Log("YtDlpService.Download", ex.Message, ex.StackTrace);
                return new DownloadResult
                {
                    Title = title ?? "",
                    Success = false,
                    Message = $"ERROR: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Detecta si el error es el bloqueo anti-bot de YouTube (reintentable
        /// con otra cadena de clientes). Se excluyen los mensajes con
        /// restricción de edad: también piden login pero reintentar no ayuda.
        /// </summary>
        private static bool IsBotCheck(List<string> errors)
        {
            lock (errors)
            {
                return errors.Any(e =>
                    (e.Contains("not a bot", StringComparison.OrdinalIgnoreCase) ||
                     e.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase) ||
                     e.Contains("login required", StringComparison.OrdinalIgnoreCase) ||
                     e.Contains("429", StringComparison.Ordinal) ||
                     e.Contains("too many requests", StringComparison.OrdinalIgnoreCase)) &&
                    !e.Contains("age", StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Indica si el fallo es definitivo de contenido (ni con sesión ni en
        /// anónimo se descargará: eliminado, privado, región, embed, DRM, edad).
        /// No vale la pena seguir intentando en este lote.
        /// </summary>
        private static bool IsDefinitiveFailure(DownloadResult result)
        {
            if (result.Success || string.IsNullOrEmpty(result.Message))
                return false;
            var m = result.Message;
            return m.Contains("private video", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("video unavailable", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("deleted", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("removed by the uploader", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("copyright", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("not available in your country", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("blocked in your country", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("not available in your region", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("embedding", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("watch on youtube", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("drm", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("encrypted media", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("age-restricted", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("age restriction", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("confirm your age", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Indica si la sesión de cookies está muerta (login requerido por
        /// sesión inválida o archivo de cookies rechazado). Solo en este caso
        /// la UI muestra "caducada": un bot-check con sesión viva NO la marca.
        /// </summary>
        private static bool IsSessionDead(DownloadResult result)
        {
            if (result.Success || string.IsNullOrEmpty(result.Message))
                return false;
            var m = result.Message;
            return m.Contains("login required", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("invalid account", StringComparison.OrdinalIgnoreCase) ||
                   m.Contains("cookie", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Interpreta una línea de stdout de yt-dlp (ruta final, progreso, título).</summary>
        private static void ParseLine(string line, ref string? title, ref string? outputPath, out double? percentage)
        {
            percentage = null;

            var trimmed = line.Trim();

            // Ruta final con centinela ("onedj:<ruta>"): admite espacios sin ambigüedad.
            if (trimmed.StartsWith(PathSentinel, StringComparison.Ordinal))
            {
                var path = trimmed[PathSentinel.Length..].Trim();
                if (path.Length > 0 && !path.Equals("NA", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath = path;
                    title ??= Path.GetFileNameWithoutExtension(path);
                }
                return;
            }

            var match = PercentRegex.Match(line);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var pct))
            {
                percentage = pct;
                return;
            }

            var dest = DestinationRegex.Match(line);
            if (dest.Success)
                title = Path.GetFileNameWithoutExtension(dest.Groups[1].Value.Trim());
        }

        /// <summary>
        /// Busca el audio más reciente creado en la carpeta destino (salvaguarda
        /// cuando la ruta reportada no se resuelve: títulos con tildes/emojis
        /// pueden fallar en File.Exists por normalización Unicode).
        /// </summary>
        public static string? FindNewestOutput(string outputDir, DateTime sinceUtc)
        {
            try
            {
                if (!Directory.Exists(outputDir))
                    return null;

                var newest = Directory.EnumerateFiles(outputDir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => OutputExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => new FileInfo(f))
                    .Where(info => info.LastWriteTimeUtc >= sinceUtc)
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .FirstOrDefault();

                return newest?.FullName;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildError(List<string> errors, int exitCode, bool isBotCheck)
        {
            if (isBotCheck)
                return "ERROR: YouTube pidi\u00f3 verificaci\u00f3n anti-bot. Suele ser temporal: reintenta en unos minutos.";

            lock (errors)
            {
                if (errors.Count > 0)
                    return "ERROR: " + errors[^1];
            }
            return exitCode == 0
                ? "ERROR: no se pudo determinar el archivo de salida"
                : $"ERROR: yt-dlp terminó con código {exitCode}";
        }

        private static void KillTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }
}

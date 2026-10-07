using Remove_Top.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.Downloader
{
    /// <summary>
    /// Aprovisiona el motor de descarga sin binarios precompilados marcados por
    /// el antivirus: usa el intérprete embebido de Python (firmado por la Python
    /// Software Foundation) + el wheel de yt-dlp (código puro, licencia Unlicense)
    /// en lugar del yt-dlp.exe de PyInstaller. También baja deno (runtime JS para
    /// los challenges de YouTube) y ffmpeg (LGPL para decodificar Opus a WAV).
    ///
    /// Todo se guarda en %LOCALAPPDATA%\Remove_Top\tools y se descarga on-demand,
    /// de modo que el instalador de la app no crece.
    /// </summary>
    public sealed class ToolManager
    {
        private const string PythonVersion = "3.12.10";
        private const string YtDlpPackage = "yt-dlp";
        private const string EjsPackage = "yt-dlp-ejs";

        /// <summary>
        /// Plugin de PO tokens (proof-of-origin) para YouTube: resuelve el
        /// challenge BotGuard con el mismo JS del navegador, vía deno. Sin esto,
        /// YouTube exige PO token incluso al cliente web_embedded (2026) y la
        /// descarga cae en "Sign in to confirm you're not a bot".
        /// </summary>
        private const string BgUtilPluginPackage = "bgutil-ytdlp-pot-provider";

        /// <summary>Versión mínima de deno que acepta el proveedor bgutil (script).</summary>
        private static readonly Version BgUtilMinDenoVersion = new(2, 4, 3);

        private static readonly HttpClient Http = CreateClient();

        public static string ToolsRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppLimits.AppDataFolderName, "tools");

        public static string PythonDir => Path.Combine(ToolsRoot, "python");
        public static string PythonExe => Path.Combine(PythonDir, "python.exe");
        public static string SitePackages => Path.Combine(PythonDir, "Lib", "site-packages");
        public static string DenoDir => Path.Combine(ToolsRoot, "deno");
        public static string DenoExe => Path.Combine(DenoDir, "deno.exe");
        public static string FfmpegDir => Path.Combine(ToolsRoot, "ffmpeg");

        private static string YtDlpVersionFile => Path.Combine(ToolsRoot, "ytdlp.version");

        /// <summary>Carpeta del proveedor de PO tokens (plugin + scripts JS).</summary>
        public static string BgUtilDir => Path.Combine(ToolsRoot, "bgutil");

        /// <summary>
        /// Carpeta "server/" del proveedor bgutil (contiene src/generate_once.ts).
        /// Se pasa a yt-dlp con --extractor-args "youtubepot-bgutilscript:server_home=...".
        /// </summary>
        public static string BgUtilServerDir => Path.Combine(BgUtilDir, "server");

        private static string BgUtilVersionFile => Path.Combine(BgUtilDir, "bgutil.version");

        /// <summary>Caché de yt-dlp (tokens PO, desafíos resueltos). Propia bajo tools/.</summary>
        public static string YtDlpCacheDir => Path.Combine(ToolsRoot, "yt-dlp-cache");

        /// <summary>Caché de descargas de deno (paquetes npm del generador de PO tokens).</summary>
        public static string DenoCacheDir => Path.Combine(ToolsRoot, "deno-cache");

        /// <summary>Proveedor de PO tokens listo (plugin + script generador + dependencias JS).</summary>
        public static bool IsBgUtilReady =>
            File.Exists(Path.Combine(SitePackages, "yt_dlp_plugins", "extractor", "getpot_bgutil_script.py")) &&
            File.Exists(Path.Combine(BgUtilServerDir, "src", "generate_once.ts")) &&
            Directory.Exists(Path.Combine(BgUtilServerDir, "node_modules"));

        /// <summary>El motor principal (Python + yt-dlp) está listo para usarse.</summary>
        public static bool IsPythonReady => File.Exists(PythonExe) && Directory.Exists(SitePackages);

        /// <summary>Runtime JS (deno) presente; si falta, yt-dlp puede fallar en YouTube.</summary>
        public static bool IsDenoReady => File.Exists(DenoExe);

        /// <summary>Ruta a ffmpeg.exe si está disponible (para decodificar Opus a WAV).</summary>
        public static string? FfmpegExe
        {
            get
            {
                try
                {
                    return Directory.Exists(FfmpegDir)
                        ? Directory.GetFiles(FfmpegDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault()
                        : null;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// Motor ya verificado en esta sesión: evita re-verificar PyPI/GitHub y
        /// recalentar en cada clic ("Intentar de nuevo" y 2.º enlace van rápido).
        /// Solo cubre la verificación; el servidor PO se chequea aparte (ping).
        /// </summary>
        private static bool _sessionEnsured;

        /// <summary>Puerto loopback del servidor bgutil de PO tokens (modo HTTP).</summary>
        public const int BgUtilServerPort = 4416;

        /// <summary>URL base del servidor bgutil (proveedor `bgutil:http` del plugin).</summary>
        public static string BgUtilServerUrl => $"http://127.0.0.1:{BgUtilServerPort}";

        /// <summary>Proceso del servidor bgutil (uno por sesión, loopback).</summary>
        private static Process? _bgUtilServer;
        private static readonly object _serverLock = new();

        /// <summary>
        /// Asegura que Python + yt-dlp estén instalados y actualizados, y que deno
        /// y ffmpeg existan. Reporta el progreso global (0-100) con el paso actual.
        /// </summary>
        public async Task EnsureToolsAsync(IProgress<ToolProgress>? progress = null, CancellationToken ct = default)
        {
            Directory.CreateDirectory(ToolsRoot);

            // Vía rápida de sesión: si ya se verificó todo una vez y los
            // archivos siguen en su sitio, solo se asegura el servidor PO
            // (ping barato) sin tocar la red (PyPI/GitHub) ni recalentar.
            if (_sessionEnsured && IsPythonReady && IsDenoReady && IsBgUtilReady && FfmpegExe != null)
            {
                progress?.Report(new ToolProgress { Status = "Verificando verificador local...", Percentage = 50 });
                await EnsureBgUtilServerAsync(null, ct).ConfigureAwait(false);
                progress?.Report(new ToolProgress { Status = "Motor listo", Percentage = 100 });
                return;
            }

            // Reparto de progreso por peso (los binarios grandes aportan más).
            const double wPython = 22, wYtDlp = 10, wEjs = 2, wDeno = 28, wFfmpeg = 30, wBgUtil = 8;
            double done = 0;

            void Step(string status, double within)
                => progress?.Report(new ToolProgress
                {
                    Status = status,
                    Percentage = Math.Min(99.0, done + within)
                });

            // 1) Python embebido (firmado por PSF).
            if (!IsPythonReady)
            {
                Step("Preparando Python portátil...", 0);
                var zip = Path.Combine(ToolsRoot, $"python-embed-{PythonVersion}.zip");
                await DownloadAsync(PythonEmbedUrl(), zip, p => Step("Descargando Python portátil...", wPython * p / 100.0), ct);
                ExtractZip(zip, PythonDir);
                ConfigureEmbeddedPython();
                RemoveMotw(zip);
                TryDelete(zip);
            }
            done += wPython;

            // 2) yt-dlp (wheel de PyPI, se auto-actualiza si hay versión nueva).
            Step("Verificando yt-dlp...", 0);
            await EnsureWheelAsync(YtDlpPackage, YtDlpVersionFile, p => Step("Actualizando yt-dlp...", wYtDlp * p / 100.0), ct);
            done += wYtDlp;

            // 3) Scripts EJS (solucionadores de challenges de YouTube).
            Step("Verificando scripts EJS...", 0);
            await EnsureWheelAsync(EjsPackage, Path.Combine(ToolsRoot, "ejs.version"), p => Step("Actualizando scripts EJS...", wEjs * p / 100.0), ct);
            done += wEjs;

            // 4) deno (runtime JS).
            if (!IsDenoReady || !IsDenoVersionSupported())
            {
                Step("Preparando runtime JavaScript (deno)...", 0);
                var zip = Path.Combine(ToolsRoot, "deno.zip");
                await DownloadAsync(DenoUrl(), zip, p => Step("Descargando deno...", wDeno * p / 100.0), ct);
                try { if (Directory.Exists(DenoDir)) Directory.Delete(DenoDir, recursive: true); } catch { }
                ExtractZip(zip, DenoDir);
                RemoveMotw(DenoExe);
                TryDelete(zip);
            }
            done += wDeno;

            // 5) ffmpeg (LGPL) para decodificar Opus a WAV.
            if (FfmpegExe == null)
            {
                Step("Preparando ffmpeg...", 0);
                var zip = Path.Combine(ToolsRoot, "ffmpeg.zip");
                await DownloadAsync(FfmpegUrl(), zip, p => Step("Descargando ffmpeg...", wFfmpeg * p / 100.0), ct);
                ExtractZip(zip, FfmpegDir);
                var exe = FfmpegExe;
                if (exe != null) RemoveMotw(exe);
                TryDelete(zip);
            }

            // 6) Proveedor de PO tokens (plugin bgutil + scripts JS). Si falla
            // (p. ej. sin acceso a GitHub), se continúa sin él: yt-dlp funciona
            // igual que antes, solo con menor resistencia al anti-bot.
            Step("Verificando proveedor anti-bot...", 0);
            await EnsureBgUtilAsync(p => Step("Actualizando proveedor anti-bot...", wBgUtil * p / 100.0), ct);
            done += wBgUtil;

            // 7) Servidor local de PO tokens (modo HTTP, persistente): atiende
            // los tokens en milisegundos sin arrancar deno en frío por llamada
            // (ese era el timeout de 15 s del modo script). Si no levanta, se
            // sigue con el modo script como antes.
            Step("Iniciando verificador local...", 0);
            await EnsureBgUtilServerAsync(null, ct).ConfigureAwait(false);

            _sessionEnsured = true;
            progress?.Report(new ToolProgress { Status = "Motor listo", Percentage = 100 });
        }

        /// <summary>
        /// Instala o actualiza un paquete de PyPI comparando la versión instalada
        /// (guardada en <paramref name="versionFile"/>) con la última publicada.
        /// </summary>
        private static async Task EnsureWheelAsync(
            string package, string versionFile, Action<double>? progress, CancellationToken ct)
        {
            var installed = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "";
            var latest = await GetLatestWheelAsync(package, ct);
            if (latest == null)
                return;

            var alreadyInstalled = Directory.Exists(Path.Combine(SitePackages, package.Replace('-', '_')));
            if (alreadyInstalled && string.Equals(installed, latest.Value.version, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Invoke(100);
                return;
            }

            var whl = Path.Combine(ToolsRoot, latest.Value.fileName);
            await DownloadAsync(latest.Value.url, whl, progress, ct);
            ExtractZip(whl, SitePackages);
            RemoveMotw(whl);
            TryDelete(whl);
            File.WriteAllText(versionFile, latest.Value.version);
        }

        /// <summary>
        /// Instala o actualiza el proveedor de PO tokens: el wheel del plugin en
        /// PyPI (misma mecánica que yt-dlp) + la carpeta "server/" del mismo
        /// release en GitHub (el plugin y los scripts deben compartir versión
        /// major). Nunca lanza excepción: si algo falla, se registra y la
        /// descarga continúa sin PO tokens.
        /// </summary>
        private static async Task EnsureBgUtilAsync(Action<double>? progress, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(BgUtilDir);
                var installed = File.Exists(BgUtilVersionFile) ? File.ReadAllText(BgUtilVersionFile).Trim() : "";
                var latest = await GetLatestWheelAsync(BgUtilPluginPackage, ct);
                if (latest == null)
                {
                    progress?.Invoke(100);
                    return;
                }

                if (IsBgUtilReady && string.Equals(installed, latest.Value.version, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Invoke(100);
                    return;
                }

                // 1) Plugin (wheel puro de PyPI) en site-packages.
                var whl = Path.Combine(ToolsRoot, latest.Value.fileName);
                await DownloadAsync(latest.Value.url, whl, p => progress?.Invoke(p * 0.3), ct);
                ExtractZip(whl, SitePackages);
                RemoveMotw(whl);
                TryDelete(whl);

                // 2) Scripts generadores (carpeta server/ del mismo tag en GitHub).
                var tagZip = Path.Combine(ToolsRoot, $"bgutil-{latest.Value.version}.zip");
                await DownloadAsync(
                    $"https://codeload.github.com/Brainicism/bgutil-ytdlp-pot-provider/zip/refs/tags/{latest.Value.version}",
                    tagZip, p => progress?.Invoke(30 + p * 0.6), ct);
                try { if (Directory.Exists(BgUtilServerDir)) Directory.Delete(BgUtilServerDir, recursive: true); } catch { }
                ExtractZipSubfolder(tagZip, "server", BgUtilServerDir);
                RemoveMotw(tagZip);
                TryDelete(tagZip);

                // 3) Dependencias JS del generador (deno install crea node_modules).
                // Sin esto, el proveedor queda mudo ("Did you forget to run deno install?").
                // En Task.Run porque puede tardar minutos (descarga paquetes npm).
                await Task.Run(() => RunDenoInstall(p => progress?.Invoke(90 + p * 0.05), ct), ct);

                // 3.5) Precompilación de los scripts (deno cache --no-check):
                // deja el transpile y los npm en la caché UNA vez aquí, para que
                // ni el servidor ni el modo script paguen ese costo por llamada.
                // Idempotente por sello (versión deno + versión bgutil).
                await Task.Run(() => EnsureDenoCache(ct), ct);

                // 4) Precalentamiento del script generador: el primer `deno run`
                // compila el TS y carga el módulo nativo canvas, superando los
                // 15 s que yt-dlp permite y haciendo fallar la primera descarga.
                // Con el cache caliente, las llamadas del plugin entran siempre.
                await Task.Run(() => WarmUpBgUtilScript(p => progress?.Invoke(95 + p * 0.05), ct), ct);

                if (IsBgUtilReady)
                    File.WriteAllText(BgUtilVersionFile, latest.Value.version);
                else
                    App.Log("ToolManager.BgUtil", "Servidor bgutil incompleto tras extraer (falta src/generate_once.ts).");
            }
            catch (Exception ex)
            {
                App.Log("ToolManager.BgUtil", ex.Message, ex.StackTrace);
            }
            progress?.Invoke(100);
        }

        /// <summary>
        /// Ejecuta "deno install" en la carpeta server/ del proveedor bgutil para
        /// descargar sus dependencias JS a node_modules. Idempotente: si ya
        /// existe node_modules se omite. Nunca lanza excepción.
        /// </summary>
        private static void RunDenoInstall(Action<double>? progress, CancellationToken ct)
        {
            try
            {
                if (!File.Exists(DenoExe) || !Directory.Exists(BgUtilServerDir))
                    return;
                if (Directory.Exists(Path.Combine(BgUtilServerDir, "node_modules")))
                {
                    progress?.Invoke(100);
                    return;
                }

                Directory.CreateDirectory(DenoCacheDir);
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = DenoExe,
                        Arguments = "install --allow-scripts=npm:canvas --frozen",
                        WorkingDirectory = BgUtilServerDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.Environment["DENO_DIR"] = DenoCacheDir;
                process.StartInfo.Environment["DENO_NO_UPDATE_CHECK"] = "1";
                process.StartInfo.Environment["DENO_NO_PROMPT"] = "1";
                process.Start();
                // Drenar la salida redirigida: si el buffer se llena, deno se
                // bloquearía y el WaitForExit no terminaría nunca.
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Bomba de progreso aproximada mientras deno resuelve paquetes.
                var done = process.WaitForExit(600000);
                if (!done)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    App.Log("ToolManager.BgUtil", "deno install superó los 10 minutos.");
                }
            }
            catch (Exception ex)
            {
                App.Log("ToolManager.BgUtil", ex.Message, ex.StackTrace);
            }
            progress?.Invoke(100);
        }

        /// <summary>
        /// Precalienta el script generador de PO tokens ejecutándolo una vez con
        /// `--version` (los mismos argumentos y entorno que usa el plugin de
        /// yt-dlp). Así el cache de deno queda caliente y la comprobación del
        /// plugin (timeout fijo de 15 s) no falla en la primera descarga real.
        /// Idempotente y best-effort: nunca lanza excepción.
        /// </summary>
        private static void WarmUpBgUtilScript(Action<double>? progress, CancellationToken ct)
        {
            try
            {
                var script = Path.Combine(BgUtilServerDir, "src", "generate_once.ts");
                if (!File.Exists(DenoExe) || !File.Exists(script))
                    return;

                var nodeModules = Path.Combine(BgUtilServerDir, "node_modules");
                if (!Directory.Exists(nodeModules))
                    return; // sin dependencias no hay nada que precalentar

                // Debe coincidir con el directorio de cache que el plugin usa
                // (XDG_CACHE_HOME = ToolsRoot → ToolsRoot\bgutil-ytdlp-pot-provider).
                var tokenCache = Path.Combine(ToolsRoot, "bgutil-ytdlp-pot-provider");
                Directory.CreateDirectory(DenoCacheDir);
                Directory.CreateDirectory(tokenCache);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = DenoExe,
                        Arguments = $"run --allow-env --allow-net --allow-ffi=\"{nodeModules}\" " +
                                    $"--allow-write=\"{tokenCache}\" --allow-read=\"{tokenCache},{nodeModules}\" " +
                                    $"\"{script}\" --version",
                        WorkingDirectory = BgUtilServerDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.Environment["DENO_DIR"] = DenoCacheDir;
                process.StartInfo.Environment["DENO_NO_UPDATE_CHECK"] = "1";
                process.StartInfo.Environment["DENO_NO_PROMPT"] = "1";
                process.Start();
                // Drenar la salida redirigida para no bloquear el proceso hijo.
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Diagnóstico: cuánto tarda el arranque en frío (si supera los
                // 15 s del plugin, el modo script falla y conviene el servidor).
                var sw = Stopwatch.StartNew();
                var done = process.WaitForExit(120000);
                sw.Stop();
                if (!done)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    App.Log("ToolManager.BgUtil", "warm-up del script superó los 2 minutos.");
                }
                else
                {
                    App.Log("ToolManager.BgUtil", $"warm-up del script en {(int)sw.Elapsed.TotalSeconds}s (código {process.ExitCode}).");
                }
            }
            catch (Exception ex)
            {
                App.Log("ToolManager.BgUtil", ex.Message, ex.StackTrace);
            }
            progress?.Invoke(100);
        }

        /// <summary>
        /// Precompila los scripts bgutil (`main.ts` del servidor + `generate_once.ts`)
        /// en la caché de deno, una sola vez por combinación (versión deno +
        /// versión bgutil, sello en `deno.cache.stamp`). Sin esto cada `deno run`
        /// paga el type-check y la resolución npm (más de 15 s en equipos lentos).
        /// Nunca lanza excepción.
        /// </summary>
        private static void EnsureDenoCache(CancellationToken ct)
        {
            try
            {
                if (!File.Exists(DenoExe) || !Directory.Exists(BgUtilServerDir))
                    return;
                if (!Directory.Exists(Path.Combine(BgUtilServerDir, "node_modules")))
                    return;

                string stampFile = Path.Combine(BgUtilDir, "deno.cache.stamp");
                string bgutilVersion = File.Exists(BgUtilVersionFile) ? File.ReadAllText(BgUtilVersionFile).Trim() : "";
                string want = GetDenoVersion() + "|" + bgutilVersion;
                if (!string.IsNullOrEmpty(want) && want != "|" &&
                    File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == want)
                    return;

                Directory.CreateDirectory(DenoCacheDir);
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = DenoExe,
                        Arguments = "cache --no-check \"src/main.ts\" \"src/generate_once.ts\"",
                        WorkingDirectory = BgUtilServerDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.Environment["DENO_DIR"] = DenoCacheDir;
                process.StartInfo.Environment["DENO_NO_UPDATE_CHECK"] = "1";
                process.StartInfo.Environment["DENO_NO_PROMPT"] = "1";
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var sw = Stopwatch.StartNew();
                var done = process.WaitForExit(600000);
                sw.Stop();
                if (!done)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    App.Log("ToolManager.BgUtil", "deno cache superó los 10 minutos.");
                    return;
                }
                if (process.ExitCode == 0)
                {
                    try { File.WriteAllText(stampFile, want); } catch { }
                    App.Log("ToolManager.BgUtil", $"deno cache OK en {(int)sw.Elapsed.TotalSeconds}s.");
                }
                else
                {
                    App.Log("ToolManager.BgUtil", $"deno cache salió con código {process.ExitCode}.");
                }
            }
            catch (Exception ex)
            {
                App.Log("ToolManager.BgUtil", ex.Message, ex.StackTrace);
            }
        }

        /// <summary>Versión de deno (`deno --version`, primera línea) o vacío.</summary>
        private static string GetDenoVersion()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = DenoExe,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.Environment["DENO_NO_UPDATE_CHECK"] = "1";
                process.Start();
                string? first = process.StandardOutput.ReadLine();
                process.WaitForExit(15000);
                return (first ?? "").Trim();
            }
            catch { return ""; }
        }

        /// <summary>
        /// Ping TCP al servidor bgutil (loopback). Rápido: true en ms si está
        /// arriba; false tras ~1.5 s si no hay nada escuchando.
        /// </summary>
        public static async Task<bool> IsBgUtilServerUpAsync()
        {
            try
            {
                using var tcp = new TcpClient();
                var connect = tcp.ConnectAsync("127.0.0.1", BgUtilServerPort);
                var done = await Task.WhenAny(connect, Task.Delay(1500)).ConfigureAwait(false);
                if (done != connect || !tcp.Connected)
                    return false;
                try { await connect.ConfigureAwait(false); } catch { return false; }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Asegura el servidor local de PO tokens (modo HTTP persistente): si
        /// responde al ping se reutiliza; si no, se arranca `deno run` con
        /// `src/main.ts` y se espera hasta 90 s a que escuche. Devuelve la URL
        /// base para el plugin (`bgutil:http`) o null (seguir en modo script).
        /// Nunca lanza excepción.
        /// </summary>
        public static async Task<string?> EnsureBgUtilServerAsync(IProgress<ToolProgress>? progress, CancellationToken ct)
        {
            try
            {
                if (await IsBgUtilServerUpAsync().ConfigureAwait(false))
                    return BgUtilServerUrl;

                var mainTs = Path.Combine(BgUtilServerDir, "src", "main.ts");
                if (!File.Exists(DenoExe) || !File.Exists(mainTs))
                    return null;

                lock (_serverLock)
                {
                    try
                    {
                        if (_bgUtilServer != null && !_bgUtilServer.HasExited)
                            return BgUtilServerUrl; // arrancando en otro hilo: el ping dirá
                    }
                    catch { _bgUtilServer = null; }
                }

                Directory.CreateDirectory(DenoCacheDir);
                var psi = new ProcessStartInfo
                {
                    FileName = DenoExe,
                    WorkingDirectory = BgUtilServerDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                // --no-check: el transpile ya quedó en caché (EnsureDenoCache).
                psi.ArgumentList.Add("run");
                psi.ArgumentList.Add("--allow-all");
                psi.ArgumentList.Add("--no-check");
                psi.ArgumentList.Add(mainTs);
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(BgUtilServerPort.ToString());
                psi.Environment["DENO_DIR"] = DenoCacheDir;
                psi.Environment["DENO_NO_UPDATE_CHECK"] = "1";
                psi.Environment["DENO_NO_PROMPT"] = "1";

                var server = new Process { StartInfo = psi, EnableRaisingEvents = true };
                server.OutputDataReceived += (_, __) => { };
                server.ErrorDataReceived += (_, __) => { };
                server.Start();
                server.BeginOutputReadLine();
                server.BeginErrorReadLine();
                lock (_serverLock) { _bgUtilServer = server; }

                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < TimeSpan.FromSeconds(90))
                {
                    ct.ThrowIfCancellationRequested();
                    try { if (server.HasExited) break; } catch { break; }
                    if (await IsBgUtilServerUpAsync().ConfigureAwait(false))
                    {
                        sw.Stop();
                        App.Log("ToolManager.BgUtil", $"servidor PO arriba en {(int)sw.Elapsed.TotalSeconds}s.");
                        progress?.Report(new ToolProgress { Status = "Verificador local listo", Percentage = 100 });
                        return BgUtilServerUrl;
                    }
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }

                try { if (!server.HasExited) server.Kill(entireProcessTree: true); } catch { }
                lock (_serverLock) { if (_bgUtilServer == server) _bgUtilServer = null; }
                App.Log("ToolManager.BgUtil", "el servidor PO no respondió en 90 s; se sigue en modo script.");
                return null;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Log("ToolManager.BgUtil", ex.Message, ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Detiene el servidor bgutil de la sesión (al cerrar la app). Best-effort.
        /// </summary>
        public static void StopBgUtilServer()
        {
            try
            {
                lock (_serverLock)
                {
                    try
                    {
                        if (_bgUtilServer != null && !_bgUtilServer.HasExited)
                            _bgUtilServer.Kill(entireProcessTree: true);
                    }
                    catch { }
                    _bgUtilServer = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// Extrae solo la subcarpeta <paramref name="subfolder"/> (primer nivel
        /// bajo la carpeta raíz del zip) en <paramref name="destination"/>.
        /// </summary>
        private static void ExtractZipSubfolder(string zipPath, string subfolder, string destination)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var destRoot = Path.GetFullPath(destination);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // directorio
                var parts = entry.FullName.Split('/');
                if (parts.Length < 3 || !parts[1].Equals(subfolder, StringComparison.OrdinalIgnoreCase))
                    continue; // fuera de <raíz>/server/
                var relative = string.Join(Path.DirectorySeparatorChar.ToString(), parts[2..]);
                var targetPath = Path.GetFullPath(Path.Combine(destination, relative));
                if (!targetPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                    continue; // protección contra zip-slip
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }
            foreach (var file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
                RemoveMotw(file);
        }

        /// <summary>
        /// Verifica que el deno instalado cumpla la versión mínima del proveedor
        /// bgutil (los deno antiguos no sirven y el proveedor quedaría mudo).
        /// </summary>
        private static bool IsDenoVersionSupported()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = DenoExe,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var first = (process.StandardOutput.ReadLine() ?? "").Trim();
                process.WaitForExit(10000);
                // Formato: "deno 2.x.y (...)".
                var tokens = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2 && Version.TryParse(tokens[1].Split('+')[0], out var version))
                    return version >= BgUtilMinDenoVersion;
            }
            catch (Exception ex)
            {
                App.Log("ToolManager.Deno", ex.Message, ex.StackTrace);
            }
            return false;
        }

        /// <summary>Consulta PyPI y devuelve versión, nombre de archivo y URL del wheel universal.</summary>
        private static async Task<(string version, string fileName, string url)?> GetLatestWheelAsync(
            string package, CancellationToken ct)
        {
            try
            {
                var json = await Http.GetStringAsync($"https://pypi.org/pypi/{package}/json", ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var version = root.GetProperty("info").GetProperty("version").GetString() ?? "";
                foreach (var u in root.GetProperty("urls").EnumerateArray())
                {
                    var fileName = u.GetProperty("filename").GetString() ?? "";
                    if (fileName.EndsWith("-py3-none-any.whl", StringComparison.OrdinalIgnoreCase))
                        return (version, fileName, u.GetProperty("url").GetString()!);
                }
            }
            catch (Exception ex)
            {
                App.Log("ToolManager.PyPI", ex.Message, ex.StackTrace);
            }
            return null;
        }

        /// <summary>Descarga un archivo con progreso (0-100) y lo escribe en disco.</summary>
        private static async Task DownloadAsync(
            string url, string destination, Action<double>? progress, CancellationToken ct)
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long read = 0;
            int current;
            while ((current = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, current), ct);
                read += current;
                if (total > 0)
                    progress?.Invoke((double)read / total * 100.0);
            }
            progress?.Invoke(100);
        }

        private static void ExtractZip(string zipPath, string destination)
        {
            Directory.CreateDirectory(destination);
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // directorio
                var targetPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!targetPath.StartsWith(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    continue; // protección contra zip-slip
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }

        /// <summary>
        /// Reescribe el archivo "._pth" del Python embebido para habilitar
        /// Lib\site-packages e "import site" (necesarios para cargar el wheel).
        /// </summary>
        private static void ConfigureEmbeddedPython()
        {
            Directory.CreateDirectory(SitePackages);
            var pth = Directory.GetFiles(PythonDir, "python*._pth").FirstOrDefault();
            if (pth == null)
                return;

            var zipName = Path.GetFileName(
                Directory.GetFiles(PythonDir, "python3*.zip").FirstOrDefault() ?? "python312.zip");

            File.WriteAllLines(pth, new[]
            {
                zipName,
                ".",
                @"Lib\site-packages",
                "import site"
            });
        }

        /// <summary>
        /// Elimina la marca de Internet (Mark of the Web) del archivo para que
        /// SmartScreen/Defender no lo bloqueen por haber sido descargado.
        /// </summary>
        private static void RemoveMotw(string path)
        {
            try { File.Delete(path + ":Zone.Identifier"); }
            catch { /* El ADS puede no existir; no es crítico. */ }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OneDjApp/1.0");
            return client;
        }

        private static string PythonEmbedUrl()
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "win32",
                Architecture.Arm64 => "arm64",
                _ => "amd64"
            };
            return $"https://www.python.org/ftp/python/{PythonVersion}/python-{PythonVersion}-embed-{arch}.zip";
        }

        private static string DenoUrl()
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "aarch64",
                _ => "x86_64"
            };
            return $"https://github.com/denoland/deno/releases/latest/download/deno-{arch}-pc-windows-msvc.zip";
        }

        private static string FfmpegUrl()
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "winarm64",
                Architecture.X86 => "win32",
                _ => "win64"
            };
            return $"https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-{arch}-lgpl.zip";
        }
    }
}

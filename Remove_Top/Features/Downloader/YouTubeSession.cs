using Microsoft.Web.WebView2.Core;
using Remove_Top.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Remove_Top.Features.Downloader
{
    /// <summary>
    /// Sesión de YouTube mediante WebView2: perfil aislado (no toca el Edge o
    /// Chrome del usuario), detección de login por cookies y exportación a
    /// formato Netscape para <c>--cookies</c> de yt-dlp. Todo vive bajo
    /// %LOCALAPPDATA%\Remove_Top y es 100% opcional.
    /// </summary>
    public static class YouTubeSession
    {
        private const string YouTubeUrl = "https://www.youtube.com";
        private const string GoogleUrl = "https://accounts.google.com";
        private const string LoginUrl = "https://accounts.google.com/ServiceLogin?continue=https://www.youtube.com";

        /// <summary>Antigüedad máxima aceptada del cookies.txt antes de pedir re-login.</summary>
        private static readonly TimeSpan MaxCookieAge = TimeSpan.FromDays(14);

        /// <summary>URL inicial del diálogo de login.</summary>
        public static string LoginPageUrl => LoginUrl;

        /// <summary>Carpeta del perfil WebView2 dedicado (sesión persistente entre arranques).</summary>
        public static string ProfileDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppLimits.AppDataFolderName, "webview-profile");

        /// <summary>Ruta del cookies.txt en formato Netscape que consume yt-dlp.</summary>
        public static string CookiesPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppLimits.AppDataFolderName, "youtube_cookies.txt");

        /// <summary>Indica si el Runtime de WebView2 está disponible en el equipo.</summary>
        public static bool IsRuntimeAvailable()
        {
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Crea (o reutiliza) el entorno WebView2 con el perfil aislado.</summary>
        public static Task<CoreWebView2Environment> EnsureEnvironmentAsync()
            => CoreWebView2Environment.CreateWithOptionsAsync(null, ProfileDir, null).AsTask();

        /// <summary>
        /// Detecta sesión iniciada por las cookies de autenticación de YouTube
        /// (SID + HSID/SSID + LOGIN_INFO con contenido).
        /// </summary>
        public static async Task<bool> IsLoggedInAsync(CoreWebView2 web)
        {
            try
            {
                var cookies = await web.CookieManager.GetCookiesAsync(YouTubeUrl);
                string? Value(string name) => cookies
                    .FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
                return !string.IsNullOrEmpty(Value("SID")) &&
                       (!string.IsNullOrEmpty(Value("HSID")) || !string.IsNullOrEmpty(Value("SSID"))) &&
                       !string.IsNullOrEmpty(Value("LOGIN_INFO"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Exporta las cookies de YouTube/Google a formato Netscape.
        /// Devuelve cuántas líneas escribió. Nunca registra valores.
        /// </summary>
        public static async Task<int> ExportCookiesAsync(CoreWebView2 web)
        {
            var youtube = await web.CookieManager.GetCookiesAsync(YouTubeUrl);
            var google = await web.CookieManager.GetCookiesAsync(GoogleUrl);

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sb = new StringBuilder("# Netscape HTTP Cookie File\n");
            int count = 0;
            foreach (var c in youtube.Concat(google))
            {
                if (string.IsNullOrEmpty(c.Name) || c.Value == null)
                    continue;
                var expiry = (long)c.Expires;
                if (expiry > 0 && expiry < now)
                    continue; // caducada
                sb.Append(Clean(c.Domain)).Append("\tTRUE\t")
                  .Append(Clean(c.Path)).Append('\t')
                  .Append(c.IsSecure ? "TRUE" : "FALSE").Append('\t')
                  .Append(expiry <= 0 ? 0 : expiry).Append('\t')
                  .Append(Clean(c.Name)).Append('\t')
                  .Append(Clean(c.Value)).Append('\n');
                count++;
            }

            if (count == 0)
                return 0;
            File.WriteAllText(CookiesPath, sb.ToString(), Encoding.UTF8);
            return count;
        }

        /// <summary>Indica si hay un cookies.txt vigente (existe, no vacío, fresco).</summary>
        public static bool HasFreshCookies()
        {
            try
            {
                if (!File.Exists(CookiesPath))
                    return false;
                var info = new FileInfo(CookiesPath);
                return info.Length > 0 && DateTime.UtcNow - info.LastWriteTimeUtc < MaxCookieAge;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Borra perfil y cookies (desconectar). Mejor esfuerzo, sin excepciones.</summary>
        public static void Clear()
        {
            try { if (File.Exists(CookiesPath)) File.Delete(CookiesPath); } catch { }
            try { if (Directory.Exists(ProfileDir)) Directory.Delete(ProfileDir, recursive: true); } catch { }
        }

        private static string Clean(string? value)
            => (value ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
    }
}

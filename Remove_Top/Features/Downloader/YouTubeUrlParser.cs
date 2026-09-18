using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Remove_Top.Features.Downloader
{
    /// <summary>
    /// Valida y normaliza enlaces de YouTube para la página de Descarga.
    /// Acepta watch, youtu.be, shorts, embed y music.youtube.com; rechaza
    /// cualquier host que no sea de YouTube.
    /// </summary>
    public static class YouTubeUrlParser
    {
        private static readonly HashSet<string> YouTubeHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "youtube.com",
            "youtu.be",
            "music.youtube.com",
            "m.youtube.com",
            "youtube-nocookie.com"
        };

        /// <summary>ID de video de YouTube: 11 caracteres alfanuméricos, guion y guion bajo.</summary>
        private static readonly Regex VideoIdRegex = new("^[A-Za-z0-9-_]{11}$", RegexOptions.Compiled);

        /// <summary>
        /// Intenta normalizar una entrada del usuario. Añade "https://" si falta
        /// esquema y descarta hosts ajenos a YouTube. Si detecta un ID de video,
        /// devuelve la forma canónica (watch?v=ID, sin listas, radios ni tiempos);
        /// si no (playlist pura), devuelve la URL tal cual. Devuelve la URL normalizada.
        /// </summary>
        public static bool TryNormalize(string? input, out string normalized)
        {
            normalized = "";
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var text = input.Trim().Trim('"', '\'', '<', '>');

            // Maneja el caso en que se pega "texto https://youtu.be/xxx" en una línea.
            var httpIndex = text.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (httpIndex > 0)
                text = text[httpIndex..];

            if (!text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                text = "https://" + text;

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;

            if (!YouTubeHosts.Contains(host))
                return false;

            // Un enlace de YouTube debe apuntar a un vídeo o a una lista.
            var path = uri.AbsolutePath;
            var hasVideo = host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) && path.Length > 1;
            var isWatch = path.StartsWith("/watch", StringComparison.OrdinalIgnoreCase);
            var isShorts = path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase);
            var isEmbed = path.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase);
            var isLive = path.StartsWith("/live/", StringComparison.OrdinalIgnoreCase);

            if (!hasVideo && !isWatch && !isShorts && !isEmbed && !isLive)
                return false;

            // Forma canónica: solo el video (sin listas, radios, tiempos ni tracking).
            // El mismo video pegado de distintas formas produce una sola entrada.
            if (TryExtractVideoId(uri, path, out var videoId))
            {
                normalized = $"https://www.youtube.com/watch?v={videoId}";
                return true;
            }

            normalized = uri.ToString();
            return true;
        }

        /// <summary>
        /// Extrae el ID del video: parámetro "v", path de youtu.be o último
        /// segmento de /shorts|embed|live. Devuelve false si no hay ID válido.
        /// </summary>
        private static bool TryExtractVideoId(Uri uri, string path, out string videoId)
        {
            videoId = "";

            // 1) Parámetro ?v= (cubre watch, music y nocookie).
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var name = eq < 0 ? pair : pair[..eq];
                if (name.Equals("v", StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]);
                    if (VideoIdRegex.IsMatch(candidate))
                    {
                        videoId = candidate;
                        return true;
                    }
                }
            }

            // 2) Path: youtu.be/ID o /(shorts|embed|live)/ID (primer segmento útil).
            string? last = null;
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Uri.UnescapeDataString(segment);
                if (VideoIdRegex.IsMatch(candidate))
                    last = candidate;
            }
            if (last != null)
            {
                videoId = last;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Toma un bloque de texto (una URL por línea, con posibles espacios) y
        /// devuelve las entradas no vacías ya recortadas, preservando el orden.
        /// </summary>
        public static IEnumerable<string> SplitLines(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            return text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);
        }
    }
}

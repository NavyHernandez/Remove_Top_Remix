using System;

namespace Remove_Top.Features.Downloader
{
    /// <summary>
    /// Traduce los errores crudos de yt-dlp a mensajes simples y amigables,
    /// sin códigos ni tecnicismos. El detalle original se conserva en el
    /// crash.log para soporte; en la UI solo se muestra este texto.
    /// </summary>
    public static class DownloadErrorText
    {
        /// <summary>Devuelve el texto simple equivalente a un error crudo.</summary>
        public static string Friendly(string? raw)
        {
            var text = (raw ?? "").Trim();
            if (text.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                text = text["ERROR:".Length..].Trim();

            // Causas definitivas primero (reintentar no ayuda): el orden importa
            // porque "login required" también aparece en videos con restricción.
            if (Contains(text, "private video") || Contains(text, "video unavailable") ||
                Contains(text, "this video is unavailable") || Contains(text, "members-only") ||
                Contains(text, "age-restricted") || Contains(text, "age restriction") ||
                Contains(text, "inappropriate for some users") || Contains(text, "removed by the uploader") ||
                Contains(text, "deleted video") || Contains(text, "copyright"))
                return "El video no est\u00e1 disponible (privado, eliminado, con derechos o con restricci\u00f3n de edad).";

            if (Contains(text, "not available in your country") || Contains(text, "blocked in your country") ||
                Contains(text, "not available in your region") || Contains(text, "country-blocked"))
                return "Este video no est\u00e1 disponible en tu pa\u00eds o regi\u00f3n.";

            if (Contains(text, "embedding") || Contains(text, "playback on other websites") ||
                Contains(text, "watch on youtube"))
                return "Este video no permite su descarga por aqu\u00ed. Prueba con otro enlace.";

            if (Contains(text, "drm") || Contains(text, "encrypted media"))
                return "Este video est\u00e1 protegido y no se puede descargar.";

            if (Contains(text, "not a bot") || Contains(text, "sign in to confirm") ||
                Contains(text, "login required") || Contains(text, "429") ||
                Contains(text, "too many requests"))
                return "YouTube pidi\u00f3 una verificaci\u00f3n de seguridad. Espera unos minutos e intenta de nuevo.";

            if (Contains(text, "timed out") || Contains(text, "unable to download") ||
                Contains(text, "connection") || Contains(text, "network") ||
                Contains(text, "temporary failure") || Contains(text, "getaddrinfo") ||
                Contains(text, "failed to resolve") || Contains(text, "reset by peer"))
                return "Hubo un problema de conexi\u00f3n a internet. Revisa tu red e intenta de nuevo.";

            if (Contains(text, "403") || Contains(text, "forbidden"))
                return "YouTube rechaz\u00f3 la descarga en este momento. Intenta de nuevo en unos minutos.";

            if (Contains(text, "requested format is not available") || Contains(text, "format is not available"))
                return "No se encontr\u00f3 una pista de audio v\u00e1lida para este enlace.";

            if (Contains(text, "no se pudo determinar el archivo de salida"))
                return "No se pudo completar la descarga. Intenta de nuevo.";

            if (Contains(text, "output-not-found"))
                return "El audio se descarg\u00f3 pero no se pudo localizar el archivo para normalizarlo. Revisa la carpeta de destino.";

            return "No se pudo descargar este enlace. Intenta de nuevo.";
        }

        private static bool Contains(string text, string value)
            => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

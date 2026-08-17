using System;
using System.Threading.Tasks;

namespace Remove_Top.Features.Account
{
    /// <summary>Resultado de la comprobación de actualizaciones de la aplicación.</summary>
    public class UpdateCheckResult
    {
        /// <summary>Versión instalada en la máquina del usuario.</summary>
        public string InstalledVersion { get; set; } = "";

        /// <summary>Última versión disponible (según la fuente consultada).</summary>
        public string LatestVersion { get; set; } = "";

        /// <summary>True si <see cref="LatestVersion"/> es más nueva que la instalada.</summary>
        public bool IsUpdateAvailable { get; set; }

        /// <summary>URL de descarga de la actualización (vacía si no hay update).</summary>
        public string DownloadUrl { get; set; } = "";

        /// <summary>Momento (UTC) en el que se realizó la comprobación.</summary>
        public DateTime CheckedAtUtc { get; set; }
    }

    /// <summary>
    /// Servicio que comprueba si hay una versión más reciente de la aplicación.
    ///
    /// ESTADO ACTUAL: SIMULADO. <see cref="IsSimulated"/> es <c>true</c> y
    /// <see cref="CheckForUpdatesAsync"/> devuelve un resultado fijo sin tocar
    /// la red. La implementación real (consultar el repositorio/servidor) está
    /// documentada en comentario al final de la clase y se activará en una
    /// próxima versión.
    ///
    /// La UI lo consume a través de <see cref="Instance"/>; si el checker pasa a
    /// real no cambia nada en la página (misma interfaz).
    /// </summary>
    public class UpdateChecker
    {
        /// <summary>Instancia única del servicio.</summary>
        public static UpdateChecker Instance { get; } = new();

        /// <summary>Versión instalada. Fuente única (TODO: leer del assembly en el futuro).</summary>
        public const string InstalledVersion = "1.0.0";

        /// <summary>Indica si el checker está en modo simulación (no consulta la red).</summary>
        public bool IsSimulated => true;

        /// <summary>
        /// Comprueba si existe una actualización. Actualmente devuelve un
        /// resultado SIMULADO (sin actualización disponible).
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            // --- MODO SIMULADO ---
            // No consulta la red: simula una latencia breve y devuelve que la
            // app está al día. Cuando se implemente el modo real, esta rama
            // desaparece y se ejecuta la implementación comentada abajo.
            await Task.Delay(300); // Latencia simulada
            return new UpdateCheckResult
            {
                InstalledVersion = InstalledVersion,
                LatestVersion = InstalledVersion,
                IsUpdateAvailable = false,
                DownloadUrl = "",
                CheckedAtUtc = DateTime.UtcNow
            };
        }

        // ====================================================================
        // IMPLEMENTACIÓN REAL (PENDIENTE)
        // --------------------------------------------------------------------
        // El repositorio GitHub (NavyHernandez/Remove_Top_Remix) es PRIVADO, por
        // lo que la API pública no lo expone. Cuando decidamos la fuente, se
        // reemplaza el método de arriba por una de estas opciones:
        //
        //   Opción A — version.json público en top-remix.com:
        //     GET https://www.top-remix.com/version.json
        //     { "version": "1.1.0", "downloadUrl": "https://www.top-remix.com/download" }
        //     Usar HttpClient con timeout corto y comparar con InstalledVersion.
        //
        //   Opción B — Releases de GitHub (requiere repo público o token):
        //     GET https://api.github.com/repos/NavyHernandez/Remove_Top_Remix/releases/latest
        //     Leer "tag_name" y "html_url".
        //
        // En ambos casos: comparación semántica de versiones (p. ej. por
        // Version.Parse) y guardar el resultado para iluminar el badge de la UI.
        // ====================================================================
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

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

        /// <summary>Información interna de Velopack para descargar/aplicar la actualización.</summary>
        internal UpdateInfo? VelopackUpdate { get; set; }
    }

    /// <summary>
    /// Servicio que comprueba si hay una versión más reciente de la aplicación
    /// usando Velopack con GitHub Releases como fuente.
    ///
    /// Flujo:
    ///   1. CheckForUpdatesAsync() consulta GitHub Releases vía Velopack.
    ///   2. Si hay update, DownloadUpdateAsync() lo descarga con progreso.
    ///   3. ApplyUpdate() reinicia la app y aplica la actualización.
    /// </summary>
    public class UpdateChecker
    {
        /// <summary>Instancia única del servicio.</summary>
        public static UpdateChecker Instance { get; } = new();

        /// <summary>Versión instalada. Se lee del assembly en tiempo de ejecución.</summary>
        public static string InstalledVersion { get; } =
            typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3) ?? "0.1.2";

        /// <summary>Indica si el checker está en modo simulación (no consulta la red).</summary>
        public bool IsSimulated => false;

        // URL de los releases de GitHub (repo público).
        private const string GitHubRepoUrl = "https://github.com/NavyHernandez/Remove_Top_Remix";

        private UpdateManager? _updateManager;
        private UpdateInfo? _pendingUpdate;

        /// <summary>
        /// Comprueba si existe una actualización consultando GitHub Releases
        /// a través de Velopack. Devuelve el resultado de la comprobación.
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                // Usar GithubSource explícito para garantizar la detección correcta.
                var source = new GithubSource(GitHubRepoUrl, "", false, null);
                _updateManager = new UpdateManager(source);

                App.Log("UpdateChecker", $"Checking updates from {GitHubRepoUrl} (installed: {InstalledVersion})");

                var updateInfo = await _updateManager.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    App.Log("UpdateChecker", "No update available (null returned)");
                    return new UpdateCheckResult
                    {
                        InstalledVersion = InstalledVersion,
                        LatestVersion = InstalledVersion,
                        IsUpdateAvailable = false,
                        DownloadUrl = "",
                        CheckedAtUtc = DateTime.UtcNow
                    };
                }

                _pendingUpdate = updateInfo;
                var latestVersion = updateInfo.TargetFullRelease.Version.ToString();

                App.Log("UpdateChecker", $"Update available: {latestVersion}");

                return new UpdateCheckResult
                {
                    InstalledVersion = InstalledVersion,
                    LatestVersion = latestVersion,
                    IsUpdateAvailable = true,
                    DownloadUrl = updateInfo.TargetFullRelease.FileName ?? "",
                    CheckedAtUtc = DateTime.UtcNow,
                    VelopackUpdate = updateInfo
                };
            }
            catch (Exception ex)
            {
                // Log del error para diagnóstico en vez de tragar silenciosamente.
                App.Log("UpdateChecker", $"ERROR: {ex.Message}", ex.StackTrace);
                return new UpdateCheckResult
                {
                    InstalledVersion = InstalledVersion,
                    LatestVersion = InstalledVersion,
                    IsUpdateAvailable = false,
                    DownloadUrl = "",
                    CheckedAtUtc = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Descarga la actualización disponible con progreso (0-100).
        /// Llamar solo si CheckForUpdatesAsync devolvió IsUpdateAvailable = true.
        /// </summary>
        public async Task DownloadUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (_updateManager == null || _pendingUpdate == null)
                throw new InvalidOperationException("No hay actualización pendiente. Llama a CheckForUpdatesAsync primero.");

            await _updateManager.DownloadUpdatesAsync(_pendingUpdate, progress, cancellationToken);
        }

        /// <summary>
        /// Aplica la actualización descargada, reinicia la app y la cierra.
        /// Se ejecuta después de DownloadUpdateAsync.
        /// </summary>
        public void ApplyUpdate()
        {
            if (_updateManager == null || _pendingUpdate == null)
                throw new InvalidOperationException("No hay actualización pendiente.");

            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
        }
    }
}

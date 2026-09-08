using Remove_Top.Features.Account;
using System;
using System.Threading.Tasks;

namespace Remove_Top.Features.Telemetry
{
    /// <summary>
    /// Orquestador de telemetría de instalación (Feature 16).
    ///
    /// En cada lanzamiento avanza el contador local (<see cref="InstallTracker"/>)
    /// y reporta a Cloud Firestore la instalación y el uso: ID anónimo, fecha de
    /// instalación, versión, contador de aperturas y última conexión.
    ///
    /// Es fire-and-forget y NUNCA lanza: cualquier error (red, Firestore, IO) se
    /// registra en crash.log y se reintenta en el siguiente lanzamiento.
    /// </summary>
    public static class TelemetryService
    {
        /// <summary>
        /// Registra un lanzamiento y envía el reporte a Firestore en background.
        /// No bloquea el arranque y nunca lanza excepciones.
        /// </summary>
        public static async Task ReportLaunchAsync()
        {
            try
            {
                InstallTracker.TrackLaunch();

                await FirebaseRestApi.ReportInstallLaunchAsync(
                    installId: InstallTracker.InstallId,
                    installedAt: InstallTracker.InstalledAtUtc,
                    appVersion: InstallTracker.AppVersion,
                    launchCount: InstallTracker.LaunchCount,
                    lastLaunchAt: InstallTracker.LastLaunchAtUtc);

                InstallTracker.MarkReported();
            }
            catch (Exception ex)
            {
                // No crítico: se reintentará en el próximo lanzamiento.
                App.Log("Telemetry", $"Error al reportar instalación: {ex.Message}");
            }
        }
    }
}
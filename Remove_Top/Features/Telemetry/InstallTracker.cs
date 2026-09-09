using Remove_Top.Helpers;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Remove_Top.Features.Telemetry
{
    /// <summary>
    /// Gestor local del ID único de instalación (Feature 16).
    ///
    /// Persiste en %LOCALAPPDATA%\Remove_Top\install.json un GUID anónimo y
    /// los datos de uso: fecha de instalación, versión, contador de aperturas
    /// y última conexión. NO guarda ni envía información personal (sin correo,
    /// sin nombre de máquina, sin IP, sin datos de archivos).
    ///
    /// Es completamente local y nunca lanza excepciones: cualquier error de
    /// File IO se traga y se usa un ID en memoria, para no interrumpir el
    /// arranque de la app.
    /// </summary>
    public static class InstallTracker
    {
        /// <summary>Ruta del archivo local de instalación.</summary>
        public static string InstallFilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppLimits.AppDataFolderName,
            "install.json");

        /// <summary>ID único anónimo de esta instalación.</summary>
        public static string InstallId { get; private set; } = Guid.NewGuid().ToString("N");

        /// <summary>Fecha (UTC) en la que se instaló la app por primera vez.</summary>
        public static DateTime InstalledAtUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>Versión de la app al momento de la instalación.</summary>
        public static string AppVersion { get; private set; } = "";

        /// <summary>Número de veces que se ha abierto la app (empieza en 1).</summary>
        public static int LaunchCount { get; private set; }

        /// <summary>Última fecha/hora (UTC) en que se abrió la app.</summary>
        public static DateTime LastLaunchAtUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>True si esta ejecución es la primera de la instalación.</summary>
        public static bool IsFirstLaunch { get; private set; }

        /// <summary>
        /// Lee o crea el registro local de instalación y avanza el contador de
        /// aperturas, actualizando la última conexión. Se llama una vez por
        /// lanzamiento, antes de reportar a Firestore.
        /// </summary>
        public static void TrackLaunch()
        {
            try
            {
                Load();
            }
            catch
            {
                // Archivo corrupto/inaccesible: se usa un registro en memoria.
            }

            LaunchCount++;
            LastLaunchAtUtc = DateTime.UtcNow;

            try
            {
                Save();
            }
            catch
            {
                // No se pudo persistir: se continúa con los valores en memoria.
            }
        }

        /// <summary>Marca la instalación como reportada con éxito al servidor.</summary>
        public static void MarkReported()
        {
            try
            {
                Save();
            }
            catch
            {
                // No crítico: se reintentará en el próximo lanzamiento.
            }
        }

        /// <summary>Lee el archivo local si existe; si no, genera un ID nuevo.</summary>
        private static void Load()
        {
            if (File.Exists(InstallFilePath))
            {
                var json = File.ReadAllText(InstallFilePath);
                var data = JsonSerializer.Deserialize<InstallRecord>(json);
                if (data != null && !string.IsNullOrWhiteSpace(data.InstallId))
                {
                    InstallId = data.InstallId;
                    InstalledAtUtc = data.InstalledAtUtc;
                    AppVersion = data.AppVersion ?? "";
                    LaunchCount = data.LaunchCount;
                    LastLaunchAtUtc = data.LastLaunchAtUtc;
                    IsFirstLaunch = false;
                    return;
                }
            }

            // Primera ejecución: se crea el registro.
            InstallId = Guid.NewGuid().ToString("N");
            InstalledAtUtc = DateTime.UtcNow;
            LastLaunchAtUtc = InstalledAtUtc;
            AppVersion = UpdateCheckerVersion();
            LaunchCount = 0; // TrackLaunch lo sube a 1.
            IsFirstLaunch = true;
        }

        /// <summary>Persiste el registro local en install.json.</summary>
        private static void Save()
        {
            var dir = Path.GetDirectoryName(InstallFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var record = new InstallRecord
            {
                InstallId = InstallId,
                InstalledAtUtc = InstalledAtUtc,
                AppVersion = AppVersion,
                LaunchCount = LaunchCount,
                LastLaunchAtUtc = LastLaunchAtUtc
            };

            File.WriteAllText(InstallFilePath, JsonSerializer.Serialize(record));
        }

        /// <summary>
        /// Obtiene la versión de la app sin acoplarse a Account: lee el assembly.
        /// </summary>
        private static string UpdateCheckerVersion()
        {
            try
            {
                return typeof(InstallTracker).Assembly.GetName().Version?.ToString(3) ?? "0.3.1";
            }
            catch
            {
                return "0.3.1";
            }
        }

        /// <summary>Registro persistido en install.json.</summary>
        private sealed class InstallRecord
        {
            [JsonPropertyName("installId")] public string InstallId { get; set; } = "";
            [JsonPropertyName("installedAtUtc")] public DateTime InstalledAtUtc { get; set; }
            [JsonPropertyName("appVersion")] public string? AppVersion { get; set; }
            [JsonPropertyName("launchCount")] public int LaunchCount { get; set; }
            [JsonPropertyName("lastLaunchAtUtc")] public DateTime LastLaunchAtUtc { get; set; }
        }
    }
}
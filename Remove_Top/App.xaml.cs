using Microsoft.UI.Xaml;
using Remove_Top.Features.VocalRemoval;
using Remove_Top.Helpers;
using System;
using System.IO;
using Velopack;

namespace Remove_Top
{
    /// <summary>
    /// Punto de entrada de la aplicación WinUI 3.
    ///   
    ///   - Inicializa el manejador global de excepciones no controladas.
    ///   - Crea la ventana principal (MainWindow) al lanzarse.
    ///   
    /// Las excepciones se registran en:
    ///   %LOCALAPPDATA%\Remove_Top\crash.log
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Referencia estática a la ventana principal, necesaria para FolderPicker.</summary>
        public static Window MainWindow { get; private set; } = null!;

        /// <summary>Instancia singleton de VocalSeparator. El modelo ONNX se mantiene cargado entre navegaciones.</summary>
        public static VocalSeparator VocalSeparator { get; } = new();

        private static readonly string LogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppLimits.AppDataFolderName, "crash.log");

        /// <summary>
        /// Escribe en el archivo de log. Crea el directorio si no existe.
        /// Nunca lanza excepciones (try-catch interno) para no interferir
        /// con el flujo normal de la aplicación.
        /// </summary>
        private static void WriteCrashLog(string source, string message, string? stackTrace)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {message}{Environment.NewLine}{stackTrace}{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>
        /// Registra un error en el crash.log desde cualquier módulo. Nunca lanza
        /// excepciones (try-catch interno) para no interferir con la app.
        /// </summary>
        internal static void Log(string source, string message, string? stackTrace = null)
        {
            WriteCrashLog(source, message, stackTrace);
        }

        public App()
        {
            InitializeComponent();

            // Captura excepciones no controladas del AppDomain (hilos de background, etc.)
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    WriteCrashLog("AppDomain", ex.Message, ex.StackTrace);
            };

            // Captura excepciones no controladas del dispatcher de WinUI
            Current.UnhandledException += (sender, e) =>
            {
                WriteCrashLog("App.UnhandledException", e.Message, e.Exception?.StackTrace);
                e.Handled = true; // Evita que WinUI cierre la app automáticamente
            };
        }

        /// <summary>
        /// Se ejecuta cuando la aplicación es lanzada.
        /// Crea la MainWindow y la activa para mostrarla.
        /// Cualquier error en este punto se registra y se relanza para
        /// que el depurador lo capture durante el desarrollo.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                // Velopack: aplicar actualizaciones pendientes al inicio.
                try
                {
                    VelopackApp.Build()
                        .SetAutoApplyOnStartup(true)
                        .Run();
                }
                catch
                {
                    // Velopack no disponible (modo unpackaged/debug) — continuar normalmente.
                }

                MainWindow = new MainWindow();
                MainWindow.Activate();
            }
            catch (Exception ex)
            {
                WriteCrashLog("OnLaunched", ex.Message, ex.StackTrace);
                throw;
            }
        }
    }
}

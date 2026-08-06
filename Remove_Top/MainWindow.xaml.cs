using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remove_Top.Features.BatchRename;
using Remove_Top.Features.DuplicateRemoval;
using Remove_Top.Features.Normalization;
using Remove_Top.Features.QuickRename;
using Remove_Top.Features.VocalRemoval;
using System;
using System.Collections.Generic;

namespace Remove_Top
{
    /// <summary>
    /// Ventana principal con NavigationView.
    /// Mantiene una caché de páginas (Dictionary&lt;Type, Page&gt;) para que cada
    /// página conserve su estado al navegar. Los errores de navegación y de
    /// carga se registran en %LOCALAPPDATA%\Remove_Top\crash.log.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly Dictionary<Type, Page> _pages = [];

        /// <summary>Crea la ventana. Si InitializeComponent falla, registra el error y relanza.</summary>
        public MainWindow()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                WriteLog($"MainWindow.InitializeComponent: {ex.Message}");
                throw;
            }
        }

        /// <summary>Escribe un mensaje en el log de errores. Nunca lanza excepciones.</summary>
        private static void WriteLog(string message)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Remove_Top");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>Devuelve la página correspondiente al tipo, creándola si no está en caché.</summary>
        private Page GetOrCreatePage(Type pageType)
        {
            if (!_pages.TryGetValue(pageType, out var page))
            {
                page = (Page)Activator.CreateInstance(pageType)!;
                _pages[pageType] = page;
            }
            return page;
        }

        /// <summary>Al cargar el NavigationView, selecciona la página de Normalización por defecto.</summary>
        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                NavView.SelectedItem = NavGain;
                ContentFrame.Content = GetOrCreatePage(typeof(NormalizationPage));
            }
            catch (Exception ex)
            {
                WriteLog($"NavView_Loaded: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Resuelve el Tag del item seleccionado a su tipo de página y la muestra
        /// en el ContentFrame (reutilizando la instancia en caché).
        /// </summary>
        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            try
            {
                if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
                {
                    Type pageType = tag switch
                    {
                        "gain" => typeof(NormalizationPage),
                        "rename" => typeof(BatchRenamePage),
                        "edit" => typeof(QuickRenamePage),
                        "stems" => typeof(VocalRemovalPage),
                        "duplicates" => typeof(DuplicateRemovalPage),
                        _ => throw new InvalidOperationException($"Unknown tag: {tag}")
                    };
                    ContentFrame.Content = GetOrCreatePage(pageType);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"NavView_ItemInvoked: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            }
        }
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Remove_Top.Helpers
{
    /// <summary>
    /// Helpers para interop Win32 en apps WinUI 3 unpackaged.
    /// Se usa para establecer el icono de la ventana, que WinUI 3 no expone
    /// directamente en modo unpackaged.
    /// </summary>
    internal static class Win32Helper
    {
        // --- P/Invoke Win32 ---

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImageW(
            IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageW(
            IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Establece el icono de la ventana WinUI 3 (barra de título + taskbar)
        /// cargando un archivo .ico desde disco.
        /// </summary>
        internal static void SetWindowIcon(Window window)
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

                if (!File.Exists(iconPath)) return;

                IntPtr hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
                if (hIcon == IntPtr.Zero) return;

                // Icono grande (barra de título).
                SendMessageW(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hIcon);
                // Icono pequeño (taskbar / Alt+Tab).
                SendMessageW(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hIcon);
            }
            catch
            {
                // No fallar si el icono no se puede cargar.
            }
        }
    }
}

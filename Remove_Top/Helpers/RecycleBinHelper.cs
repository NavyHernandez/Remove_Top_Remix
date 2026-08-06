using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Remove_Top.Helpers
{
    /// <summary>
    /// Envía archivos a la Papelera de Windows usando la API shell32.
    /// Es más seguro que un borrado permanente porque el usuario puede
    /// recuperar los archivos desde la Papelera si se arrepiente.
    /// </summary>
    internal static class RecycleBinHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        // Operación: borrar
        private const uint FO_DELETE = 3;

        // Flags: permitir deshacer (Papelera), silencioso, sin confirmación, sin diálogos de error
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_NOERRORUI = 0x0400;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

        /// <summary>
        /// Envía un archivo a la Papelera de Windows.
        /// Lanza <see cref="Win32Exception"/> si la operación falla.
        /// </summary>
        public static void SendToRecycleBin(string path)
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI
            };

            int result = SHFileOperationW(ref op);
            if (result != 0)
                throw new Win32Exception(result);
        }

        /// <summary>
        /// Elimina un archivo de forma permanente (NO pasa por la Papelera).
        /// Quita el atributo de solo lectura si lo tuviera para no bloquear
        /// el borrado. Lanza la excepción correspondiente si falla.
        /// </summary>
        public static void DeletePermanently(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

            File.Delete(path);
        }
    }
}

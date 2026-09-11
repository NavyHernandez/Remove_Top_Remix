using System;
using System.Collections.Generic;

namespace Remove_Top.Controls
{
    /// <summary>
    /// Argumentos del evento <see cref="DropTargetControl.FilesDropped"/>:
    /// rutas (carpetas y/o archivos) que el usuario soltó sobre la página.
    /// Las carpetas se procesan después de forma recursiva por el consumidor.
    /// </summary>
    public sealed class DropFilesEventArgs : EventArgs
    {
        /// <summary>Rutas de los elementos soltados (carpetas o archivos).</summary>
        public IReadOnlyList<string> Paths { get; }

        /// <summary>Crea los argumentos con las rutas recibidas.</summary>
        public DropFilesEventArgs(IReadOnlyList<string> paths)
        {
            Paths = paths;
        }
    }

    /// <summary>
    /// Argumentos del evento <see cref="DropTargetControl.DropFailed"/>:
    /// el arrastre se mostró pero no se pudo leer lo soltado (p. ej.
    /// GetStorageItemsAsync falló en Windows 10 o las rutas venían vacías).
    /// </summary>
    public sealed class DropFailedEventArgs : EventArgs
    {
        /// <summary>Motivo legible para mostrar en la línea de estado del origen.</summary>
        public string Reason { get; }

        /// <summary>Crea los argumentos con el motivo del fallo.</summary>
        public DropFailedEventArgs(string reason)
        {
            Reason = reason;
        }
    }
}
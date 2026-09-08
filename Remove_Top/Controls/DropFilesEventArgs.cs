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
}
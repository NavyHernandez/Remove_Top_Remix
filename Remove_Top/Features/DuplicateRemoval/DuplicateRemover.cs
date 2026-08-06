using FluentIcons.Common;
using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>Modo de borrado de los archivos duplicados.</summary>
    public enum DeletionMode
    {
        /// <summary>Envía a la Papelera de Windows (recuperable).</summary>
        RecycleBin,

        /// <summary>Elimina de forma permanente, sin pasar por la Papelera.</summary>
        Permanent
    }

    /// <summary>Resultado del borrado de un archivo.</summary>
    public class DeletionResult
    {
        public string FileName { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public Icon StatusIcon => Success ? Icon.CheckmarkCircle : Icon.DismissCircle;
    }

    /// <summary>Progreso del proceso de borrado.</summary>
    public class DeletionProgress
    {
        public int CurrentIndex { get; set; }
        public int TotalCount { get; set; }
        public string CurrentFile { get; set; } = "";
        public DeletionResult? Result { get; set; }
        public double Percentage => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0;
    }

    /// <summary>
    /// Servicio de eliminación de duplicados.
    /// Envía los archivos confirmados a la Papelera de Windows (recuperable)
    /// o los elimina definitivamente según el DeletionMode, usando
    /// RecycleBinHelper. Reporta progreso mediante IProgress y soporta
    /// cancelación. Los errores por archivo se devuelven en el Result.
    /// </summary>
    public class DuplicateRemover
    {
        /// <summary>Límite de archivos eliminables por ejecución (versión gratuita).</summary>
        public const int MaxDeletionsPerRun = 1000;

        /// <summary>
        /// Procesa los archivos marcados (IsMarkedForDeletion), hasta
        /// MaxDeletionsPerRun, enviándolos a la Papelera o eliminándolos
        /// definitivamente según el <paramref name="mode"/>.
        /// </summary>
        public async Task RemoveFilesAsync(
            IEnumerable<DuplicateItem> items,
            DeletionMode mode,
            IProgress<DeletionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            var pending = items.Where(i => i.IsMarkedForDeletion).Take(MaxDeletionsPerRun).ToArray();
            int total = pending.Length;

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = pending[i];
                DeletionResult result;

                try
                {
                    if (mode == DeletionMode.RecycleBin)
                        await Task.Run(() => RecycleBinHelper.SendToRecycleBin(item.FilePath), cancellationToken);
                    else
                        await Task.Run(() => RecycleBinHelper.DeletePermanently(item.FilePath), cancellationToken);

                    result = new DeletionResult
                    {
                        FileName = item.FileName,
                        Success = true,
                        Message = mode == DeletionMode.RecycleBin
                            ? "Enviado a la Papelera"
                            : "Eliminado definitivamente"
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result = new DeletionResult
                    {
                        FileName = item.FileName,
                        Success = false,
                        Message = $"Error: {ex.Message}"
                    };
                }

                progress.Report(new DeletionProgress
                {
                    CurrentIndex = i + 1,
                    TotalCount = total,
                    CurrentFile = item.FileName,
                    Result = result
                });
            }
        }
    }
}

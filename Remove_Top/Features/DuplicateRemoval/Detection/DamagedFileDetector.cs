using System.Collections.Generic;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Detecta archivos dañados: tamaño inferior al mínimo válido
    /// (DuplicateScanner.MinValidFileSizeBytes). No se agrupan como duplicados
    /// para evitar falsos positivos por hash de archivos vacíos. Se conserva la
    /// lógica original.
    /// </summary>
    internal static class DamagedFileDetector
    {
        public static List<DuplicateItem> Detect(IEnumerable<FileRecord> records)
        {
            return records
                .Select(r => new DuplicateItem
                {
                    FilePath = r.FilePath,
                    Size = r.Size,
                    RepeatCount = 1,
                    MatchKind = DuplicateMatchKind.Damaged,
                    SameSize = true,
                    IsMarkedForDeletion = true
                })
                .ToList();
        }
    }
}

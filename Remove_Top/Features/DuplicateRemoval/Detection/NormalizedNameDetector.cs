using System;
using System.Collections.Generic;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Detecta duplicados por nombre normalizado: archivos cuyo nombre base
    /// (sin extensión, sin mayúsculas, acentos, guiones o espacios) coincide,
    /// aunque el contenido difiera. Se conserva la lógica original de agrupar
    /// solo los que no forman parte de un grupo exacto (lo garantiza el
    /// orquestador al filtrar).
    /// </summary>
    internal sealed class NormalizedNameDetector : IDuplicateDetector
    {
        public IReadOnlyList<DuplicateGroup> Detect(IReadOnlyList<FileRecord> records)
        {
            return records
                .Where(r => r.NormalizedName.Length > 0)
                .GroupBy(r => r.NormalizedName, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => GroupBuilder.Build(g, DuplicateMatchKind.ProbableByName, keepLargest: false))
                .ToList();
        }
    }
}

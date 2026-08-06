using System;
using System.Collections.Generic;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Detecta duplicados exactos: archivos con el mismo hash SHA-256
    /// (contenido idéntico). Solo se considera el hash ya calculado (vacío en
    /// los archivos de tamaño único).
    /// </summary>
    internal sealed class ExactHashDetector : IDuplicateDetector
    {
        public IReadOnlyList<DuplicateGroup> Detect(IReadOnlyList<FileRecord> records)
        {
            return records
                .Where(r => r.Hash.Length > 0)
                .GroupBy(r => r.Hash, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => GroupBuilder.Build(g, DuplicateMatchKind.Exact, keepLargest: false))
                .ToList();
        }
    }
}

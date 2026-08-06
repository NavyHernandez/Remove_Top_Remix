using System.Collections.Generic;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Contrato común de los detectores de duplicados. Cada detector recibe
    /// un conjunto de <see cref="FileRecord"/> ya filtrado por el orquestador
    /// (los archivos reclamados por un detector de mayor prioridad se excluyen)
    /// y devuelve los grupos que detecta.
    /// </summary>
    internal interface IDuplicateDetector
    {
        IReadOnlyList<DuplicateGroup> Detect(IReadOnlyList<FileRecord> records);
    }
}

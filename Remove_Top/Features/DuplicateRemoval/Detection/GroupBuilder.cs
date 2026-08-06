using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Construye un <see cref="DuplicateGroup"/> a partir de los miembros de un
    /// grupo repetido, eligiendo la copia conservada (keeper) y el marcado por
    /// defecto según el tipo de coincidencia.
    /// </summary>
    internal static class GroupBuilder
    {
        /// <summary>
        /// Construye el grupo. <paramref name="keepLargest"/> selecciona como
        /// keeper la copia de mayor tamaño (mejor calidad); en caso contrario se
        /// conserva la ruta más superficial y, en empate, la más corta.
        /// </summary>
        public static DuplicateGroup Build(
            IEnumerable<FileRecord> members,
            DuplicateMatchKind kind,
            bool keepLargest)
        {
            var arr = members.ToArray();

            // SameSize se calcula POR ÍTEM: un miembro "comparte tamaño" si otro
            // miembro del grupo tiene exactamente su mismo tamaño. Así en un grupo
            // {5 MB, 5 MB, 8 MB} los dos de 5 MB quedan marcados (casi seguros)
            // mientras el de 8 MB (re-codificación) queda desmarcado.
            var sizeCounts = arr.GroupBy(a => a.Size)
                .ToDictionary(g => g.Key, g => g.Count());

            FileRecord keeper;
            if (keepLargest)
            {
                keeper = arr
                    .OrderByDescending(a => a.Size)
                    .ThenBy(a => a.FilePath.Length)
                    .First();
            }
            else
            {
                keeper = arr
                    .OrderBy(a => a.FilePath.Count(c => c == Path.DirectorySeparatorChar))
                    .ThenBy(a => a.FilePath.Length)
                    .First();
            }

            var duplicates = arr
                .Where(a => !string.Equals(a.FilePath, keeper.FilePath, StringComparison.OrdinalIgnoreCase))
                .Select(a =>
                {
                    bool sameSize = sizeCounts.TryGetValue(a.Size, out int count) && count > 1;
                    return new DuplicateItem
                    {
                        FilePath = a.FilePath,
                        Size = a.Size,
                        RepeatCount = arr.Length,
                        MatchKind = kind,
                        SameSize = sameSize,
                        IsMarkedForDeletion = kind switch
                        {
                            DuplicateMatchKind.Exact => true,
                            DuplicateMatchKind.ProbableByName => sameSize,
                            _ => false
                        }
                    };
                })
                .ToList();

            return new DuplicateGroup
            {
                KeeperPath = keeper.FilePath,
                Duplicates = duplicates
            };
        }
    }
}

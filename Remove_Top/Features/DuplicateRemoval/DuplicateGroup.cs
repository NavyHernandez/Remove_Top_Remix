using System.Collections.Generic;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>
    /// Grupo de archivos repetidos. Conserva 1 copia (Keeper, la ruta más
    /// superficial por defecto en los exactos y por nombre; la de mayor
    /// tamaño en los posibles por palabra) y el resto quedan como duplicados
    /// candidatos.
    /// </summary>
    public class DuplicateGroup
    {
        /// <summary>Ruta de la copia que se conserva (no se muestra en la UI).</summary>
        public string KeeperPath { get; set; } = "";

        /// <summary>Copias duplicadas (no conservadas) mostradas en la UI.</summary>
        public List<DuplicateItem> Duplicates { get; set; } = [];

        /// <summary>Total de copias del grupo (conservada + duplicados).</summary>
        public int RepeatCount => Duplicates.Count + 1;

        /// <summary>Espacio que se liberaría al eliminar los duplicados.</summary>
        public long WastedBytes => Duplicates.Sum(d => d.Size);
    }
}

using System.Collections.Generic;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>
    /// Resultado del escaneo: grupos de duplicados exactos (por hash y por
    /// nombre normalizado / misma canción), posibles (por palabra clave),
    /// archivos dañados y totales.
    /// </summary>
    public class DuplicateScanResult
    {
        /// <summary>
        /// Grupos con contenido idéntico (mismo hash, Exact) o con el mismo
        /// nombre normalizado (misma canción, SameName). Ambos quedan marcados.
        /// </summary>
        public List<DuplicateGroup> ExactGroups { get; set; } = [];

        /// <summary>
        /// Grupos con coincidencia de pares de palabras del título
        /// (ProbableByKeyword) pero contenido distinto; se verifican por duración.
        /// </summary>
        public List<DuplicateGroup> PossibleGroups { get; set; } = [];

        /// <summary>Archivos con tamaño inferior al mínimo válido (probablemente dañados).</summary>
        public List<DuplicateItem> DamagedFiles { get; set; } = [];

        /// <summary>Nº de archivos realmente analizados (máx. MaxFilesToScan).</summary>
        public int ScannedFiles { get; set; }

        /// <summary>Nº total de archivos encontrados en las carpetas.</summary>
        public int TotalFilesFound { get; set; }

        /// <summary>Nº de archivos dañados detectados.</summary>
        public int DamagedCount => DamagedFiles.Count;

        /// <summary>Nº de duplicados exactos mostrados.</summary>
        public int ExactCount => ExactGroups.Sum(g => g.Duplicates.Count);

        /// <summary>Nº de duplicados posibles mostrados.</summary>
        public int PossibleCount => PossibleGroups.Sum(g => g.Duplicates.Count);

        /// <summary>Espacio total que se liberaría al eliminar todos los duplicados.</summary>
        public long WastedBytes => ExactGroups.Sum(g => g.WastedBytes) + PossibleGroups.Sum(g => g.WastedBytes);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Detecta la MISMA CANCIÓN por nombre normalizado: archivos cuyo nombre
    /// base (sin extensión, sin mayúsculas, acentos, guiones, espacios o
    /// guiones iniciales) coincide, aunque el contenido y el tamaño difieran.
    /// Ej.: "Pipe Bueno   Te Parece Poco.mp3" y "PIPE BUENO - TE PARECE
    /// POCO.mp3", o "JESSI URIBE - SOBREVIVIRE.mp3" y "Jessi Uribe
    /// Sobreviviré.mp3". Todos los miembros quedan marcados por defecto
    /// (misma canción) y se clasifican como <see cref="DuplicateMatchKind.SameName"/>.
    ///
    /// Además de la coincidencia EXACTA, detecta nombres "casi idénticos":
    /// que difieren en UNA sola LETRA (falta ortográfica: "Incomprencion" vs
    /// "Incomprension"). La diferencia se valida PALABRA POR PALABRA: la palabra
    /// distinta debe tener al menos <see cref="MinFuzzyWordLength"/> letras y
    /// diferir en una única edición de letra. Así los títulos cortos ("uno" vs
    /// "una") y los dígitos ("mosaico 1" vs "mosaico 2") nunca se agrupan.
    /// Estos grupos también se clasifican como <see cref="DuplicateMatchKind.SameName"/>.
    /// </summary>
    internal sealed class NormalizedNameDetector : IDuplicateDetector
    {
        /// <summary>
        /// Longitud mínima (en caracteres) del nombre normalizado para entrar en
        /// la pasada difusa de "1 letra de diferencia". Evita que títulos muy
        /// cortos de canciones distintas ("uno"/"una", "beso"/"besa") se agrupen.
        /// </summary>
        public const int MinFuzzyNameLength = 6;

        /// <summary>
        /// Longitud mínima (en letras) de la PALABRA donde ocurre la diferencia
        /// para considerar dos nombres "casi idénticos". Así "Mosaico 1" vs
        /// "Mosaico 2" nunca coinciden (el token distinto es un dígito de 1 letra).
        /// </summary>
        public const int MinFuzzyWordLength = 5;

        public IReadOnlyList<DuplicateGroup> Detect(IReadOnlyList<FileRecord> records)
        {
            var exact = records
                .Where(r => r.NormalizedName.Length > 0)
                .GroupBy(r => r.NormalizedName, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => GroupBuilder.Build(g, DuplicateMatchKind.SameName, keepLargest: false));

            var groups = new List<DuplicateGroup>(exact);

            var used = CollectPaths(groups);
            var remaining = records.Where(r => !used.Contains(r.FilePath)).ToArray();
            foreach (var cluster in FindNearNameClusters(remaining))
            {
                groups.Add(GroupBuilder.Build(cluster, DuplicateMatchKind.SameName, keepLargest: false, nearName: true));
            }

            return groups;
        }

        /// <summary>
        /// Reúne todas las rutas (keeper + duplicados) de los grupos ya formados.
        /// </summary>
        private static HashSet<string> CollectPaths(IEnumerable<DuplicateGroup> groups)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                if (group.KeeperPath.Length > 0) paths.Add(group.KeeperPath);
                foreach (var duplicate in group.Duplicates)
                    paths.Add(duplicate.FilePath);
            }
            return paths;
        }

        /// <summary>
        /// Agrupa en clusters transitivos (union-find) los registros cuyos NOMBRES
        /// difieren en una sola LETRA dentro de una misma palabra. Dos nombres
        /// coinciden solo si tienen el mismo número de palabras, exactamente UNA
        /// palabra distinta en la misma posición, esa palabra tiene al menos
        /// <see cref="MinFuzzyWordLength"/> letras y difiere en una única edición
        /// de letra (p.ej. "Incomprencion"/"Incomprension"). Devuelve los clusters
        /// con 2 o más miembros.
        /// </summary>
        private static List<FileRecord[]> FindNearNameClusters(FileRecord[] records)
        {
            int n = records.Length;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            var tokens = new string[n][];
            for (int i = 0; i < n; i++)
                tokens[i] = NameNormalizer.GetAllNameWords(records[i].FilePath);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    string a = records[i].NormalizedName;
                    string b = records[j].NormalizedName;
                    if (a.Length < MinFuzzyNameLength || b.Length < MinFuzzyNameLength) continue;
                    if (!NearNameMatches(tokens[i], tokens[j])) continue;
                    int ra = Find(i), rb = Find(j);
                    if (ra != rb) parent[ra] = rb;
                }
            }

            return Enumerable.Range(0, n)
                .Where(i => parent[i] == i)
                .Select(root => records
                    .Where((_, idx) => Find(idx) == root)
                    .ToArray())
                .Where(c => c.Length > 1)
                .ToList();
        }

        /// <summary>
        /// Indica si dos secuencias de palabras representan el mismo nombre con
        /// una sola letra de diferencia: mismo número de palabras, exactamente una
        /// palabra distinta en la misma posición, con al menos
        /// <see cref="MinFuzzyWordLength"/> letras y a una única edición de letra.
        /// </summary>
        private static bool NearNameMatches(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;

            int diffIndex = -1;
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    if (diffIndex >= 0) return false;
                    diffIndex = i;
                }
            }

            if (diffIndex < 0) return false;

            string wordA = a[diffIndex];
            string wordB = b[diffIndex];
            if (wordA.Length < MinFuzzyWordLength || wordB.Length < MinFuzzyWordLength) return false;
            return EditDistanceIsOne(wordA, wordB);
        }

        /// <summary>
        /// Indica si dos cadenas difieren en exactamente UNA edición (una
        /// sustitución de letra, o una inserción/eliminación de una letra).
        /// El carácter diferente debe ser una LETRA: se descartan las
        /// diferencias de dígitos para no unir pistas de una serie
        /// ("mosaico 1" vs "mosaico 2", "vol 1" vs "vol 2").
        /// </summary>
        private static bool EditDistanceIsOne(string a, string b)
        {
            int la = a.Length, lb = b.Length;
            if (Math.Abs(la - lb) > 1) return false;

            if (la == lb)
            {
                int diffIndex = -1;
                for (int i = 0; i < la; i++)
                {
                    if (a[i] != b[i])
                    {
                        if (diffIndex >= 0) return false;
                        diffIndex = i;
                    }
                }
                return diffIndex >= 0 &&
                    char.IsLetter(a[diffIndex]) && char.IsLetter(b[diffIndex]);
            }

            string longer = la > lb ? a : b;
            string shorter = la > lb ? b : a;
            int li = 0, si = 0, skipIndex = -1;
            while (li < longer.Length && si < shorter.Length)
            {
                if (longer[li] != shorter[si])
                {
                    if (skipIndex >= 0) return false;
                    skipIndex = li;
                    li++;
                }
                else
                {
                    li++;
                    si++;
                }
            }
            if (skipIndex < 0) skipIndex = li;
            return char.IsLetter(longer[skipIndex]);
        }
    }
}

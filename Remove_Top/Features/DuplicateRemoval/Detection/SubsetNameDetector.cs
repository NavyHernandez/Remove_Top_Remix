using System;
using System.Collections.Generic;
using System.Linq;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Detecta duplicados por CONTENCIÓN DE PALABRAS: archivos donde todas las
    /// palabras del BLOQUE DE TÍTULO del nombre más corto aparecen en el bloque
    /// de título del más largo.
    /// Ej.: "147 Lolita Echeverria Cuchara De Palo Extenced Intro Simple 2K24 147 BPM.mp3"
    /// y "147 Lolita Echeverria Cuchara de Palo Extenced Intro 2k24.mp3" — el
    /// segundo es subconjunto del primero (le faltan "simple" y "bpm").
    ///
    /// La comparación se hace SOLO con el TÍTULO (último bloque tras los
    /// separadores habituales), NO con el nombre completo: así el ARTISTA no
    /// participa del test y un archivo con solo el artista (p.ej.
    /// "hipatia balseca.mp3") deja de ser un "hub" que agrupa transitivamente
    /// TODAS las canciones del mismo intérprete. La contención por nombre
    /// normalizado (que incluye el artista) sigue verificándose como salvaguarda.
    ///
    /// Requisitos para agrupar:
    ///   - Diferencia de palabras del título: 1 a <see cref="MaxWordDifference"/>
    ///   - Palabras del título más corto deben ser subconjunto exacto del más largo
    ///   - Nombre normalizado del más corto debe estar contenido en el del más largo
    ///   - Clustering transitivo (union-find): A ⊂ B y B ⊂ C → {A, B, C}, con
    ///     un tope de <see cref="MaxGroupSize"/> miembros (los clusters mayores
    ///     son cadenas de falso positivo y se descartan).
    ///
    /// Se ejecuta DESPUÉS de <see cref="NormalizedNameDetector"/> (que ya captura
    /// nombres idénticos y fuzzy "1 letra") y ANTES del hash. Los archivos ya
    /// reclamados por nombre exacto no participan.
    ///
    /// Estos grupos se clasifican como <see cref="DuplicateMatchKind.SubsetMatch"/>
    /// (visible como "Exacto" en la UI) y se marcan por defecto; el
    /// DurationVerifier los desmarca si la duración difiere mucho.
    /// </summary>
    internal sealed class SubsetNameDetector : IDuplicateDetector
    {
        /// <summary>
        /// Diferencia máxima de palabras del título entre el más corto y el más
        /// largo para considerar que uno es subconjunto del otro. Con 3 se
        /// evita agrupar canciones que solo comparten el artista.
        /// </summary>
        public const int MaxWordDifference = 3;

        /// <summary>
        /// Tope de miembros de un cluster "nombre contenido". El clustering
        /// transitivo (union-find) puede encadenar variantes legítimas (A ⊂ B ⊂ C),
        /// pero un cluster descomunal indica una cadena de falsos positivos
        /// (un nombre corto común actuando como hub); esos clusters se descartan
        /// para no marcar canciones distintas como duplicados.
        /// </summary>
        public const int MaxGroupSize = 6;

        public IReadOnlyList<DuplicateGroup> Detect(IReadOnlyList<FileRecord> records)
        {
            int n = records.Count;
            if (n < 2) return [];

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

            var allWords = new string[n][];
            var normNames = new string[n];
            for (int i = 0; i < n; i++)
            {
                allWords[i] = NameNormalizer.GetTitleWordsAll(records[i].FilePath);
                normNames[i] = records[i].NormalizedName;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (!IsSubsetPair(allWords[i], allWords[j], normNames[i], normNames[j]))
                        continue;
                    int ri = Find(i), rj = Find(j);
                    if (ri != rj) parent[ri] = rj;
                }
            }

            return Enumerable.Range(0, n)
                .Where(i => parent[i] == i)
                .Select(root => records
                    .Where((_, idx) => Find(idx) == root)
                    .ToArray())
                .Where(c => c.Length > 1)
                .Where(c => c.Length <= MaxGroupSize)
                .Select(c => GroupBuilder.Build(c, DuplicateMatchKind.SubsetMatch, keepLargest: false, nearName: true))
                .ToList();
        }

        /// <summary>
        /// Indica si un par de títulos cumple la relación de contención:
        /// uno tiene todas las palabras del otro (subconjunto) con una
        /// diferencia de 1 a <see cref="MaxWordDifference"/> palabras.
        /// Los tokens numéricos puros del título corto se ignoran para no
        /// agrupar "mosaico 1" con "mosaico 2".
        /// </summary>
        private static bool IsSubsetPair(
            string[] wordsA, string[] wordsB,
            string normA, string normB)
        {
            if (wordsA.Length == wordsB.Length) return false;

            string[] shorterWords, longerWords;
            string shorterNorm, longerNorm;
            if (wordsA.Length < wordsB.Length)
            {
                shorterWords = wordsA; longerWords = wordsB;
                shorterNorm = normA; longerNorm = normB;
            }
            else
            {
                shorterWords = wordsB; longerWords = wordsA;
                shorterNorm = normB; longerNorm = normA;
            }

            int diff = longerWords.Length - shorterWords.Length;
            if (diff < 1 || diff > MaxWordDifference) return false;

            if (!longerNorm.Contains(shorterNorm, StringComparison.Ordinal)) return false;

            return IsWordSubset(shorterWords, longerWords);
        }

        /// <summary>
        /// Verifica si todas las palabras del array más corto existen en el
        /// array más largo. Los tokens numéricos puros se ignoran para no
        /// agrupar "mosaico 1" con "mosaico 2".
        /// </summary>
        private static bool IsWordSubset(string[] shorter, string[] longer)
        {
            var longerSet = new HashSet<string>(longer, StringComparer.Ordinal);
            foreach (var word in shorter)
            {
                if (word.Length > 0 && char.IsDigit(word[0])) continue;
                if (!longerSet.Contains(word)) return false;
            }
            return true;
        }
    }
}

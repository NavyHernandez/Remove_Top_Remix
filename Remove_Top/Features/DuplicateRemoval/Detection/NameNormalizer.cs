using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Normaliza nombres de archivo para comparar duplicados de forma
    /// insensible a mayúsculas/minúsculas, acentos, guiones, espacios e
    /// incluso guiones iniciales. También extrae hasta 4 palabras
    /// significativas del BLOQUE DE TÍTULO (el último bloque separado por
    /// guiones) para la coincidencia difusa por pares de palabras, evitando
    /// que el artista ("ROSITA FLORES - AGUAS DEL RIO" vs "ROSITA FLORES -
    /// BUSCANDO OLVIDO") provoque falsos positivos.
    /// </summary>
    internal static class NameNormalizer
    {
        /// <summary>
        /// Palabras que no se tienen en cuenta como palabra clave (artículos,
        /// pronombres, conectores y marcadores habituales en títulos).
        /// </summary>
        private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
        {
            // Español
            "de", "del", "la", "las", "los", "el", "en", "y", "o", "e",
            "un", "una", "unos", "unas", "con", "por", "para", "al",
            "se", "su", "sus", "mi", "mis", "tu", "tus", "te", "ti", "me",
            "le", "les", "lo", "no", "que", "como", "si", "ya", "sin",
            "esta", "este", "estas", "estos", "esa", "ese", "esas", "esos",
            "otra", "otro", "otras", "otros", "entre", "pero", "mas",
            "bien", "cuando", "donde", "muy", "todo", "toda", "todos",
            "todas", "sobre", "hasta", "desde", "hacia", "tras", "contra",
            "a", "ante", "copia",
            // Español: auxiliares y conectores que crean pares débiles
            "es", "son", "fue", "era", "ser", "estar", "estan", "estoy",
            "hay", "ha", "han", "he", "has", "hubo", "habia", "tiene",
            "tienen", "tenia", "soy", "eres", "somos", "tan", "tanto",
            "cada", "ambos", "nuestro", "nuestra", "vuestro", "ni", "co",
            // Inglés
            "the", "a", "an", "and", "or", "of", "for", "to", "in", "on",
            "with", "at", "by", "from", "is", "are", "be", "it", "this",
            "that", "these", "those", "my", "your", "you", "me", "not",
            "feat", "ft", "featuring", "vs", "versus", "copy",
            // Inglés: auxiliares y pronombres que crean pares débiles
            "am", "was", "were", "been", "have", "has", "had", "will",
            "would", "can", "could", "should", "do", "does", "did", "his",
            "her", "him", "its", "our", "their", "them", "they", "she",
            "he", "us", "we", "who", "what", "when", "why", "how", "up",
            "out", "so", "if", "then", "also", "very", "just", "one", "two",
            // Marcadores musicales
            "remix", "remastered", "remaster", "live", "version", "edit",
            "original", "official", "karaoke", "instrumental", "part", "vol",
            "edition", "extended", "acoustic", "acustico", "bonus",
            "vivo", "mix", "megamix", "demo", "remake"
        };

        /// <summary>
        /// Normaliza el nombre base (sin extensión): FormD, elimina marcas de
        /// acento, pasa a minúsculas y descarta todo lo que no sea alfanumérico
        /// (guiones, espacios, guiones iniciales, signos).
        /// </summary>
        public static string Normalize(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            name = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                char lower = char.ToLowerInvariant(c);
                if (char.IsLetterOrDigit(lower))
                    sb.Append(lower);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Extrae hasta <paramref name="maxWords"/> palabras significativas del
        /// nombre base completo (sin extensión). Se ignoran los separadores
        /// (guiones, espacios, guiones iniciales), las <see cref="StopWords"/> y
        /// los tokens numéricos puros (años, números de pista). Devuelve un
        /// array vacío si no hay ninguna palabra significativa.
        /// </summary>
        public static string[] GetSignificantWords(string filePath, int maxWords = 4)
            => ExtractSignificantWords(Path.GetFileNameWithoutExtension(filePath), maxWords);

        /// <summary>
        /// Extrae hasta <paramref name="maxWords"/> palabras significativas del
        /// BLOQUE DE TÍTULO del nombre (sin extensión). El nombre se divide en
        /// bloques por separadores (guion, guion largo, pleca, punto medio...);
        /// el título es el último bloque que contiene al menos una palabra
        /// significativa (saltando sufijos de versión como "EN VIVO", "REMIX").
        ///
        /// Así "ROSITA FLORES - AGUAS DEL RIO" y "ROSITA FLORES - BUSCANDO
        /// OLVIDO" comparten el bloque del artista pero tienen TÍTULOS
        /// distintos y NO se consideran duplicados; mientras que "ROSITA
        /// FLORES - AGUAS DEL RIO" y "ROSITA FLORES FEAT VERONICA - AGUAS DEL
        /// RIO" comparten el título "AGUAS DEL RIO" y SÍ se consideran un
        /// posible duplicado. Sin separador, todo el nombre es un único bloque.
        /// </summary>
        public static string[] GetTitleWords(string filePath, int maxWords = 8)
        {
            var blocks = SplitBlocks(Path.GetFileNameWithoutExtension(filePath));
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                var words = ExtractSignificantWords(blocks[i], maxWords);
                if (words.Length > 0) return words;
            }
            return [];
        }

        /// <summary>
        /// Divide el nombre base completo (sin extensión) en TODAS sus palabras:
        /// minúsculas y sin acentos. A diferencia de <see cref="GetSignificantWords"/>,
        /// aquí se conservan los dígitos y las stop-words (p.ej. "Mosaico 1" →
        /// ["mosaico", "1"]). Sirve para validar la coincidencia "1 letra de
        /// diferencia" a nivel de palabra (solo palabras de longitud suficiente).
        /// </summary>
        public static string[] GetAllNameWords(string filePath)
        {
            var words = new List<string>();
            var token = new StringBuilder(32);
            string name = Path.GetFileNameWithoutExtension(filePath);

            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    token.Append(c);
                    continue;
                }

                if (token.Length > 0)
                {
                    string w = CleanKeepAll(token.ToString());
                    if (w.Length > 0) words.Add(w);
                    token.Clear();
                }
            }

            if (token.Length > 0)
            {
                string w = CleanKeepAll(token.ToString());
                if (w.Length > 0) words.Add(w);
            }

            return words.ToArray();
        }

        /// <summary>
        /// Quita acentos (FormD) y pasa a minúsculas, sin descartar stop-words ni
        /// tokens numéricos. Devuelve "" solo si queda vacío.
        /// </summary>
        private static string CleanKeepAll(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Divide un nombre en bloques usando los separadores habituales
        /// (guion, guion en/em largo, pleca, punto medio y viñeta). Los bloques
        /// quedan sin espacios iniciales/finales y se ignoran los vacíos.
        /// </summary>
        private static List<string> SplitBlocks(string name)
        {
            var blocks = new List<string>();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (IsBlockSeparator(c))
                {
                    if (sb.Length > 0) { blocks.Add(sb.ToString().Trim()); sb.Clear(); }
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0) blocks.Add(sb.ToString().Trim());
            return blocks;
        }

        private static bool IsBlockSeparator(char c) =>
            c is '-' or '\u2013' or '\u2014' or '\u2015' or '|' or '\u00B7' or '\u2022';

        /// <summary>Extrae palabras significativas de un texto (un bloque).</summary>
        private static string[] ExtractSignificantWords(string text, int maxWords)
        {
            var words = new List<string>(maxWords);
            var token = new StringBuilder(text.Length);

            for (int i = 0; i <= text.Length; i++)
            {
                bool end = i == text.Length;
                char c = end ? '\0' : text[i];

                if (!end && char.IsLetterOrDigit(c))
                {
                    token.Append(c);
                    continue;
                }

                if (token.Length > 0)
                {
                    string word = CleanWord(token.ToString());
                    token.Clear();
                    if (word.Length == 0) continue;
                    words.Add(word);
                    if (words.Count == maxWords) break;
                }
            }

            return words.ToArray();
        }

        /// <summary>
        /// Limpia un token: quita acentos (FormD), pasa a minúsculas y devuelve
        /// "" si es una stop-word, un token numérico puro o queda vacío.
        /// </summary>
        private static string CleanWord(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            bool anyLetter = false;
            foreach (char c in raw.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                char lower = char.ToLowerInvariant(c);
                if (char.IsLetter(lower)) anyLetter = true;
                sb.Append(lower);
            }

            string word = sb.ToString();
            if (word.Length == 0 || !anyLetter) return "";
            return StopWords.Contains(word) ? "" : word;
        }
    }
}

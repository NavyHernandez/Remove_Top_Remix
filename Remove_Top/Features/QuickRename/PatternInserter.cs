using System;
using System.IO;
using System.Linq;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>Posición donde se inserta el patrón dentro del nombre.</summary>
    public enum PatternPosition
    {
        /// <summary>Al inicio: "REMIX cancion".</summary>
        Start,

        /// <summary>Al centro, en un límite entre palabras: "a b REMIX c d".</summary>
        Middle,

        /// <summary>Al final: "cancion REMIX".</summary>
        End
    }

    /// <summary>
    /// Servicio que inserta un texto (patrón) en un nombre de archivo,
    /// separándolo con un espacio y sin tocar la extensión.
    ///
    /// El centro se calcula SOLO en límites entre palabras (nunca dentro de
    /// una palabra): se divide la base por espacios y se inserta en el índice
    /// <c>palabras / 2</c> (división entera). Con 0-1 palabras equivale al final.
    /// </summary>
    public static class PatternInserter
    {
        /// <summary>
        /// Inserta el patrón en un nombre de archivo completo, conservando la
        /// extensión original.
        /// </summary>
        public static string InsertFileName(string fileName, string pattern, PatternPosition position)
        {
            var ext = Path.GetExtension(fileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            return InsertIntoBase(baseName, pattern, position) + ext;
        }

        /// <summary>Inserta el patrón en la base del nombre (sin extensión).</summary>
        public static string InsertIntoBase(string baseName, string pattern, PatternPosition position)
        {
            if (string.IsNullOrWhiteSpace(baseName)) return baseName;

            return position switch
            {
                PatternPosition.Start => $"{pattern} {baseName}",
                PatternPosition.End => $"{baseName} {pattern}",
                _ => InsertMiddle(baseName, pattern),
            };
        }

        private static string InsertMiddle(string baseName, string pattern)
        {
            var words = baseName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length <= 1) return $"{baseName} {pattern}";

            var list = words.ToList();
            list.Insert(words.Length / 2, pattern);
            return string.Join(" ", list);
        }
    }
}

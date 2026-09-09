using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>Separador usado para dividir el nombre en partes.</summary>
    public enum NameSeparator
    {
        /// <summary>Espacio simple (" ").</summary>
        Space,

        /// <summary>Guion rodeado de espacios (" - ").</summary>
        Hyphen
    }

    /// <summary>
    /// Un bloque (chip) del nombre. Puede abarcar una o varias palabras
    /// contiguas del nombre original:
    ///   - <see cref="BlockIndex"/>: índice de la PRIMERA palabra del bloque en
    ///     la secuencia original (antes de fusionar). Es la clave que se usa
    ///     para derivar la permutación al arrastrar.
    ///   - <see cref="WordCount"/>: cuántas palabras contiguas abarca (1 =
    ///     palabra suelta; &gt;1 = bloque unido).
    ///   - <see cref="Text"/>: texto combinado del bloque (para preview/unión).
    /// </summary>
    public class NamePart : INotifyPropertyChanged
    {
        private bool _isJoinSource;

        public int BlockIndex { get; set; }
        public int WordCount { get; set; } = 1;
        public string Text { get; set; } = "";

        /// <summary>True si el bloque abarca más de una palabra (está unido).</summary>
        public bool IsMerged => WordCount > 1;

        /// <summary>True si el chip está seleccionado como fuente de una unión.</summary>
        public bool IsJoinSource
        {
            get => _isJoinSource;
            set
            {
                if (_isJoinSource != value)
                {
                    _isJoinSource = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Texto del badge del chip: rango si está unido, índice si no.</summary>
        public string BadgeText => IsMerged
            ? $"{BlockIndex + 1}–{BlockIndex + WordCount}"
            : (BlockIndex + 1).ToString();

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Fila de la vista previa del reorden: muestra el resultado de aplicar el
    /// reorden a un archivo concreto.
    /// </summary>
    public class ReorderPreviewItem
    {
        /// <summary>Nombre reordenado (resultado de la vista previa).</summary>
        public string Result { get; set; } = "";
    }

    /// <summary>
    /// Servicio que divide un nombre de archivo en bloques (palabras sueltas o
    /// agrupadas) usando un separador (espacio o guion) y permite reordenarlas.
    ///
    /// Ejemplo (separador guion):
    ///   "108 - Gloria Cdeño - Intro rmx - Cobra Jr dj.mp3"
    ///   → bloques ["108", "Gloria Cdeño", "Intro rmx", "Cobra Jr dj"]
    ///
    /// Ejemplo (separador espacio, con unión de bloques):
    ///   "108 Glorita Cedeño Intro Rmx Cobra Jr.mp3"
    ///   → palabras ["108", "Glorita", "Cedeño", "Intro", "Rmx", "Cobra", "Jr"]
    ///   → si se unen "Glorita"+"Cedeño" → bloques ["108", "Glorita Cedeño",
    ///      "Intro", "Rmx", "Cobra", "Jr"]
    ///
    /// El reorden se aplica SOLO a los índices de bloque que existan en cada
    /// nombre: si un archivo tiene menos bloques que la plantilla, las
    /// posiciones disponibles se reordenan y el resto se conserva tal cual.
    /// </summary>
    public static class NamePartReorderer
    {
        /// <summary>
        /// Divide el nombre base (sin extensión) en partes según el separador.
        /// Recorta espacios y descarta partes vacías (dobles espacios, guiones
        /// seguidos). Devuelve un array vacío si el nombre no es separable.
        /// </summary>
        public static string[] SplitParts(string name, NameSeparator separator)
        {
            var baseName = Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrWhiteSpace(baseName)) return [];

            // Para el espacio se divide por espacios; para el guion se divide
            // por el guion (con o sin espacios alrededor) y se recorta.
            var raw = separator == NameSeparator.Space
                ? baseName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : baseName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return raw.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        }

        /// <summary>
        /// Construye los chips iniciales de un nombre: cada parte del separador
        /// es un bloque de una palabra (WordCount = 1). Se usan en la UI para
        /// arrastrar, reordenar y fusionar.
        /// </summary>
        public static NamePart[] CreateChips(string name, NameSeparator separator)
        {
            var parts = SplitParts(name, separator);
            return parts.Select((p, i) => new NamePart { BlockIndex = i, WordCount = 1, Text = p }).ToArray();
        }

        /// <summary>Une las partes con el separador indicado.</summary>
        public static string JoinParts(string[] parts, NameSeparator separator)
        {
            return string.Join(SeparatorString(separator), parts);
        }

        /// <summary>Une los chips con el separador indicado (para el preview).</summary>
        public static string JoinChips(System.Collections.Generic.IEnumerable<NamePart> chips, NameSeparator separator)
        {
            return string.Join(SeparatorString(separator), chips.Select(c => c.Text));
        }

        /// <summary>
        /// Fusiona dos bloques en uno. El resultado conserva el BlockIndex del
        /// bloque que aparece primero (menor índice original) y el WordCount es
        /// la suma de ambos. Devuelve null si no son contiguos (deben abarcar
        /// un rango continuo de palabras).
        /// </summary>
        public static NamePart? Merge(NamePart a, NamePart b)
        {
            if (a.BlockIndex + a.WordCount != b.BlockIndex &&
                b.BlockIndex + b.WordCount != a.BlockIndex)
            {
                // No son contiguos: no se pueden fusionar en un bloque continuo.
                return null;
            }

            var first = a.BlockIndex <= b.BlockIndex ? a : b;
            var second = first == a ? b : a;

            return new NamePart
            {
                BlockIndex = first.BlockIndex,
                WordCount = a.WordCount + b.WordCount,
                Text = $"{first.Text} {second.Text}"
            };
        }

        /// <summary>
        /// Separa un bloque unido (WordCount &gt; 1) en sus palabras individuales
        /// (bloques de una palabra), manteniendo el orden original.
        /// </summary>
        public static NamePart[] Split(NamePart merged)
        {
            var words = merged.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return words.Select((w, i) => new NamePart { BlockIndex = merged.BlockIndex + i, WordCount = 1, Text = w }).ToArray();
        }

        /// <summary>
        /// Devuelve los tamaños de bloque en ORDEN ORIGINAL (según BlockIndex).
        /// La plantilla usa estos tamaños para reagrupar las palabras de cada
        /// archivo antes de aplicar el reorden.
        /// </summary>
        public static int[] BuildSizes(System.Collections.Generic.IEnumerable<NamePart> chips)
        {
            return chips.OrderBy(c => c.BlockIndex).Select(c => c.WordCount).ToArray();
        }

        /// <summary>
        /// Aplica una permutación (newOrder) a los bloques del nombre y devuelve
        /// el nombre reordenado, conservando la extensión original.
        ///
        /// Reglas:
        ///   - Si el nombre tiene 0 o 1 partes, se devuelve tal cual.
        ///   - newOrder[i] indica qué índice de bloque original ocupa la posición i.
        ///   - Si newOrder[i] no existe en este nombre (tiene menos bloques que
        ///     la plantilla), la posición i conserva su bloque original.
        ///   - Los tamaños de bloque de la plantilla (sizes) se usan para
        ///     reagrupar las palabras de este archivo en bloques equivalentes.
        /// </summary>
        public static string ReorderName(string name, NameSeparator separator, int[] sizes, int[] newOrder)
        {
            var ext = Path.GetExtension(name);
            var words = SplitParts(name, separator);
            if (words.Length <= 1) return name;

            // Agrupa las palabras de este archivo en bloques usando los tamaños
            // de la plantilla (en orden original). Si el archivo tiene menos
            // palabras, el último bloque queda corto.
            var blocks = new System.Collections.Generic.List<string[]>();
            int wi = 0;
            for (int i = 0; i < sizes.Length && wi < words.Length; i++)
            {
                int count = Math.Min(sizes[i], words.Length - wi);
                blocks.Add(words[wi..(wi + count)]);
                wi += count;
            }
            // Palabras sobrantes (archivo con más palabras que la plantilla):
            // se conservan como un bloque extra al final.
            if (wi < words.Length)
                blocks.Add(words[wi..]);

            // Reordena los bloques por la permutación.
            var result = new string[blocks.Count];
            for (int i = 0; i < blocks.Count; i++)
            {
                int src = i < newOrder.Length ? newOrder[i] : i;
                result[i] = src < blocks.Count ? string.Join(" ", blocks[src]) : string.Join(" ", blocks[i]);
            }

            return string.Join(SeparatorString(separator), result) + ext;
        }

        /// <summary>Cadena usada como separador al unir bloques.</summary>
        private static string SeparatorString(NameSeparator separator)
        {
            return separator == NameSeparator.Space ? " " : " - ";
        }
    }
}
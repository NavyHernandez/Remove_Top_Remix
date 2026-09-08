using System.Linq;

namespace Remove_Top.Features.TagRemoval
{
    /// <summary>
    /// Ítem del listado de análisis: un archivo de audio con sus tags actuales
    /// (título, artista, álbum y presencia de portada).
    /// </summary>
    public class TagFileItem
    {
        /// <summary>Ruta completa del archivo.</summary>
        public string FilePath { get; set; } = "";

        /// <summary>Nombre del archivo (sin ruta).</summary>
        public string FileName { get; set; } = "";

        /// <summary>Título actual (vacío si no hay tags).</summary>
        public string Title { get; set; } = "";

        /// <summary>Intérprete actual.</summary>
        public string Artist { get; set; } = "";

        /// <summary>Álbum actual.</summary>
        public string Album { get; set; } = "";

        /// <summary>Indica si el archivo tiene imagen de portada.</summary>
        public bool HasCover { get; set; }

        /// <summary>Línea secundaria de la lista: "Artista – Título" o "Sin etiquetas".</summary>
        public string CurrentInfo
        {
            get
            {
                if (string.IsNullOrEmpty(Artist) && string.IsNullOrEmpty(Title))
                    return "Sin etiquetas";
                return string.Join(" – ", new[] { Artist, Title }.Where(s => !string.IsNullOrEmpty(s)));
            }
        }

        /// <summary>Indicador de portada de la lista ("Portada" si existe, vacío si no).</summary>
        public string CoverStatus => HasCover ? "Portada" : "";
    }
}
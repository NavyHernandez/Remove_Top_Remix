namespace Remove_Top.Features.TagRemoval
{
    /// <summary>
    /// Valores que se escriben al reemplazar etiquetas. Se aplican los mismos
    /// a todos los archivos cargados. Los campos vacíos se limpian.
    /// </summary>
    public class TagValues
    {
        /// <summary>Título de la canción.</summary>
        public string Title { get; set; } = "";

        /// <summary>Intérprete / artista (TagLib Performers).</summary>
        public string Artist { get; set; } = "";

        /// <summary>Artista del álbum (TagLib AlbumArtists).</summary>
        public string AlbumArtist { get; set; } = "";

        /// <summary>Álbum.</summary>
        public string Album { get; set; } = "";

        /// <summary>Género musical.</summary>
        public string Genre { get; set; } = "";

        /// <summary>Año de publicación (texto; solo se escribe si es numérico).</summary>
        public string Year { get; set; } = "";

        /// <summary>Comentario.</summary>
        public string Comment { get; set; } = "";

        /// <summary>
        /// Ruta de la imagen de portada nueva. Si es null se conserva la
        /// portada existente (en el modo reemplazo).
        /// </summary>
        public string? CoverPath { get; set; }
    }
}
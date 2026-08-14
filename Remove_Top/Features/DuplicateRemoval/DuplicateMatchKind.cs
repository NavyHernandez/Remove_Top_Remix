namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>Tipo de coincidencia de un archivo repetido.</summary>
    public enum DuplicateMatchKind
    {
        /// <summary>Contenido idéntico (mismo hash SHA-256).</summary>
        Exact,

        /// <summary>
        /// Misma canción por nombre: mismo nombre normalizado (sin extensión;
        /// se ignoran mayúsculas, acentos, guiones, espacios e incluso guiones
        /// iniciales). Es la misma canción aunque el contenido/tamaño difiera
        /// (re-codificación o descarga distinta). Se marca por defecto.
        /// </summary>
        SameName,

        /// <summary>
        /// Mismo nombre normalizado pero contenido claramente distinto (mantenido
        /// por compatibilidad; los grupos por nombre actuales se clasifican como
        /// <see cref="SameName"/>).
        /// </summary>
        ProbableByName,

        /// <summary>
        /// Coincidencia de la 1.ª o 2.ª palabra significativa del nombre
        /// (coincidencia difusa: mismas canciones/artistas en variantes).
        /// </summary>
        ProbableByKeyword,

        /// <summary>
        /// Nombre contenido: todas las palabras del nombre más corto aparecen
        /// en el más largo (subconjunto de palabras). Si la duración coincide,
        /// se considera la misma canción con descriptores extra; si no, se
        /// desmarca para que el usuario revise.
        /// </summary>
        SubsetMatch,

        /// <summary>Tamaño menor al mínimo válido (probablemente dañado).</summary>
        Damaged
    }
}

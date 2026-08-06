namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>Tipo de coincidencia de un archivo repetido.</summary>
    public enum DuplicateMatchKind
    {
        /// <summary>Contenido idéntico (mismo hash SHA-256).</summary>
        Exact,

        /// <summary>
        /// Mismo nombre normalizado (sin extensión; se ignoran mayúsculas,
        /// acentos, guiones, espacios e incluso guiones iniciales), pero
        /// contenido distinto.
        /// </summary>
        ProbableByName,

        /// <summary>
        /// Coincidencia de la 1.ª o 2.ª palabra significativa del nombre
        /// (coincidencia difusa: mismas canciones/artistas en variantes).
        /// </summary>
        ProbableByKeyword,

        /// <summary>Tamaño menor al mínimo válido (probablemente dañado).</summary>
        Damaged
    }
}

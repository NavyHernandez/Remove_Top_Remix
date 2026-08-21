namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Registro de un archivo candidato durante el escaneo: tamaño, hash
    /// (se rellena solo para tamaños repetidos) y el nombre normalizado con
    /// sus palabras significativas, precalculados una sola vez por archivo
    /// para no repetir trabajo en cada detector.
    /// </summary>
    internal sealed class FileRecord
    {
        public string FilePath { get; set; } = "";
        public long Size { get; set; }
        public string Hash { get; set; } = "";
        public string NormalizedName { get; set; } = "";

        /// <summary>
        /// Palabras significativas del BLOQUE DE TÍTULO del nombre (hasta 8),
        /// ya limpias y minúsculas. El artista (bloque previo al último guion)
        /// no se incluye: evitaría falsos positivos entre canciones distintas
        /// del mismo intérprete.
        /// </summary>
        public string[] Words { get; set; } = [];
    }
}

namespace Remove_Top.Features.TagRemoval
{
    /// <summary>
    /// Datos de progreso del análisis y del procesamiento para la UI.
    /// <see cref="Result"/> se rellena solo al terminar un archivo.
    /// </summary>
    public class TagProgress
    {
        /// <summary>Índice del archivo actual (1-based).</summary>
        public int CurrentIndex { get; set; }

        /// <summary>Total de archivos del lote.</summary>
        public int TotalCount { get; set; }

        /// <summary>Nombre del archivo que se está procesando.</summary>
        public string CurrentFile { get; set; } = "";

        /// <summary>Resultado del archivo recién terminado (opcional).</summary>
        public TagResult? Result { get; set; }

        /// <summary>Porcentaje global completado (0-100).</summary>
        public double Percentage =>
            TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0;
    }
}
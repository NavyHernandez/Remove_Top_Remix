using System.Globalization;

namespace Remove_Top.Helpers
{
    /// <summary>
    /// LÍMITES CENTRALIZADOS DE LA VERSIÓN GRATUITA.
    ///
    /// Este es el ÚNICO lugar donde se definen los límites de cada funcionalidad
    /// de la aplicación. Para cambiar manualmente cualquier límite basta con
    /// editar el valor de la constante correspondiente aquí abajo: toda la
    /// lógica de negocio (los <c>Take(n)</c>, comparaciones y topeos de cada
    /// servicio) y los textos de la UI (InfoBars, contadores y descripciones)
    /// se generan a partir de estas constantes, por lo que nunca se desincronizan.
    ///
    /// Convenciones de nombres:
    ///   - "FreeLimitDisplay"  → límite PUBLICADO en la UI (marketing).
    ///   - "MaxFilesToScan"    → límite REAL de procesamiento/escaneo.
    ///   - "Max...PerBatch"    → tope por lote de ejecución.
    ///   - "Min...Bytes"       → umbral técnico de validación de archivos.
    ///
    /// Si un límite se muestra formateado en la UI (p. ej. "1.000"), se usa la
    /// sobrecarga <see cref="N0(int)"/> para conservar el separador de miles.
    /// </summary>
    public static class AppLimits
    {
        // ====================================================================
        // NORMALIZACIÓN (Features/Normalization)
        // ====================================================================

        /// <summary>
        /// Límite PUBLICADO de archivos para la versión gratuita. Solo es texto
        /// de marketing: se muestra en el InfoBar de la página de Normalización,
        /// pero el procesamiento real sigue el límite de
        /// <see cref="NormalizationMaxFilesToScan"/>.
        /// </summary>
        public const int NormalizationFreeLimitDisplay = 50;

        /// <summary>
        /// Límite REAL de archivos que se analizan/procesan por ejecución en la
        /// Normalización (AudioNormalizer). Por encima de esta cantidad el resto
        /// de archivos se omite.
        /// </summary>
        public const int NormalizationMaxFilesToScan = 1000;

        // ====================================================================
        // RENOMBRADO MASIVO (Features/BatchRename)
        // ====================================================================

        /// <summary>
        /// Máximo de patrones de texto que el usuario puede tener activos a la
        /// vez en el Renombrado Masivo. Los patrones se persisten en
        /// %LOCALAPPDATA%\Remove_Top\patterns.json y la UI los limita a este tope.
        /// </summary>
        public const int BatchRenameMaxPatterns = 20;

        // ====================================================================
        // EXTRACCIÓN DE STEMS (Features/VocalRemoval)
        // ====================================================================

        /// <summary>
        /// Máximo de canciones estéreo que se pueden procesar por lote en la
        /// Extracción de Stems (VocalSeparator). La UI encola hasta este número
        /// de archivos por ejecución.
        /// </summary>
        public const int VocalRemovalMaxFilesPerBatch = 5;

        // ====================================================================
        // ELIMINACIÓN DE DUPLICADOS (Features/DuplicateRemoval)
        // ====================================================================

        /// <summary>
        /// Límite REAL de archivos que se escanean por ejecución en la detección
        /// de duplicados (DuplicateScanner). Los archivos que superen esta
        /// cantidad se ignoran en el escaneo.
        /// </summary>
        public const int DuplicatesMaxFilesToScan = 1000;

        /// <summary>
        /// Máximo de archivos que se eliminan por ejecución en la Eliminación de
        /// Duplicados (DuplicateRemover). Se aplica tanto a la Papelera como al
        /// borrado definitivo.
        /// </summary>
        public const int DuplicatesMaxDeletionsPerRun = 1000;

        /// <summary>
        /// Tamaño mínimo (en bytes) para considerar válido un archivo en la
        /// detección de duplicados. Los archivos más pequeños se clasifican como
        /// "dañados" y se excluyen de la agrupación de duplicados (evita falsos
        /// positivos por hash de archivos vacíos).
        /// </summary>
        public const long DuplicatesMinValidFileSizeBytes = 6 * 1024;

        // ====================================================================
        // TEXTOS DE LA UI (generados a partir de las constantes)
        // ====================================================================

        /// <summary>Formatea un número entero con separador de miles ("1.000").</summary>
        public static string N0(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Título del InfoBar de la página de Normalización.</summary>
        public static string NormalizationInfoBarTitle =>
            $"Versión gratuita: hasta {N0(NormalizationFreeLimitDisplay)} archivos";

        /// <summary>Mensaje del InfoBar de la página de Normalización.</summary>
        public static string NormalizationInfoBarMessage =>
            $"El escaneo es recursivo e incluye las subcarpetas. Si la carpeta tiene más de " +
            $"{N0(NormalizationFreeLimitDisplay)} archivos.";

        /// <summary>Título del InfoBar de la página de Eliminación de Duplicados.</summary>
        public static string DuplicatesInfoBarTitle =>
            $"Versión gratuita: hasta {N0(DuplicatesMaxFilesToScan)} archivos";

        /// <summary>Mensaje del InfoBar de la página de Eliminación de Duplicados.</summary>
        public static string DuplicatesInfoBarMessage =>
            $"El escaneo es recursivo e incluye las subcarpetas. Si la carpeta tiene más de " +
            $"{N0(DuplicatesMaxFilesToScan)} archivos, se analizan los primeros {N0(DuplicatesMaxFilesToScan)}.";

        /// <summary>Aviso de límite de patrones en el Renombrado Masivo.</summary>
        public static string BatchRenameLimitMessage =>
            $"Máximo {BatchRenameMaxPatterns} patrones. La búsqueda no distingue mayúsculas/minúsculas.";

        /// <summary>Descripción de la página de Extracción de Stems.</summary>
        public static string VocalRemovalPageDescription =>
            $"Separa la voz del instrumental usando IA (HD-Demucs). Máximo " +
            $"{VocalRemovalMaxFilesPerBatch} canciones por lote.";

        // ====================================================================
        // TÍTULOS, SUBTÍTULOS Y BADGES DE LAS PÁGINAS
        // (textos del encabezado de cada funcionalidad, montados en runtime
        // por cada página para que se cambien en un solo lugar)
        // ====================================================================

        /// <summary>Texto del badge "Versión Gratuita" que acompaña a los avisos de límite.</summary>
        public const string FreeBadgeText = "Versión Gratuita";

        /// <summary>Título del encabezado de la página de Normalización.</summary>
        public const string NormalizationPageTitle = "Normalización de Audio";

        /// <summary>Subtítulo del encabezado de la página de Normalización.</summary>
        public const string NormalizationPageSubtitle =
            "Ajusta el nivel de pico de todos los archivos de audio a un objetivo común (dBFS).";

        /// <summary>Título del encabezado de la página de Renombrado Masivo.</summary>
        public const string BatchRenamePageTitle = "Renombrado Masivo";

        /// <summary>Subtítulo del encabezado de la página de Renombrado Masivo.</summary>
        public const string BatchRenamePageSubtitle =
            "Elimina texto específico del nombre de archivos. Soporta audio, video, imagen y documentos.";

        /// <summary>Título del encabezado de la página de Edición Rápida.</summary>
        public const string QuickRenamePageTitle = "Edición Rápida de Nombres";

        /// <summary>Subtítulo del encabezado de la página de Edición Rápida.</summary>
        public const string QuickRenamePageSubtitle =
            "Lista los archivos .mp3 y .wav de la carpeta y edita sus nombres de forma rápida.";

        /// <summary>Título del encabezado de la página de Extracción de Stems.</summary>
        public const string VocalRemovalPageTitle = "Stems — Extraer Voz";

        /// <summary>
        /// Subtítulo del encabezado de la página de Extracción de Stems
        /// (alias de <see cref="VocalRemovalPageDescription"/>, que ya incluye
        /// el máximo de canciones por lote).
        /// </summary>
        public static string VocalRemovalPageSubtitle => VocalRemovalPageDescription;

        /// <summary>Título del encabezado de la página de Eliminación de Duplicados.</summary>
        public const string DuplicatesPageTitle = "Eliminación de Duplicados";

        /// <summary>Subtítulo del encabezado de la página de Eliminación de Duplicados.</summary>
        public const string DuplicatesPageSubtitle =
            "Escanea una carpeta (incluye subcarpetas), detecta duplicados, posibles y archivos dañados";
    }
}

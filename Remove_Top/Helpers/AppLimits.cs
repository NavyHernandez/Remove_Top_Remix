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
        // IDENTIDAD DE LA APLICACIÓN (branding central)
        // ====================================================================

        /// <summary>
        /// Nombre de marca de la aplicación: título de ventana, menú de
        /// navegación y badges de cabecera de todas las páginas.
        /// CAMBIAR AQUÍ para renombrar la app en toda la UI.
        /// </summary>
        public const string AppName = "One Dj App";

        /// <summary>
        /// Subtítulo que acompaña al nombre en el menú de navegación.
        /// CAMBIAR AQUÍ (editable desde el módulo de límites).
        /// </summary>
        public const string AppSubtitle = "Organiza tu mundo musical";

        /// <summary>
        /// Sitio web de marca que se muestra en las páginas (no cambiar el
        /// dominio; solo centraliza el texto para editarlo en un solo lugar).
        /// </summary>
        public const string AppBrandSite = "www.top-remix.com";

        /// <summary>
        /// Nombre de la carpeta de datos persistente en %LOCALAPPDATA%.
        /// Se conserva "Remove_Top" para no perder patrones guardados
        /// (patterns.json) ni el modelo ONNX descargado.
        /// </summary>
        public const string AppDataFolderName = "Remove_Top";

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

        /// <summary>
        /// Límite REAL de archivos que se procesan por ejecución en el
        /// Renombrado Masivo (FileRenamer). El escaneo es recursivo e incluye
        /// las subcarpetas; por encima de esta cantidad el resto se omite.
        /// </summary>
        public const int BatchRenameMaxFilesToScan = 1000;

        // ====================================================================
        // EDICIÓN RÁPIDA (Features/QuickRename)
        // ====================================================================

        /// <summary>
        /// Máximo de archivos .mp3/.wav que se listan por ejecución en la
        /// Edición Rápida (QuickRenamer). Solo se muestran los primeros N
        /// archivos de la carpeta.
        /// </summary>
        public const int QuickRenameMaxFilesToScan = 200;

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

        /// <summary>Aviso de límite de archivos en el Renombrado Masivo (junto al badge "Versión Gratuita").</summary>
        public static string BatchRenameFilesLimitMessage =>
            $"Se procesan hasta {N0(BatchRenameMaxFilesToScan)} archivos por ejecución. El escaneo es recursivo e incluye las subcarpetas.";

        /// <summary>Aviso de límite de archivos en la Edición Rápida.</summary>
        public static string QuickRenameLimitMessage =>
            $"Se muestran los primeros {N0(QuickRenameMaxFilesToScan)} archivos .mp3/.wav de la carpeta.";

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

        /// <summary>Título del encabezado de la página Cuenta (perfil/uso/actualizaciones).</summary>
        public const string AccountPageTitle = "Cuenta";

        /// <summary>Subtítulo del encabezado de la página Cuenta.</summary>
        public const string AccountPageSubtitle =
            "Tu perfil, el estado de la aplicación y las estadísticas de tu uso.";

        /// <summary>Máximo de caracteres permitidos en el cuadro de sugerencias (página Cuenta).</summary>
        public const int SuggestionsMaxLength = 1000;

        /// <summary>Título de la sección de sugerencias de la página Cuenta.</summary>
        public const string SuggestionsTitle = "Sugerencias y feedback";

        /// <summary>Subtítulo de la sección de sugerencias de la página Cuenta.</summary>
        public const string SuggestionsSubtitle =
            "Tu opinión nos ayuda a mejorar One Dj App. Cuéntanos qué te gustaría añadir o qué encontramos mal.";
    }
}

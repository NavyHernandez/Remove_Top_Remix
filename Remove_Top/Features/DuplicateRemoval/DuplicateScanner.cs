using Remove_Top.Features.DuplicateRemoval.Detection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.DuplicateRemoval
{
    /// <summary>
    /// Servicio de detección de archivos duplicados.
    /// Escanea de forma recursiva una o varias carpetas (máx. MaxFilesToScan)
    /// y agrupa el resultado ejecutando los detectores por prioridad:
    /// misma canción por nombre normalizado (SameName) → exactos por hash →
    /// palabra clave. Cada archivo cae en un único grupo. El hash SHA-256 solo
    /// se calcula (en paralelo) para los archivos NO reclamados por nombre y
    /// con tamaño repetido, lo que reduce mucho el trabajo en bibliotecas
    /// musicales (la mayoría de los duplicados comparten nombre).
    /// Mantiene separada la detección de archivos dañados (&lt; MinValidFileSizeBytes).
    /// </summary>
    public class DuplicateScanner
    {
        /// <summary>Límite de archivos analizados por ejecución (versión gratuita).</summary>
        public const int MaxFilesToScan = 1000;

        /// <summary>
        /// Tamaño mínimo (en bytes) para considerar un archivo válido.
        /// Archivos menores se consideran probablemente dañados y se excluyen
        /// de la agrupación de duplicados (evita falsos positivos por hash de
        /// archivos vacíos).
        /// </summary>
        public const int MinValidFileSizeBytes = 6 * 1024;

        /// <summary>Paso de notificación de progreso (archivos) para no saturar la UI.</summary>
        private const int ProgressStep = 25;

        /// <summary>
        /// Encuentra todos los archivos de la carpeta (búsqueda recursiva:
        /// incluye subcarpetas anidadas), tolerando errores de permisos.
        /// Excluye archivos basura de macOS (sidecars AppleDouble "._*" y
        /// ".DS_Store") que tienen contenido idéntico y provocarían
        /// falsos positivos de duplicados.
        /// </summary>
        public static string[] GetAllFiles(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return [];

            try
            {
                return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(p => !IsMacJunk(Path.GetFileName(p)))
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Indica si el nombre corresponde a un archivo basura de macOS
        /// (sidecar AppleDouble "._*" o índice ".DS_Store").
        /// </summary>
        private static bool IsMacJunk(string fileName) =>
            fileName.StartsWith("._", StringComparison.Ordinal) ||
            string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Normaliza el nombre de un archivo para comparar duplicados por
        /// nombre (delegado en <see cref="NameNormalizer"/>).
        /// </summary>
        public static string NormalizeName(string filePath) => NameNormalizer.Normalize(filePath);

        /// <summary>
        /// Escanea la carpeta (incluyendo subcarpetas), analiza hasta
        /// MaxFilesToScan archivos y agrupa los duplicados: misma canción por
        /// nombre normalizado (SameName, marcados), exactos por hash y posibles
        /// por palabra clave. Los grupos por nombre y por palabra se verifican
        /// después con la duración real de los archivos de audio
        /// (DurationVerifier). Por rendimiento el hash SHA-256 solo se calcula
        /// para los archivos cuyo tamaño se repite entre los no reclamados por
        /// nombre, y en paralelo. Reporta fases y avance mediante IProgress y
        /// soporta cancelación.
        /// </summary>
        public async Task<DuplicateScanResult> ScanAsync(
            string folderPath,
            IProgress<ScanProgress> progress,
            CancellationToken cancellationToken = default)
        {
            progress.Report(new ScanProgress { Phase = "Enumerando archivos..." });

            var allFiles = GetAllFiles(folderPath);
            int totalFound = allFiles.Length;
            var files = allFiles.Take(MaxFilesToScan).ToArray();
            int total = files.Length;

            // Fase 1: leer el tamaño de cada archivo (rápido, sin hashear) y
            // precalcular una sola vez el nombre normalizado y sus palabras.
            var records = new FileRecord[total];
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldReport(i, total))
                {
                    progress.Report(new ScanProgress
                    {
                        Phase = "Leyendo tamaños...",
                        Current = i + 1,
                        Total = total
                    });
                }
                records[i] = BuildRecord(files[i]);
            }

            var damagedRecords = records
                .Where(r => r.Size >= 0 && r.Size < MinValidFileSizeBytes)
                .ToArray();

            var valid = records.Where(r => r.Size >= MinValidFileSizeBytes).ToArray();

            // Detección por prioridad, optimizada para bibliotecas musicales:
            //   1) MISMA CANCIÓN por NOMBRE NORMALIZADO (barato: compara strings,
            //      sin hashear). "Pipe Bueno   Te Parece Poco" vs "PIPE BUENO -
            //      TE PARECE POCO" o "JESSI URIBE - SOBREVIVIRE" vs "Jessi Uribe
            //      Sobreviviré" caen aquí y quedan marcados como exactos.
            //   2) Exactos por HASH entre lo NO reclamado por nombre (solo tamaños
            //      repetidos, en paralelo). Captura duplicados byte-idénticos con
            //      nombres distintos.
            //   3) Posibles por PALABRA CLAVE entre lo restante.
            // Como el nombre explica la mayoría de los duplicados de una biblioteca
            // musical, se reduce drásticamente el número de hashes a calcular.
            var byNameGroups = new NormalizedNameDetector().Detect(valid).ToList();
            var used = CollectPaths(byNameGroups);

            var remainingForHash = valid.Where(r => !used.Contains(r.FilePath)).ToArray();

            // Solo se hashean los archivos cuyo tamaño se repite, porque un
            // archivo único en tamaño no puede tener un duplicado exacto.
            var toHash = remainingForHash
                .GroupBy(r => r.Size)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .ToArray();

            if (toHash.Length > 0)
            {
                int totalHash = toHash.Length;
                int processed = 0;
                await Task.Run(() =>
                {
                    Parallel.For(0, totalHash, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                        CancellationToken = cancellationToken
                    }, i =>
                    {
                        var record = toHash[i];
                        record.Hash = ComputeHash(record.FilePath);
                        int n = Interlocked.Increment(ref processed);
                        if (n == 1 || n == totalHash || n % ProgressStep == 0)
                        {
                            progress.Report(new ScanProgress
                            {
                                Phase = "Calculando hashes (solo tamaños repetidos)...",
                                Current = n,
                                Total = totalHash
                            });
                        }
                    });
                }, cancellationToken);
            }

            var exactByHashGroups = new ExactHashDetector().Detect(remainingForHash).ToList();
            used.UnionWith(CollectPaths(exactByHashGroups));

            var remainingByKeyword = valid.Where(r => !used.Contains(r.FilePath)).ToArray();
            var keywordGroups = new KeywordDetector().Detect(remainingByKeyword).ToList();

            // Verificación por duración de audio:
            //   - SameName (misma canción por nombre): adjunta la duración para
            //     mostrar y aplica una salvaguarda (duración MUY distinta > 2x
            //     degrade el ítem a desmarcado para evitar títulos idénticos de
            //     canciones distintas).
            //   - Keyword: elimina falsos positivos por duración muy distinta.
            var verified = await DurationVerifier.VerifyAsync(
                byNameGroups.Concat(keywordGroups).ToList(),
                progress,
                cancellationToken);

            var exactGroups = exactByHashGroups.Concat(
                verified.Where(g => g.Duplicates.Count > 0 &&
                    g.Duplicates[0].MatchKind == DuplicateMatchKind.SameName)).ToList();
            var possibleGroups = verified.Where(g => g.Duplicates.Count > 0 &&
                g.Duplicates[0].MatchKind != DuplicateMatchKind.SameName).ToList();

            return new DuplicateScanResult
            {
                ExactGroups = exactGroups,
                PossibleGroups = possibleGroups,
                DamagedFiles = DamagedFileDetector.Detect(damagedRecords),
                ScannedFiles = total,
                TotalFilesFound = totalFound
            };
        }

        /// <summary>
        /// Decide si se notifica el progreso en el paso <paramref name="index"/>
        /// (0-based): primero, último o cada ProgressStep archivos.
        /// </summary>
        private static bool ShouldReport(int index, int total) =>
            total <= 0 || index == 0 || index == total - 1 || index % ProgressStep == 0;

        /// <summary>Construye el registro de un archivo con sus datos precalculados.</summary>
        private static FileRecord BuildRecord(string filePath)
        {
            return new FileRecord
            {
                FilePath = filePath,
                Size = GetFileSize(filePath),
                NormalizedName = NameNormalizer.Normalize(filePath),
                Words = NameNormalizer.GetTitleWords(filePath)
            };
        }

        /// <summary>Reúne todas las rutas (keeper + duplicados) de los grupos.</summary>
        private static HashSet<string> CollectPaths(IEnumerable<DuplicateGroup> groups)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                if (group.KeeperPath.Length > 0) paths.Add(group.KeeperPath);
                foreach (var duplicate in group.Duplicates)
                    paths.Add(duplicate.FilePath);
            }
            return paths;
        }

        /// <summary>Obtiene el tamaño de un archivo; -1 si no se puede leer.</summary>
        private static long GetFileSize(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>Calcula el hash SHA-256 de un archivo.</summary>
        private static string ComputeHash(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}

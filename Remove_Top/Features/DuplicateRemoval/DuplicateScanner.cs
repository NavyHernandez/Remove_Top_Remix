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
    /// Escanea de forma recursiva una o varias carpetas (máx. MaxFilesToScan),
    /// calcula el hash SHA-256 solo de los archivos con tamaño repetido (en
    /// paralelo) y agrupa el resultado ejecutando los detectores por prioridad:
    /// exactos (hash) → mismos nombre normalizado → misma palabra clave.
    /// Cada archivo cae en un único grupo. Mantiene separada la detección de
    /// archivos dañados (&lt; MinValidFileSizeBytes).
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
        /// MaxFilesToScan archivos y agrupa los duplicados: exactos por hash,
        /// posibles por nombre normalizado y posibles por 1.ª/2.ª palabra.
        /// Los posibles se verifican después con la duración real de los
        /// archivos de audio (DurationVerifier) para descartar falsos positivos.
        /// Por rendimiento solo calcula el hash SHA-256 de los archivos cuyo
        /// tamaño se repite (los únicos que pueden ser duplicados exactos) y
        /// lo hace en paralelo. Reporta fases y avance mediante IProgress y
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

            // Fase 2: solo se hashean los archivos cuyo tamaño se repite,
            // porque un archivo único en tamaño no puede tener un duplicado exacto.
            var toHash = valid
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

            // Detección por prioridad: exactos → mismo nombre → misma palabra.
            // Los archivos reclamados por un detector se excluyen del siguiente.
            var exactGroups = new ExactHashDetector().Detect(valid).ToList();

            var used = CollectPaths(exactGroups);

            var remainingByName = valid.Where(r => !used.Contains(r.FilePath)).ToArray();
            var byNameGroups = new NormalizedNameDetector().Detect(remainingByName).ToList();
            used.UnionWith(CollectPaths(byNameGroups));

            var remainingByKeyword = valid.Where(r => !used.Contains(r.FilePath)).ToArray();
            var keywordGroups = new KeywordDetector().Detect(remainingByKeyword).ToList();

            // Verificación por duración de audio: elimina falsos positivos por
            // palabra clave (duración muy distinta) y confirma por duración los
            // "posibles por nombre" de tamaño distinto (misma canción).
            var possibleGroups = await DurationVerifier.VerifyAsync(
                byNameGroups.Concat(keywordGroups).ToList(),
                progress,
                cancellationToken);

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

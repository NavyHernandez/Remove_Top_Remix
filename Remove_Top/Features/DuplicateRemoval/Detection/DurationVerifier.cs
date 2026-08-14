using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.DuplicateRemoval.Detection
{
    /// <summary>
    /// Verifica los grupos "posibles" con la duración real de los archivos de
    /// audio (NAudio MediaFoundationReader), leída en paralelo y solo para los
    /// archivos que caen en un grupo.
    ///
    /// - Grupos por palabra clave (ProbableByKeyword): elimina los miembros cuya
    ///   duración difiere mucho de la referencia del grupo (falso positivo:
    ///   canciones distintas que comparten palabras). Si el grupo se queda sin
    ///   duplicados, se descarta.
    /// - Grupos por nombre (SameName): ya vienen marcados; aquí solo se adjunta
    ///   la duración para mostrar y se aplica una salvaguarda: si un miembro
    ///   tiene una duración MUY distinta a la referencia (&gt; SameNameMaxDurationRatio),
    ///   se desmarca (posible título idéntico de otra canción).
    /// - Grupos por nombre contenido (SubsetMatch): tras la coincidencia de
    ///   palabras se confirma el duplicado SOLO si comparte tamaño exacto o si
    ///   la duración es prácticamente igual (&lt;= SubsetMatchDurationTolerance).
    ///   Si no se confirma (p. ej. 4:10 vs 3:31), son canciones distintas que
    ///   comparten palabras y se eliminan del grupo (falso positivo).
    ///
    /// Los archivos que no son audio o cuya duración no se puede leer (null)
    /// no son verificables y se conservan con la lógica de detección original.
    /// </summary>
    internal static class DurationVerifier
    {
        /// <summary>Tolerancia relativa máxima de duración entre copias de un mismo tema.</summary>
        public const double DurationTolerance = 0.30;

        /// <summary>
        /// Razón máxima (duración mayor / menor) para mantener marcado un miembro
        /// "misma canción por nombre". Por encima se considera otro tema y se
        /// desmarca (salvaguarda frente a títulos idénticos de canciones distintas).
        /// </summary>
        public const double SameNameMaxDurationRatio = 2.0;

        /// <summary>
        /// Tolerancia relativa de duración para confirmar un "nombre contenido"
        /// como la misma canción. Más estricta que <see cref="DurationTolerance"/>:
        /// canciones distintas que comparten palabras suelen diferir bastante en
        /// duración (p. ej. 4:10 vs 3:31 = 16% &gt; esta tolerancia → no duplicado).
        /// </summary>
        public const double SubsetMatchDurationTolerance = 0.10;

        /// <summary>Piso (segundos): por debajo no se descarta/marca por duración (ruido en pistas cortas).</summary>
        private const double MinRelevantSeconds = 5.0;

        /// <summary>Paso de notificación de progreso (archivos) para no saturar la UI.</summary>
        private const int ProgressStep = 25;

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".oga",
            ".opus", ".wma", ".mp4", ".m4b", ".aiff", ".aif", ".ape", ".wv"
        };

        public static async Task<List<DuplicateGroup>> VerifyAsync(
            IReadOnlyList<DuplicateGroup> groups,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            if (groups.Count == 0) return [.. groups];

            // Reúne las rutas de todos los miembros (keeper + duplicados).
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                if (group.KeeperPath.Length > 0) paths.Add(group.KeeperPath);
                foreach (var dup in group.Duplicates)
                    paths.Add(dup.FilePath);
            }

            var audioPaths = paths.Where(IsAudioPath).ToArray();
            var durations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (audioPaths.Length > 0)
            {
                int total = audioPaths.Length;
                int processed = 0;
                await Task.Run(() =>
                {
                    Parallel.For(0, total, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                        CancellationToken = cancellationToken
                    }, i =>
                    {
                        string path = audioPaths[i];
                        double? d = ReadDuration(path);
                        if (d.HasValue)
                        {
                            lock (durations) durations[path] = d.Value;
                        }
                        int n = Interlocked.Increment(ref processed);
                        if (n == 1 || n == total || n % ProgressStep == 0)
                        {
                            progress?.Report(new ScanProgress
                            {
                                Phase = "Verificando duración de audio...",
                                Current = n,
                                Total = total
                            });
                        }
                    });
                }, cancellationToken);
            }

            var result = new List<DuplicateGroup>(groups.Count);
            foreach (var group in groups)
            {
                var verified = VerifyGroup(group, durations);
                if (verified != null) result.Add(verified);
            }
            return result;
        }

        /// <summary>
        /// Aplica la verificación por duración a un único grupo. Devuelve null
        /// si el grupo queda sin duplicados (todos eran falsos positivos).
        /// </summary>
        private static DuplicateGroup? VerifyGroup(DuplicateGroup group, IReadOnlyDictionary<string, double> durations)
        {
            DuplicateMatchKind kind = group.Duplicates.Count > 0
                ? group.Duplicates[0].MatchKind
                : DuplicateMatchKind.ProbableByName;
            bool isKeyword = kind == DuplicateMatchKind.ProbableByKeyword;
            bool isSameName = kind == DuplicateMatchKind.SameName;
            bool isSubsetMatch = kind == DuplicateMatchKind.SubsetMatch;

            // Referencia de duración del grupo: el keeper o cualquier duplicado conocido.
            double? reference = GetDuration(group.KeeperPath, durations);
            if (reference is null)
            {
                foreach (var item in group.Duplicates)
                {
                    var d = GetDuration(item.FilePath, durations);
                    if (d.HasValue) { reference = d; break; }
                }
            }

            var remaining = new List<DuplicateItem>(group.Duplicates.Count);
            foreach (var item in group.Duplicates)
            {
                double? itemDur = GetDuration(item.FilePath, durations);
                item.DurationSeconds = itemDur;
                item.ReferenceDurationSeconds = reference;

                bool durationMatches = itemDur.HasValue && reference.HasValue &&
                    WithinTolerance(itemDur.Value, reference.Value);

                if (isKeyword)
                {
                    // Palabra clave: si la duración se conoce y no coincide, es
                    // un falso positivo (canciones distintas que comparten palabras).
                    if (itemDur.HasValue && reference.HasValue && !durationMatches) continue;
                }
                else if (isSameName)
                {
                    // Misma canción por nombre: ya viene marcada.
                    // Salvaguarda: si ambas duraciones se conocen y difieren MUCHO
                    // (otro tema con título idéntico), se desmarca para revisar.
                    item.DurationMatches = durationMatches;
                    if (itemDur.HasValue && reference.HasValue &&
                        TooDifferent(itemDur.Value, reference.Value))
                    {
                        item.IsMarkedForDeletion = false;
                    }
                }
                else if (isSubsetMatch)
                {
                    // Nombre contenido: la coincidencia de palabras no basta.
                    // Se confirma el duplicado SOLO si comparte tamaño exacto
                    // (misma canción recodificada) o si la duración es
                    // prácticamente igual (misma canción, misma longitud). Si
                    // ninguna se cumple, son canciones distintas que comparten
                    // palabras y se eliminan del grupo (falso positivo).
                    bool sizeConfirms = item.SameSize;
                    bool durationConfirms = itemDur.HasValue && reference.HasValue &&
                        SubsetDurationsMatch(itemDur.Value, reference.Value);
                    item.DurationMatches = durationConfirms;
                    if (!sizeConfirms && !durationConfirms) continue;
                }
                else
                {
                    // Por nombre (legacy): se marca si comparte tamaño O si la
                    // duración coincide (misma canción con otra codificación).
                    item.DurationMatches = durationMatches;
                    item.IsMarkedForDeletion = item.SameSize || durationMatches;
                }

                remaining.Add(item);
            }

            if (remaining.Count == 0) return null;

            if (remaining.Count != group.Duplicates.Count)
            {
                // Se quitaron miembros: recalcula el total de copias restantes.
                foreach (var item in remaining)
                    item.RepeatCount = remaining.Count + 1;
                group.Duplicates = remaining;
            }
            return group;
        }

        private static bool WithinTolerance(double a, double b)
        {
            double max = Math.Max(a, b);
            if (max < MinRelevantSeconds) return true;
            return Math.Abs(a - b) / max <= DurationTolerance;
        }

        /// <summary>
        /// Compara duraciones con la tolerancia estricta de "nombre contenido"
        /// (misma canción = misma duración, con pequeñas variaciones de codificación).
        /// </summary>
        private static bool SubsetDurationsMatch(double a, double b)
        {
            double max = Math.Max(a, b);
            if (max < MinRelevantSeconds) return true;
            return Math.Abs(a - b) / max <= SubsetMatchDurationTolerance;
        }

        /// <summary>Indica si dos duraciones difieren de forma inequívoca (otro tema).</summary>
        private static bool TooDifferent(double a, double b)
        {
            double min = Math.Min(a, b);
            return min > 0 && Math.Max(a, b) / min > SameNameMaxDurationRatio;
        }

        private static bool IsAudioPath(string path) => AudioExtensions.Contains(Path.GetExtension(path));

        private static double? GetDuration(string path, IReadOnlyDictionary<string, double> durations)
            => path.Length > 0 && durations.TryGetValue(path, out double d) ? d : null;

        private static double? ReadDuration(string path)
        {
            try
            {
                using var reader = new MediaFoundationReader(path);
                return reader.TotalTime.TotalSeconds;
            }
            catch
            {
                return null;
            }
        }
    }
}

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
    /// archivos que caen en un grupo posible.
    ///
    /// - Grupos por palabra clave: elimina los miembros cuya duración difiere
    ///   mucho de la referencia del grupo (falso positivo: canciones distintas
    ///   que comparten palabras). Si el grupo se queda sin duplicados, se
    ///   descarta.
    /// - Grupos por nombre: marca los miembros de tamaño distinto cuando su
    ///   duración coincide con la referencia (misma canción re-codificada).
    ///
    /// Los archivos que no son audio o cuya duración no se puede leer (null)
    /// no son verificables y se conservan con la lógica de detección original.
    /// </summary>
    internal static class DurationVerifier
    {
        /// <summary>Tolerancia relativa máxima de duración entre copias de un mismo tema.</summary>
        public const double DurationTolerance = 0.30;

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
            bool isKeyword = group.Duplicates.Count > 0 &&
                group.Duplicates[0].MatchKind == DuplicateMatchKind.ProbableByKeyword;

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
                else
                {
                    // Por nombre: se marca si comparte tamaño O si la duración
                    // coincide (misma canción con otra codificación).
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.BatchRename
{
    /// <summary>
    /// Proveedor local de pruebas (gratuito, sin red ni API Key).
    /// Deriva variantes de los patrones actuales y tokens recurrentes de los
    /// nombres de archivos afectados. Útil para probar el flujo sin Groq.
    /// Cuando exista la API real basta con cambiar el proveedor en la UI.
    /// </summary>
    public class MockPatternSuggester : IPatternSuggestionProvider
    {
        private static readonly string[] Separators = [" ", "-", "_", "."];

        private const int MaxSuggestions = 6;

        public Task<IReadOnlyList<PatternSuggestion>> SuggestPatternsAsync(
            IReadOnlyList<string> patterns,
            IReadOnlyList<string> fileNames,
            CancellationToken cancellationToken = default)
        {
            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string value)
            {
                value = value.Trim();
                if (value.Length < 2) return;
                if (patterns.Any(p => p.Equals(value, StringComparison.OrdinalIgnoreCase))) return;
                if (seen.Add(value)) candidates.Add(value);
            }

            foreach (var pattern in patterns)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var words = pattern.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 1)
                {
                    Add(string.Join(" ", words));
                    Add(string.Join("-", words));
                    Add(string.Join("_", words));
                    Add(string.Join(" ", words.Take(2)));
                    Add(string.Join("-", words.Take(2)));
                }
                else
                {
                    Add(words[0].ToLowerInvariant());
                }
            }

            var tokenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in fileNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var token in name.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Length >= 3)
                        tokenCounts[token] = tokenCounts.TryGetValue(token, out var c) ? c + 1 : 1;
                }
            }
            foreach (var kv in tokenCounts
                .Where(kv => kv.Value >= 2)
                .OrderByDescending(kv => kv.Value)
                .Take(4))
            {
                Add(kv.Key);
            }

            var suggestions = candidates
                .Take(MaxSuggestions)
                .Select(c => new PatternSuggestion
                {
                    Text = c,
                    Matches = fileNames.Count(f =>
                        f.Contains(c, StringComparison.OrdinalIgnoreCase))
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<PatternSuggestion>>(suggestions);
        }
    }
}

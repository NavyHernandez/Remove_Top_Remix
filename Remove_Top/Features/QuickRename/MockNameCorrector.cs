using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>
    /// Proveedor local de pruebas (gratuito, sin red ni API Key).
    /// Simula la corrección de nombres con heurísticas deterministas:
    ///   - Separa palabras unidas (camelCase, snake_case, guiones, puntos).
    ///   - Corrige espacios múltiples.
    ///   - Pone la primera letra de cada palabra en mayúscula.
    ///   - Conserva la extensión del archivo.
    /// Cuando exista la API real basta con cambiar el proveedor en la UI.
    /// </summary>
    public class MockNameCorrector : INameCorrectionProvider
    {
        private static readonly string[] Separators = [" ", "-", "_", "."];

        public Task<IReadOnlyList<CorrectionSuggestion>> CorrectNamesAsync(
            IReadOnlyList<string> fileNames,
            CancellationToken cancellationToken = default)
        {
            var suggestions = new List<CorrectionSuggestion>(fileNames.Count);
            foreach (var fileName in fileNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                suggestions.Add(new CorrectionSuggestion
                {
                    OriginalFull = fileName,
                    SuggestedFull = CorrectName(fileName)
                });
            }
            return Task.FromResult<IReadOnlyList<CorrectionSuggestion>>(suggestions);
        }

        private static string CorrectName(string fullName)
        {
            int dot = fullName.LastIndexOf('.');
            string name = dot > 0 ? fullName[..dot] : fullName;
            string ext = dot > 0 ? fullName[dot..] : "";

            string separated = Regex.Replace(
                name, "([a-z0-9])([A-Z])", "$1 $2");

            var words = separated
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(Capitalize)
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .ToArray();

            string joined = string.Join(" ", words);
            joined = Regex.Replace(joined, @"\s+", " ").Trim();

            return string.IsNullOrEmpty(joined) ? fullName : joined + ext;
        }

        private static string Capitalize(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;
            return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        }
    }
}

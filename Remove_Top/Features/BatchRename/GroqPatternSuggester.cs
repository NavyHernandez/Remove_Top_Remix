using Remove_Top.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.BatchRename
{
    /// <summary>
    /// Proveedor real mediante el servidor Topremix (compatible con OpenAI).
    /// Envía los patrones actuales + los primeros 10 nombres de archivos afectados
    /// (ligeros: sin extensión) y pide NUEVOS patrones a eliminar.
    ///
    /// NOTA: La conexión (endpoint/modelo/apiKey) se configura en
    /// Helpers/TopRemixServerApiClient.cs.
    /// </summary>
    public class GroqPatternSuggester : IPatternSuggestionProvider
    {
        private const int MaxFileNames = 10;
        private const int MaxFileNameLength = 80;
        private const int MaxSuggestions = 10;

        public async Task<IReadOnlyList<PatternSuggestion>> SuggestPatternsAsync(
            IReadOnlyList<string> patterns,
            IReadOnlyList<string> fileNames,
            CancellationToken cancellationToken = default)
        {
            var names = fileNames
                .Select(f => f.Length <= MaxFileNameLength ? f : f[..MaxFileNameLength])
                .Take(MaxFileNames)
                .ToArray();

            var system = new StringBuilder()
                .AppendLine("Eres un experto en organización y limpieza de bibliotecas musicales.")
                .AppendLine("Recibes un JSON con los patrones actuales que se eliminan de los")
                .AppendLine("nombres de archivos y una lista de nombres de archivos (sin extensión).")
                .AppendLine("Sugiere NUEVOS patrones de texto a eliminar para limpiar más archivos.")
                .AppendLine("Considera variantes con separadores (espacio, guión, guión bajo),")
                .AppendLine("tildes, mayúsculas/minúsculas y prefijos comunes")
                .AppendLine("(ej: Prod, Producer, Official, Video, Tio, Letra...).")
                .AppendLine("NO repitas los patrones actuales.")
                .AppendLine($"Devuelve como máximo {MaxSuggestions} patrones, ordenados por utilidad.")
                .AppendLine("Responde SOLO con un array JSON de strings, sin texto adicional,")
                .AppendLine("sin comillas envolventes, sin markdown.")
                .ToString();

            var user = JsonSerializer.Serialize(new
            {
                patrones = patterns,
                archivos = names
            });

            var content = await TopRemixServerApiClient.CompleteAsync(system, user, cancellationToken: cancellationToken);
            var raw = TopRemixServerApiClient.ParseStringArray(content, maxItems: MaxSuggestions);

            var suggestions = new List<PatternSuggestion>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in raw)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (patterns.Any(p => p.Equals(item, StringComparison.OrdinalIgnoreCase))) continue;
                if (!seen.Add(item)) continue;

                suggestions.Add(new PatternSuggestion
                {
                    Text = item.Trim(),
                    Matches = fileNames.Count(f =>
                        f.Contains(item, StringComparison.OrdinalIgnoreCase))
                });
            }
            return suggestions;
        }
    }
}

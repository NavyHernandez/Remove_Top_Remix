using Remove_Top.Helpers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>
    /// Proveedor real mediante la API de Groq (OpenAI-compatible).
    /// Envía la lista de nombres y un prompt en español pidiendo que cada
    /// palabra esté escrita completa y correcta, conservando la extensión.
    ///
    /// NOTA: Queda deshabilitado en la UI hasta que el usuario proporcione
    /// su API Key. Mientras tanto se usa MockNameCorrector para pruebas.
    /// </summary>
    public class GroqNameCorrector : INameCorrectionProvider
    {
        private readonly string _apiKey;

        public GroqNameCorrector(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<IReadOnlyList<CorrectionSuggestion>> CorrectNamesAsync(
            IReadOnlyList<string> fileNames,
            CancellationToken cancellationToken = default)
        {
            var system = new StringBuilder()
                .AppendLine("Eres un asistente que corrige nombres de archivos de audio.")
                .AppendLine("Recibes una lista JSON de nombres de archivo.")
                .AppendLine("Para cada nombre, corrige la ortografía, separa palabras unidas")
                .AppendLine("y deja cada palabra escrita completa y correcta, con mayúsculas iniciales.")
                .AppendLine("NO cambies la extensión del archivo.")
                .AppendLine("Responde SOLO con un array JSON de nombres corregidos en el mismo orden,")
                .AppendLine("sin texto adicional, sin comillas envolventes, sin markdown.")
                .ToString();

            var content = await GroqApiClient.CompleteAsync(
                system,
                JsonSerializer.Serialize(fileNames),
                _apiKey,
                cancellationToken);

            var corrected = GroqApiClient.ParseStringArray(
                content, arrayProperty: "corregidos", maxItems: fileNames.Count);

            var suggestions = new List<CorrectionSuggestion>(fileNames.Count);
            for (int i = 0; i < fileNames.Count; i++)
            {
                suggestions.Add(new CorrectionSuggestion
                {
                    OriginalFull = fileNames[i],
                    SuggestedFull = i < corrected.Length ? corrected[i] : fileNames[i]
                });
            }
            return suggestions;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Remove_Top.Helpers
{
    /// <summary>
    /// Cliente HTTP compartido para el servidor Topremix (compatible con OpenAI).
    /// Centraliza endpoint, modelo, autenticación y parseo de respuestas para
    /// que las features (QuickRename, BatchRename) no dupliquen el plumbing.
    /// </summary>
    public static class TopRemixServerApiClient
    {
        // ================================================================
        // CONFIGURACIÓN DE CONEXIÓN AL SERVIDOR TOPREMIX
        // ================================================================
        // 1) ENDPOINT: URL del servidor Topremix (placeholder).
        //    Cambia esto por la URL real de tu servidor.
        // 2) MODEL: modelo del servidor (placeholder).
        //    Cambia esto por el modelo real que uses.
        // 3) API KEY: constante interna. ¡No la subas a git!
        // ================================================================
        private const string Endpoint = "https://api.topremix.example/v1/chat/completions";
        private const string Model = "topremix-model";
        private const string ApiKey = "TU_API_KEY_TOPREMIX_AQUI";
        // ================================================================

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        /// <summary>
        /// Envía un chat de una sola vuelta (system + user) y devuelve el texto
        /// crudo del campo "content" de la respuesta. Si <paramref name="apiKey"/>
        /// es null, usa la constante interna definida en esta clase.
        /// </summary>
        public static async Task<string> CompleteAsync(
            string systemPrompt,
            string userContent,
            string? apiKey = null,
            CancellationToken cancellationToken = default,
            double temperature = 0.2)
        {
            var key = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : ApiKey;
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Se requiere una API Key para el servidor Topremix.");

            var body = new
            {
                model = Model,
                temperature,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"TopRemix Server error {(int)response.StatusCode}: {errBody}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractContent(json);
        }

        /// <summary>
        /// Extrae un array de strings del "content" de la respuesta del servidor.
        /// El content puede venir como JSON plano o envuelto en ```json ... ```,
        /// y como array directo o como objeto con una propiedad de tipo array.
        /// Devuelve un array vacío si no puede interpretarlo.
        /// </summary>
        public static string[] ParseStringArray(
            string content,
            string? arrayProperty = null,
            int maxItems = 100)
        {
            try
            {
                content = content.Trim();
                if (content.StartsWith("```", StringComparison.Ordinal))
                {
                    int first = content.IndexOf('\n');
                    int last = content.LastIndexOf("```", StringComparison.Ordinal);
                    if (first >= 0 && last > first)
                        content = content[(first + 1)..last].Trim();
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                JsonElement array = default;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    array = root;
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (arrayProperty != null && root.TryGetProperty(arrayProperty, out var prop))
                        array = prop;
                    else
                    {
                        foreach (var p in root.EnumerateObject())
                        {
                            if (p.Value.ValueKind == JsonValueKind.Array)
                            {
                                array = p.Value;
                                break;
                            }
                        }
                    }
                }

                if (array.ValueKind != JsonValueKind.Array) return [];

                var result = new List<string>();
                foreach (var item in array.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        result.Add(s.Trim());
                    if (result.Count >= maxItems) break;
                }
                return result.ToArray();
            }
            catch
            {
                return [];
            }
        }

        private static string ExtractContent(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
        }
    }
}

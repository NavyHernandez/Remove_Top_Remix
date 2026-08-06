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
    /// Cliente HTTP compartido para la API de Groq (OpenAI-compatible).
    /// Centraliza endpoint, modelo, autenticación y parseo de respuestas para
    /// que las features (QuickRename, BatchRename) no dupliquen el plumbing.
    /// </summary>
    public static class GroqApiClient
    {
        // ================================================================
        // CONFIGURACIÓN DE CONEXIÓN A GROQ
        // ================================================================
        // 1) API KEY: se ingresa en la UI (PasswordBox) por sesión; no se persiste.
        //    Si prefieres fijarla aquí para pruebas, reemplaza el parámetro apiKey
        //    de los métodos por una constante local (¡no la subas a git!).
        // 2) Si cambias de proveedor (otro endpoint compatible con OpenAI),
        //    ajusta Endpoint y Model aquí. No hace falta tocar las features.
        private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
        private const string Model = "llama-3.3-70b-versatile";
        // ================================================================

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        /// <summary>
        /// Envía un chat de una sola vuelta (system + user) y devuelve el texto
        /// crudo del campo "content" de la respuesta. Lanza si falta la API Key
        /// o si la API responde con error.
        /// </summary>
        public static async Task<string> CompleteAsync(
            string systemPrompt,
            string userContent,
            string apiKey,
            CancellationToken cancellationToken = default,
            double temperature = 0.2)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Se requiere una API Key de Groq.");

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
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Groq API error {(int)response.StatusCode}: {errBody}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractContent(json);
        }

        /// <summary>
        /// Extrae un array de strings del "content" de la respuesta de Groq.
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

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Remove_Top.Features.Account
{
    /// <summary>
    /// Excepción de los servicios REST de Firebase con un mensaje ya traducido
    /// a un texto amigable para el usuario (nunca expone la respuesta cruda).
    /// </summary>
    public class FirebaseApiException : Exception
    {
        public FirebaseApiException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Cliente REST mínimo de Firebase usado por la feature Cuenta, sin dependencias
    /// nuevas (System.Text.Json + HttpClient del framework):
    ///
    ///   1. <see cref="SendVerificationEmailAsync"/> — envía el correo de
    ///      verificación vía Identity Toolkit REST (<c>accounts:sendOobCode</c>).
    ///      El paquete FirebaseAuthentication.net v4 no expone este método, así
    ///      que se invoca la API REST directamente con el idToken del usuario.
    ///
    ///   2. <see cref="AddSuggestionAsync"/> — guarda una sugerencia en Cloud
    ///      Firestore vía REST (<c>projects.databases.documents.createDocument</c>),
    ///      autenticando con el Firebase ID token (<c>Authorization: Bearer</c>).
    ///      Firestore evalúa la petición contra sus Security Rules (deben permitir
    ///      <c>create</c> a usuarios autenticados y con correo verificado).
    ///
    /// Se eligió la REST API en vez de Google.Cloud.Firestore para no arrastrar
    /// la pila gRPC (Grpc.Core) a la app; el idToken que ya produce
    /// <c>AuthService</c> es suficiente para ambos endpoints.
    /// </summary>
    public static class FirebaseRestApi
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>Envía el correo de verificación de correo al usuario autenticado.</summary>
        /// <param name="idToken">IdToken de Firebase del usuario (de <c>GetIdTokenAsync</c>).</param>
        /// <exception cref="FirebaseApiException">Si Firebase rechaza la petición.</exception>
        public static async Task SendVerificationEmailAsync(string idToken)
        {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseConfig.ApiKey}";
            await PostAsync(url, bearerToken: null, new { requestType = "VERIFY_EMAIL", idToken });
        }

        /// <summary>
        /// Consulta el estado real de verificación del correo del usuario
        /// (<c>accounts:getAccountInfo</c>). Se usa para el auto-login tras
        /// confirmar el correo en el navegador. Nunca lanza: devuelve false si
        /// no se puede determinar.
        /// </summary>
        /// <param name="idToken">IdToken fresco de Firebase del usuario.</param>
        public static async Task<bool> IsEmailVerifiedAsync(string idToken)
        {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:getAccountInfo?key={FirebaseConfig.ApiKey}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(JsonSerializer.Serialize(new { idToken }), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("users", out var users) &&
                    users.GetArrayLength() > 0 &&
                    users[0].TryGetProperty("emailVerified", out var emailVerified))
                {
                    return emailVerified.GetBoolean();
                }
            }
            catch (Exception)
            {
                // Respuesta inesperada: se interpreta como "no verificado".
            }

            return false;
        }

        /// <summary>
        /// Crea un documento nuevo en la colección de sugerencias de Firestore
        /// (ID asignado por el servicio). Solo se llama con el usuario logueado.
        /// </summary>
        /// <param name="uid">Identificador Firebase del usuario que sugiere.</param>
        /// <param name="email">Correo del usuario (para responderle).</param>
        /// <param name="message">Texto de la sugerencia.</param>
        /// <param name="idToken">IdToken de Firebase para autorizar la escritura.</param>
        /// <exception cref="FirebaseApiException">Si Firestore rechaza la petición (reglas, sesión, etc.).</exception>
        public static async Task AddSuggestionAsync(string uid, string email, string message, string idToken)
        {
            var url = $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/{FirebaseConfig.SuggestionsCollection}";

            var body = new JsonObject
            {
                ["fields"] = new JsonObject
                {
                    ["uid"] = Field("stringValue", uid),
                    ["email"] = Field("stringValue", email),
                    ["message"] = Field("stringValue", message),
                    ["createdAt"] = Field("timestampValue", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
                }
            };

            await PostAsync(url, bearerToken: idToken, body);
        }

        /// <summary>
        /// Envía un POST JSON a Firebase y lanza <see cref="FirebaseApiException"/>
        /// con mensaje amigable si la respuesta no es exitosa.
        /// </summary>
        private static async Task PostAsync(string url, string? bearerToken, object? body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrEmpty(bearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            if (body != null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            using var response = await Http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw FromError(content, response.StatusCode);
            }
        }

        /// <summary>Construye un campo de Firestore del tipo y valor indicados.</summary>
        private static JsonObject Field(string type, string value) => new() { [type] = value };

        /// <summary>
        /// Parsea el cuerpo de error de Firebase y lo traduce a un mensaje amigable.
        /// Prioriza el código de error (<c>error.message</c>), luego el estado HTTP.
        /// </summary>
        private static FirebaseApiException FromError(string responseBody, HttpStatusCode status)
        {
            string code = "";
            string errorStatus = "";

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.TryGetProperty("message", out var message))
                        code = message.GetString() ?? "";
                    if (error.TryGetProperty("status", out var st))
                        errorStatus = st.GetString() ?? "";
                }
            }
            catch
            {
                // Respuesta no JSON: se cae al fallback por código HTTP.
            }

            var messageText = MapErrorCode(code)
                ?? MapErrorStatus(errorStatus)
                ?? MapHttpStatus(status)
                ?? "No se pudo completar la operación. Inténtalo de nuevo.";

            return new FirebaseApiException(messageText);
        }

        private static string? MapErrorCode(string code) => code switch
        {
            "INVALID_ID_TOKEN" => "Tu sesión expiró. Vuelve a iniciar sesión.",
            "TOO_MANY_ATTEMPTS_TRY_LATER" => "Demasiados intentos. Espera unos minutos y reintenta.",
            "EMAIL_NOT_FOUND" or "USER_NOT_FOUND" => "No existe una cuenta con este correo.",
            "INVALID_EMAIL" => "El correo no tiene un formato válido.",
            "OPERATION_NOT_ALLOWED" => "El proveedor de correo no está habilitado en Firebase.",
            "INTERNAL" => "Error interno de Firebase. Inténtalo más tarde.",
            "UNAVAILABLE" => "El servicio no está disponible. Inténtalo más tarde.",
            _ => null
        };

        private static string? MapErrorStatus(string status) => status switch
        {
            "UNAUTHENTICATED" => "Tu sesión expiró. Vuelve a iniciar sesión.",
            "PERMISSION_DENIED" => "Sin permisos para guardar. Revisa las reglas de seguridad de Firestore.",
            "NOT_FOUND" => "La base de datos Firestore no está creada.",
            "UNAVAILABLE" => "El servicio no está disponible. Inténtalo más tarde.",
            _ => null
        };

        private static string? MapHttpStatus(HttpStatusCode status) => status switch
        {
            HttpStatusCode.Unauthorized => "Tu sesión expiró. Vuelve a iniciar sesión.",
            HttpStatusCode.Forbidden => "Sin permisos para realizar esta acción.",
            HttpStatusCode.NotFound => "El recurso no se encontró (¿base de datos Firestore creada?).",
            _ => null
        };
    }
}
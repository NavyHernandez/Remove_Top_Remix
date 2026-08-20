using System;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Auth.Providers;

namespace Remove_Top.Features.Account
{
    /// <summary>Datos básicos del perfil del usuario autenticado.</summary>
    public class UserProfile
    {
        /// <summary>Identificador único del usuario en Firebase (Uid).</summary>
        public string Uid { get; set; } = "";

        /// <summary>Nombre para mostrar (del registro o vacío).</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Correo electrónico de la cuenta.</summary>
        public string Email { get; set; } = "";

        /// <summary>URL del avatar (si el proveedor la entrega).</summary>
        public string AvatarUrl { get; set; } = "";
    }

    /// <summary>Resultado del intento de inicio de sesión.</summary>
    public class LoginResult
    {
        /// <summary>Perfil del usuario si la sesión quedó iniciada (null si no).</summary>
        public UserProfile? User { get; set; }

        /// <summary>
        /// True cuando las credenciales son correctas pero el correo aún no está
        /// verificado: la sesión NO se abre (solo se admiten cuentas verificadas).
        /// </summary>
        public bool RequiresEmailVerification { get; set; }

        /// <summary>
        /// True cuando la cuenta no existía y fue creada automáticamente en este
        /// intento (la sesión NO se abre: se requiere confirmar el correo).
        /// </summary>
        public bool AccountCreated { get; set; }

        /// <summary>
        /// True si se envió el correo de verificación (puede fallar por red).
        /// Aplica a <see cref="AccountCreated"/> y a
        /// <see cref="RegisterResult.EmailVerificationSent"/>.
        /// </summary>
        public bool EmailVerificationSent { get; set; }
    }

    /// <summary>Resultado de la creación de una cuenta nueva.</summary>
    public class RegisterResult
    {
        /// <summary>True si se envió el correo de verificación (puede fallar por red).</summary>
        public bool EmailVerificationSent { get; set; }
    }

    /// <summary>
    /// Servicio de autenticación de la aplicación con Firebase Authentication
    /// (proveedor Email/Password), usando el paquete <c>FirebaseAuthentication.net</c>.
    ///
    /// Flujos expuestos:
    ///   - <see cref="LoginAsync"/>    → <c>SignInWithEmailAndPasswordAsync</c>
    ///   - <see cref="LoginOrRegisterAsync"/> → login o auto-creación de cuenta en primer uso
    ///   - <see cref="RegisterAsync"/> → <c>CreateUserWithEmailAndPasswordAsync</c> + verificación
    ///   - <see cref="SendVerificationEmailAsync"/> → reenvío del enlace de verificación
    ///   - <see cref="SubmitSuggestionAsync"/> → guarda una sugerencia en Firestore
    ///   - <see cref="SignOut"/>       → cierra sesión y borra el token guardado.
    ///
    /// Verificación de correo (gate de acceso): solo se admiten usuarios con
    /// correo verificado. En <see cref="LoginAsync"/> y en la restauración de
    /// sesión se comprueba <c>IsEmailVerified</c>; si el correo no está verificado
    /// la sesión NO se abre (se devuelve
    /// <see cref="LoginResult.RequiresEmailVerification"/>), pero el refresh token
    /// se conserva como "pendiente": así <see cref="CompleteVerificationAsync"/>
    /// puede detectar la confirmación del correo y hacer el auto-login sin volver
    /// a pedir la contraseña. El correo de verificación se envía con la REST API
    /// de Firebase Identity Toolkit (<see cref="FirebaseRestApi.SendVerificationEmailAsync"/>),
    /// ya que el paquete v4 no expone ese método.
    ///
    /// Persistencia: la sesión se restaura automáticamente al primer acceso.
    /// <see cref="FirebaseAuthClient"/> lee el <c>SecureUserRepository</c>
    /// (Windows Credential Locker) en su constructor; si hay un refresh token
    /// guardado, rehidrata <c>User</c> y <see cref="CurrentUser"/> se puebla solo.
    ///
    /// Seguridad: la contraseña nunca se guarda; solo se persiste el refresh token
    /// cifrado por el SO. Los errores se traducen a mensajes amigables con
    /// <see cref="GetAuthErrorMessage"/> (nunca se muestra la respuesta cruda).
    /// </summary>
    public class AuthService
    {
        /// <summary>Instancia única del servicio.</summary>
        public static AuthService Instance { get; } = new();

        private FirebaseAuthClient? _client;

        /// <summary>Usuario con sesión activa (null si no hay sesión).</summary>
        public UserProfile? CurrentUser { get; private set; }

        /// <summary>True si el usuario tiene una sesión iniciada.</summary>
        public bool IsLoggedIn => this.CurrentUser != null;

        /// <summary>
        /// True si hay un usuario "pendiente de verificación": refresh token
        /// conservado pero sin sesión activa (el correo aún no se confirmó).
        /// Se usa para reactivar el auto-login al volver a la página Cuenta.
        /// </summary>
        public bool HasPendingVerification => this.Client.User != null && this.CurrentUser == null;

        /// <summary>
        /// Cliente Firebase configurado con el proveedor Email/Password y el
        /// repositorio seguro. Se crea una sola vez (lazy) y al crearlo restaura
        /// la sesión guardada si existe (y está verificada).
        /// </summary>
        private FirebaseAuthClient Client
        {
            get
            {
                if (this._client == null)
                {
                    this._client = new FirebaseAuthClient(new FirebaseAuthConfig
                    {
                        ApiKey = FirebaseConfig.ApiKey,
                        AuthDomain = FirebaseConfig.AuthDomain,
                        Providers = new FirebaseAuthProvider[] { new EmailProvider() },
                        UserRepository = new SecureUserRepository()
                    });

                    this.RestoreSession();
                }

                return this._client;
            }
        }

        /// <summary>
        /// Inicia sesión con correo y contraseña. Solo se abre la sesión si el
        /// correo está verificado; en caso contrario devuelve
        /// <see cref="LoginResult.RequiresEmailVerification"/> y conserva el
        /// refresh token como "pendiente" para el auto-login.
        /// </summary>
        /// <exception cref="FirebaseAuthException">Credenciales inválidas u otro error de autenticación.</exception>
        public async Task<LoginResult> LoginAsync(string email, string password)
        {
            var credential = await this.Client.SignInWithEmailAndPasswordAsync(email, password).ConfigureAwait(false);
            var user = credential.User;

            if (!(user.Info?.IsEmailVerified ?? false))
            {
                // Credenciales correctas pero correo sin verificar: NO se abre la
                // sesión. El refresh token se conserva como "pendiente" para poder
                // hacer el auto-login cuando el usuario confirme el correo.
                return new LoginResult { RequiresEmailVerification = true };
            }

            this.ApplyUser(user);
            return new LoginResult { User = this.CurrentUser };
        }

        /// <summary>
        /// Inicia sesión o, si la cuenta aún no existe, la crea automáticamente y
        /// envía el correo de verificación. Experiencia de "primer uso": el usuario
        /// escribe sus credenciales en el formulario de login y la cuenta se crea
        /// sin pasar por el modo explícito de registro.
        ///
        /// Google usa el MISMO código (<c>INVALID_LOGIN_CREDENTIALS</c>) para correo
        /// inexistente y contraseña incorrecta, así que el flujo es:
        ///   1. Intentar <see cref="LoginAsync"/>.
        ///   2. Si falla por credenciales → intentar <see cref="RegisterAsync"/>.
        ///      - Si el registro funciona → <see cref="LoginResult.AccountCreated"/>.
        ///      - Si el registro falla con <c>EMAIL_EXISTS</c>, la cuenta YA existía
        ///        y la contraseña era incorrecta → se relanza el error de login.
        ///   3. Cualquier otro error (red, etc.) se propaga sin crear nada.
        /// </summary>
        /// <exception cref="FirebaseAuthException">Errores de autenticación (incluido credenciales inválidas).</exception>
        public async Task<LoginResult> LoginOrRegisterAsync(string email, string password, string? displayName = null)
        {
            // 1) Intento de inicio de sesión normal.
            Exception? loginError = null;
            try
            {
                var login = await LoginAsync(email, password).ConfigureAwait(false);
                if (login.User != null || login.RequiresEmailVerification)
                {
                    return login;
                }
            }
            catch (Exception ex)
            {
                loginError = ex;
            }

            // 2) Sin resultado válido ni error de credenciales: se propaga.
            if (loginError == null || !IsUnknownCredentials(loginError))
            {
                if (loginError != null)
                {
                    throw loginError;
                }

                throw new InvalidOperationException("No se pudo iniciar sesión.");
            }

            // 3) La cuenta no existe (o la contraseña no coincide con una existente):
            //    se intenta crear automáticamente y enviar el correo de verificación.
            try
            {
                var register = await RegisterAsync(email, password, displayName).ConfigureAwait(false);
                return new LoginResult
                {
                    AccountCreated = true,
                    EmailVerificationSent = register.EmailVerificationSent
                };
            }
            catch (Exception registerEx) when (IsEmailExists(registerEx))
            {
                // La cuenta ya existe → la contraseña era incorrecta.
                throw loginError;
            }
        }

        /// <summary>
        /// Crea una cuenta nueva con correo, contraseña y (opcional) nombre para
        /// mostrar, y envía el correo de verificación. La cuenta NO queda con
        /// sesión: el usuario debe verificar su correo antes de iniciar sesión.
        /// </summary>
        /// <exception cref="FirebaseAuthException">Correo en uso, contraseña débil u otro error.</exception>
        public async Task<RegisterResult> RegisterAsync(string email, string password, string? displayName)
        {
            var credential = await this.Client
                .CreateUserWithEmailAndPasswordAsync(email, password, string.IsNullOrWhiteSpace(displayName) ? null : displayName)
                .ConfigureAwait(false);

            var sent = false;
            try
            {
                var idToken = await credential.User.GetIdTokenAsync().ConfigureAwait(false);
                await FirebaseRestApi.SendVerificationEmailAsync(idToken).ConfigureAwait(false);
                sent = true;
            }
            catch (Exception)
            {
                // La cuenta se creó igual; el envío del enlace se reintenta desde
                // el formulario de login (botón "Reenviar enlace de verificación").
                sent = false;
            }

            // La cuenta NO queda con sesión activa, pero el refresh token se
            // conserva como "pendiente" para que la app detecte la confirmación
            // del correo y haga el auto-login sin volver a pedir la contraseña.
            return new RegisterResult { EmailVerificationSent = sent };
        }

        /// <summary>
        /// Reenvía el correo de verificación de una cuenta sin verificar.
        /// Re-autentica (para obtener un idToken fresco), envía el enlace y
        /// vuelve a cerrar la sesión, sin dejar al usuario dentro.
        /// </summary>
        /// <exception cref="FirebaseAuthException">Credenciales inválidas.</exception>
        public async Task SendVerificationEmailAsync(string email, string password)
        {
            var credential = await this.Client.SignInWithEmailAndPasswordAsync(email, password).ConfigureAwait(false);

            // Re-autentica para obtener un idToken fresco y envía el enlace.
            // El refresh token se conserva (pendiente) para el auto-login.
            var idToken = await credential.User.GetIdTokenAsync().ConfigureAwait(false);
            await FirebaseRestApi.SendVerificationEmailAsync(idToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Guarda una sugerencia del usuario logueado en Firestore. Requiere
        /// sesión activa (y, por tanto, correo verificado).
        /// </summary>
        /// <exception cref="FirebaseApiException">Firestore rechaza la escritura.</exception>
        public async Task SubmitSuggestionAsync(string message)
        {
            var user = this.CurrentUser
                ?? throw new InvalidOperationException("Debes iniciar sesión para enviar sugerencias.");

            var idToken = await this.Client.User.GetIdTokenAsync().ConfigureAwait(false);
            await FirebaseRestApi.AddSuggestionAsync(user.Uid, user.Email, message, idToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Comprueba en el servidor si el correo del usuario pendiente ya está
        /// verificado y, si lo está, inicia la sesión automáticamente (auto-login).
        /// Usa el refresh token conservado, así que no requiere la contraseña de
        /// nuevo. Devuelve true si la sesión quedó iniciada.
        /// </summary>
        public async Task<bool> CompleteVerificationAsync()
        {
            if (this.CurrentUser != null)
            {
                return true;
            }

            var user = this.Client.User;
            if (user == null)
            {
                return false;
            }

            try
            {
                // 1) idToken fresco usando el refresh token guardado (sin contraseña).
                var freshIdToken = await user.GetIdTokenAsync(true).ConfigureAwait(false);

                // 2) Estado real del correo en el servidor (getAccountInfo).
                if (!await FirebaseRestApi.IsEmailVerifiedAsync(freshIdToken).ConfigureAwait(false))
                {
                    return false;
                }

                // 3) Marcar la info local como verificada y persistirla
                //    (GetIdTokenAsync llama internamente a UserManager, que guarda
                //    la Info en el repositorio seguro para futuros arranques).
                user.Info.IsEmailVerified = true;
                await user.GetIdTokenAsync(true).ConfigureAwait(false);

                this.ApplyUser(user);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Cierra la sesión del usuario actual y borra el token guardado.</summary>
        public void SignOut()
        {
            this.Client.SignOut();
            this.CurrentUser = null;
        }

        /// <summary>Mapea el <see cref="User"/> de Firebase al perfil de la app.</summary>
        private UserProfile? ApplyUser(User user)
        {
            this.CurrentUser = new UserProfile
            {
                Uid = user.Uid,
                DisplayName = user.Info?.DisplayName ?? "",
                Email = user.Info?.Email ?? "",
                AvatarUrl = user.Info?.PhotoUrl ?? ""
            };
            return this.CurrentUser;
        }

        /// <summary>
        /// Rehidrata <see cref="CurrentUser"/> desde el usuario restaurado por el
        /// cliente Firebase (leído del Credential Locker en el constructor).
        /// Solo restaura sesiones de cuentas con correo verificado.
        /// </summary>
        private void RestoreSession()
        {
            var user = this._client!.User;
            if (user != null)
            {
                if (user.Info?.IsEmailVerified ?? false)
                {
                    this.ApplyUser(user);
                }
                // Si el correo aún no está verificado, el refresh token se
                // conserva como "pendiente" (sin sesión activa): permitirá el
                // auto-login cuando el usuario confirme el correo en el navegador.
            }
        }

        /// <summary>
        /// Traduce una excepción de autenticación/Firebase a un mensaje amigable
        /// en español. Nunca expone la respuesta cruda del servidor.
        ///
        /// Detalle importante: el SDK v4 empaqueta CUALQUIER fallo HTTP en
        /// <see cref="FirebaseAuthHttpException"/>. Google devuelve el mismo
        /// código <c>INVALID_LOGIN_CREDENTIALS</c> para correo inexistente y para
        /// contraseña incorrecta (para no enumerar cuentas), y el parser del SDK
        /// no lo reconoce → Reason = Unknown. Por eso aquí se lee el cuerpo JSON
        /// (<c>ResponseData</c>) directamente y se distingue un error de credenciales
        /// de un fallo de red real (cuerpo vacío).
        /// </summary>
        public static string GetAuthErrorMessage(Exception ex)
        {
            if (ex is FirebaseApiException apiEx)
            {
                return apiEx.Message;
            }

            if (ex is FirebaseAuthHttpException httpEx)
            {
                var mapped = MapHttpErrorMessage(httpEx.ResponseData);
                if (mapped != null)
                {
                    return mapped;
                }

                // Respuesta con cuerpo pero sin mapear: se registra el detalle para diagnóstico.
                if (!string.IsNullOrWhiteSpace(httpEx.ResponseData))
                {
                    App.Log("Auth",
                        $"Error HTTP no mapeado. Reason={httpEx.Reason} | Url={httpEx.RequestUrl} | " +
                        $"Response={httpEx.ResponseData} | Inner={httpEx.InnerException?.Message}");
                }

                // Sin cuerpo JSON = fallo de red real (no se alcanzó Firebase).
                return "No se pudo conectar con el servidor. Revisa tu internet e inténtalo de nuevo.";
            }

            if (ex is FirebaseAuthException firebaseEx)
            {
                return firebaseEx.Reason switch
                {
                    AuthErrorReason.EmailExists => "Ya existe una cuenta con este correo.",
                    AuthErrorReason.WeakPassword => "La contraseña debe tener al menos 6 caracteres.",
                    AuthErrorReason.WrongPassword => "Contraseña incorrecta.",
                    AuthErrorReason.UnknownEmailAddress or AuthErrorReason.UserNotFound => "No existe una cuenta con este correo.",
                    AuthErrorReason.InvalidEmailAddress => "El correo no tiene un formato válido.",
                    AuthErrorReason.MissingPassword => "Ingresa una contraseña.",
                    AuthErrorReason.MissingEmail => "Ingresa un correo.",
                    AuthErrorReason.UserDisabled => "La cuenta está deshabilitada. Contacta con soporte.",
                    AuthErrorReason.TooManyAttemptsTryLater => "Demasiados intentos fallidos. Intenta de nuevo más tarde.",
                    AuthErrorReason.InvalidApiKey => "Error de configuración de la aplicación.",
                    AuthErrorReason.OperationNotAllowed => "El inicio de sesión con correo no está habilitado.",
                    AuthErrorReason.Undefined => "No se pudo conectar con el servidor. Revisa tu internet e inténtalo de nuevo.",
                    _ => "No se pudo iniciar sesión. Inténtalo de nuevo."
                };
            }

            if (ex is InvalidOperationException)
            {
                return "No se pudo conectar con el servidor de autenticación.";
            }

            return "Ocurrió un error inesperado. Inténtalo de nuevo.";
        }

        /// <summary>
        /// Interpreta el cuerpo JSON de error de Firebase (<c>ResponseData</c>) y
        /// devuelve el mensaje amigable correspondiente, o null si no se reconoce.
        /// </summary>
        private static string? MapHttpErrorMessage(string responseData)
        {
            var code = GetErrorCode(responseData);
            if (code == null)
            {
                return null;
            }

            if (code.StartsWith("WEAK_PASSWORD", StringComparison.Ordinal))
            {
                return "La contraseña debe tener al menos 6 caracteres.";
            }

            if (code.StartsWith("TOO_MANY_ATTEMPTS_TRY_LATER", StringComparison.Ordinal))
            {
                return "Demasiados intentos fallidos. Intenta de nuevo más tarde.";
            }

            return code switch
            {
                // Google usa un solo código para correo inexistente y contraseña incorrecta.
                "INVALID_LOGIN_CREDENTIALS" => "Correo o contraseña incorrectos.",
                "INVALID_EMAIL" => "El correo no tiene un formato válido.",
                "MISSING_EMAIL" => "Ingresa un correo.",
                "MISSING_PASSWORD" => "Ingresa una contraseña.",
                "EMAIL_EXISTS" => "Ya existe una cuenta con este correo.",
                "EMAIL_NOT_FOUND" or "USER_NOT_FOUND" => "No existe una cuenta con este correo.",
                "USER_DISABLED" => "La cuenta está deshabilitada. Contacta con soporte.",
                "OPERATION_NOT_ALLOWED" => "El inicio de sesión con correo no está habilitado.",
                "CREDENTIAL_TOO_OLD_LOGIN_AGAIN" => "Por seguridad, vuelve a iniciar sesión.",
                "API key not valid. Please pass a valid API key." => "Error de configuración de la aplicación.",
                _ => null
            };
        }

        /// <summary>
        /// True si el error corresponde a credenciales inválidas (el código que
        /// Google usa tanto para correo inexistente como para contraseña incorrecta).
        /// </summary>
        private static bool IsUnknownCredentials(Exception ex)
        {
            return GetErrorCode(ex) == "INVALID_LOGIN_CREDENTIALS";
        }

        /// <summary>True si el error indica que el correo ya está registrado.</summary>
        private static bool IsEmailExists(Exception ex)
        {
            if (ex is FirebaseAuthException firebaseEx && firebaseEx.Reason == AuthErrorReason.EmailExists)
            {
                return true;
            }

            return GetErrorCode(ex) == "EMAIL_EXISTS";
        }

        /// <summary>Extrae el código de error de Firebase desde la excepción.</summary>
        private static string? GetErrorCode(Exception ex)
        {
            if (ex is FirebaseAuthHttpException httpEx)
            {
                return GetErrorCode(httpEx.ResponseData);
            }

            if (ex is FirebaseAuthException firebaseEx)
            {
                return firebaseEx.Reason switch
                {
                    AuthErrorReason.EmailExists => "EMAIL_EXISTS",
                    AuthErrorReason.WeakPassword => "WEAK_PASSWORD",
                    AuthErrorReason.InvalidEmailAddress => "INVALID_EMAIL",
                    AuthErrorReason.UserNotFound or AuthErrorReason.UnknownEmailAddress => "USER_NOT_FOUND",
                    AuthErrorReason.WrongPassword => "INVALID_LOGIN_CREDENTIALS",
                    _ => null
                };
            }

            return null;
        }

        /// <summary>Extrae el campo <c>error.message</c> del cuerpo JSON de error de Firebase.</summary>
        private static string? GetErrorCode(string responseData)
        {
            if (string.IsNullOrWhiteSpace(responseData))
            {
                return null;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(responseData);
                if (!doc.RootElement.TryGetProperty("error", out var error) ||
                    !error.TryGetProperty("message", out var messageProp))
                {
                    return null;
                }

                return messageProp.GetString();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
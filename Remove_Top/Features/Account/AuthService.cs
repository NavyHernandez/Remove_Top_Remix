using System.Threading.Tasks;

namespace Remove_Top.Features.Account
{
    /// <summary>Datos básicos del perfil del usuario (se completa con el login de Google).</summary>
    public class UserProfile
    {
        /// <summary>Nombre para mostrar.</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Correo electrónico de la cuenta de Google.</summary>
        public string Email { get; set; } = "";

        /// <summary>URL del avatar (si el proveedor la entrega).</summary>
        public string AvatarUrl { get; set; } = "";
    }

    /// <summary>
    /// Servicio de autenticación de la aplicación.
    ///
    /// ESTADO ACTUAL: solo la INTERFAZ. La página "Cuenta" construye todo el
    /// perfil (avatar, nombre, correo, botón de login) pero el flujo OAuth real
    /// con Google aún no está disponible: <see cref="LoginWithGoogleAsync"/>
    /// devuelve <c>null</c> (no sesión). La implementación real (OAuth con
    /// WebAuthenticationBroker + PKCE) está documentada en el comentario al
    /// final de la clase y se conectará cuando exista el Client ID del servidor.
    /// </summary>
    public class AuthService
    {
        /// <summary>Instancia única del servicio.</summary>
        public static AuthService Instance { get; } = new();

        /// <summary>Usuario con sesión activa (null si no hay sesión).</summary>
        public UserProfile? CurrentUser { get; private set; }

        /// <summary>True si el usuario tiene una sesión iniciada.</summary>
        public bool IsLoggedIn => CurrentUser != null;

        /// <summary>
        /// Inicia sesión con Google. Actualmente NO implementado: devuelve
        /// <c>null</c> para que la UI muestre el estado "sin sesión".
        /// </summary>
        public Task<UserProfile?> LoginWithGoogleAsync()
        {
            // TODO(próxima versión): flujo OAuth real. Ver comentario abajo.
            return Task.FromResult<UserProfile?>(null);
        }

        /// <summary>Cierra la sesión del usuario actual.</summary>
        public void SignOut() => CurrentUser = null;

        // ====================================================================
        // IMPLEMENTACIÓN REAL DEL LOGIN CON GOOGLE (PENDIENTE)
        // --------------------------------------------------------------------
        // Requiere un Client ID de Google (consola de desarrolladores) y un
        // backend Topremix que canjee el "authorization code" por tokens.
        //
        //   public async Task<UserProfile?> LoginWithGoogleAsync()
        //   {
        //       // 1) Generar verifier + challenge PKCE (RFC 7636)
        //       // 2) Construir la URL de autorización de Google:
        //       //    https://accounts.google.com/o/oauth2/v2/auth
        //       //      ?client_id=...&redirect_uri=...&response_type=code
        //       //      &scope=openid email profile&code_challenge=...&state=...
        //       // 3) WebAuthenticationBroker.AuthenticateAsync(WebAuthenticationOptions.None, uri)
        //       //    para que el usuario autorice en el navegador del sistema.
        //       // 4) Enviar el "code" al backend Topremix (/auth/google) para
        //       //    canjearlo por un token de acceso + datos del perfil.
        //       // 5) CurrentUser = new UserProfile { DisplayName, Email, AvatarUrl };
        //   }
        // ====================================================================
    }
}

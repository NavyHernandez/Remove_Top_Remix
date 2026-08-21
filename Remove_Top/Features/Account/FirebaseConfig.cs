namespace Remove_Top.Features.Account
{
    /// <summary>
    /// Configuración centralizada de Firebase Authentication (proveedor
    /// Email/Password) de la aplicación.
    ///
    /// Los valores corresponden a la app web de Firebase "top-remix-e53ca".
    /// La <see cref="ApiKey"/> es PÚBLICA por diseño (es la "Web API key" de la
    /// consola de Firebase): viaja en el cliente y solo identifica el proyecto;
    /// NO es un secreto. La seguridad real se apoya en las reglas de seguridad
    /// de Firebase y en el almacenamiento protegido del refresh token
    /// (<see cref="SecureUserRepository"/>).
    /// </summary>
    public static class FirebaseConfig
    {
        /// <summary>Web API key de la app de Firebase (pública por diseño).</summary>
        public const string ApiKey = "AIzaSyD4bnxoglAdLlAcUyGwb8Iet6LJwoAjpE8";

        /// <summary>Dominio de autenticación (authDomain) de la app de Firebase.</summary>
        public const string AuthDomain = "top-remix-e53ca.firebaseapp.com";

        /// <summary>ID del proyecto Firebase (se usa en las URLs de Firestore).</summary>
        public const string ProjectId = "top-remix-e53ca";

        /// <summary>
        /// Colección de Firestore donde se guardan las sugerencias de los usuarios.
        /// La escritura se valida con las reglas de seguridad de Firestore
        /// (deben permitir <c>create</c> para usuarios autenticados con correo
        /// verificado). Solo lectura desde la consola/backend.
        /// </summary>
        public const string SuggestionsCollection = "suggestions";
    }
}
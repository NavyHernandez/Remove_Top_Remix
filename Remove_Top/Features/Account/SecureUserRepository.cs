using System;
using Firebase.Auth;
using Firebase.Auth.Repository;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Windows.Security.Credentials;

namespace Remove_Top.Features.Account
{
    /// <summary>
    /// <see cref="IUserRepository"/> que persiste el usuario y sus credenciales
    /// de Firebase en el Windows Credential Locker (<see cref="PasswordVault"/>).
    ///
    /// Por qué un repositorio propio y no el <c>FileUserRepository</c> del paquete:
    /// este último guarda <c>firebase.json</c> en <c>%AppData%</c> en texto plano,
    /// exponiendo el refresh token a cualquier proceso del usuario. El Credential
    /// Locker cifra el contenido a nivel de SO (vinculado al usuario de Windows),
    /// que es la forma recomendada por Microsoft para guardar tokens en WinUI 3.
    ///
    /// El contenido serializado es un DTO <see cref="UserDal"/> con el perfil
    /// (<see cref="UserInfo"/>) y las credenciales (<see cref="FirebaseCredential"/>,
    /// incluido el refresh token). Se guarda en el campo "password" de un único
    /// credential con recurso fijo (<see cref="VaultResource"/>). Nunca se almacena
    /// la contraseña del usuario, solo sus tokens de sesión.
    ///
    /// Nota: en apps de escritorio "full-trust" (unpackaged) el Credential Locker
    /// no aísla por aplicación (cualquier proceso del mismo usuario podría leerlo),
    /// pero es el mismo nivel de seguridad que ofrece DPAPI y es la vía canónica
    /// documentada por Microsoft para apps de escritorio.
    /// </summary>
    public class SecureUserRepository : IUserRepository
    {
        /// <summary>Identificador del credential dentro del Locker (marca de la app).</summary>
        private const string VaultResource = "Remove_Top_FirebaseAuth";

        /// <summary>Nombre de usuario fijo: solo hay un credential por cuenta.</summary>
        private const string VaultUserName = "firebase_user";

        private readonly PasswordVault vault = new();
        private readonly JsonSerializerSettings options;

        public SecureUserRepository()
        {
            // StringEnumConverter serializa el enum ProviderType como texto
            // (más legible y estable entre versiones) en vez de número.
            this.options = new JsonSerializerSettings();
            this.options.Converters.Add(new StringEnumConverter());
        }

        /// <inheritdoc />
        public bool UserExists()
        {
            try
            {
                return this.vault.FindAllByResource(VaultResource).Count > 0;
            }
            catch (Exception)
            {
                // FindAllByResource lanza ELEMENT_NOT_FOUND cuando no hay nada;
                // no hay credencial guardada.
                return false;
            }
        }

        /// <inheritdoc />
        public (UserInfo userInfo, FirebaseCredential credential) ReadUser()
        {
            var dal = JsonConvert.DeserializeObject<UserDal>(this.RetrieveJson(), this.options);
            return (dal!.UserInfo!, dal.Credential!);
        }

        /// <inheritdoc />
        public void SaveUser(User user)
        {
            var json = JsonConvert.SerializeObject(new UserDal(user.Info, user.Credential), this.options);
            this.ReplaceCredential(json);
        }

        /// <inheritdoc />
        public void DeleteUser() => this.ReplaceCredential(null);

        /// <summary>
        /// Reemplaza el credential del Locker: si existe lo elimina y (si se
        /// indica contenido) lo vuelve a crear. PasswordVault.Add lanza si ya
        /// existe el mismo recurso+usuario, por eso siempre se elimina primero.
        /// </summary>
        private void ReplaceCredential(string? json)
        {
            try
            {
                var existing = this.vault.Retrieve(VaultResource, VaultUserName);
                this.vault.Remove(existing);
            }
            catch (Exception)
            {
                // No existía; es el caso esperado en el primer guardado.
            }

            if (json != null)
            {
                this.vault.Add(new PasswordCredential(VaultResource, VaultUserName, json));
            }
        }

        /// <summary>Lee el JSON serializado desde el Locker (recupera la contraseña diferida).</summary>
        private string RetrieveJson()
        {
            var credential = this.vault.Retrieve(VaultResource, VaultUserName);
            credential.RetrievePassword();
            return credential.Password;
        }

        /// <summary>DTO serializable con el perfil y las credenciales de la sesión.</summary>
        private class UserDal
        {
            public UserDal()
            {
            }

            public UserDal(UserInfo? userInfo, FirebaseCredential? credential)
            {
                this.UserInfo = userInfo;
                this.Credential = credential;
            }

            public UserInfo? UserInfo { get; set; }

            public FirebaseCredential? Credential { get; set; }
        }
    }
}
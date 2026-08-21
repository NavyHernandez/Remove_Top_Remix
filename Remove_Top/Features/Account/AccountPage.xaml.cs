using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Remove_Top.Helpers;
using System;
using System.Text.RegularExpressions;
using Windows.System;

namespace Remove_Top.Features.Account
{
    /// <summary>
    /// Página "Cuenta": perfil, sugerencias y actualizaciones de la aplicación.
    /// Sigue el mismo patrón del resto de funcionalidades:
    ///   - ViewModel inline en el code-behind.
    ///   - Textos del encabezado (título/subtítulo/badge) centralizados en AppLimits.
    ///   - Secciones en tarjetas (LayerFillColorDefaultBrush + borde de elevación).
    ///
    /// Estado actual:
    ///   - Perfil: autenticación real con Firebase (Email/Password). Sin sesión
    ///     muestra un formulario de login/registro; con sesión, el perfil y el
    ///     botón "Cerrar sesión". Solo se admiten cuentas con correo verificado:
    ///     al loguear/registrar se avisa al usuario y se ofrece reenviar el enlace.
    ///     Mientras la verificación está pendiente, un polling comprueba si el
    ///     correo ya se confirmó y hace el auto-login.
    ///     La sesión se restaura al iniciar la app (token en el Credential Locker).
    ///     Ver <see cref="AuthService"/>.
    ///   - Sugerencias: cuadro de feedback visible SOLO con sesión iniciada; el
    ///     envío guarda en Firestore vía <see cref="FirebaseRestApi"/>.
    ///   - Actualizaciones: en modo simulado (ver <see cref="UpdateChecker"/>).
    /// </summary>
    public sealed partial class AccountPage : Page
    {
        /// <summary>True cuando el formulario está en modo "registro" (false = login).</summary>
        private bool _isRegisterMode;

        /// <summary>Timer del auto-login: comprueba si el correo ya se verificó.</summary>
        private DispatcherTimer? _verifyPollTimer;

        /// <summary>Contador de ticks del polling de verificación.</summary>
        private int _verifyPollTicks;

        /// <summary>Cada cuántos segundos se comprueba si el correo ya se verificó.</summary>
        private const int VerifyPollIntervalSeconds = 5;

        /// <summary>Tope del polling (~10 minutos) para no consultar indefinidamente.</summary>
        private const int VerifyPollMaxTicks = 120;

        /// <summary>Validación ligera de formato de correo (previo al envío).</summary>
        private static readonly Regex EmailRegex = new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public AccountPage()
        {
            InitializeComponent();

            // Identidad de la app y textos del encabezado, centralizados en AppLimits.
            BrandText.Text = AppLimits.AppName;
            PageTitleText.Text = AppLimits.AccountPageTitle;
            PageSubtitleText.Text = AppLimits.AccountPageSubtitle;
            AppSiteText.Text = AppLimits.AppBrandSite;

            // Versión actual visible de entrada (sin necesidad de buscar actualizaciones).
            InstalledVersionText.Text = $"Versión instalada: {UpdateChecker.InstalledVersion}";

            // Cargar notas de versión desde el archivo de texto.
            LoadReleaseNotes();

            // Sección de sugerencias: textos y límite centralizados en AppLimits.
            SuggestionsTitleText.Text = AppLimits.SuggestionsTitle;
            SuggestionsSubtitleText.Text = AppLimits.SuggestionsSubtitle;
            SuggestionBox.MaxLength = AppLimits.SuggestionsMaxLength;
            UpdateSuggestionCounter();
            SendSuggestionButton.Content = UiHelpers.Content(Icon.Send, "Enviar sugerencia");

            Loaded += AccountPage_Loaded;
            Unloaded += AccountPage_Unloaded;
        }

        /// <summary>Al cargar, refresca el perfil y reactiva el auto-login si hay una verificación pendiente.</summary>
        private void AccountPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshProfile();

            if (AuthService.Instance.HasPendingVerification)
            {
                StartVerificationPoll();
            }
        }

        /// <summary>Al salir de la página, detiene el polling de verificación.</summary>
        private void AccountPage_Unloaded(object sender, RoutedEventArgs e)
        {
            StopVerificationPoll();
        }

        // ================================================================
        // PERFIL / AUTENTICACIÓN
        // ================================================================

        /// <summary>
        /// Alterna entre el perfil (sesión activa) y el formulario de
        /// login/registro (sin sesión) según el estado de <see cref="AuthService"/>.
        /// </summary>
        private void RefreshProfile()
        {
            var user = AuthService.Instance.CurrentUser;
            bool logged = AuthService.Instance.IsLoggedIn;

            LoggedInPanel.Visibility = logged ? Visibility.Visible : Visibility.Collapsed;
            LoggedOutPanel.Visibility = logged ? Visibility.Collapsed : Visibility.Visible;

            // La sección de sugerencias solo está disponible con sesión iniciada.
            SuggestionsSection.Visibility = logged ? Visibility.Visible : Visibility.Collapsed;

            if (logged)
            {
                ProfileNameText.Text = string.IsNullOrWhiteSpace(user!.DisplayName)
                    ? "Usuario One Dj App"
                    : user.DisplayName;
                ProfileEmailText.Text = user.Email;

                // Ya hay sesión: no hace falta seguir comprobando la verificación.
                StopVerificationPoll();
            }
            else
            {
                // Sin sesión: asegura el contenido por defecto del formulario.
                SetAuthMode(_isRegisterMode);
                SuggestionInfoBar.IsOpen = false;
            }
        }

        /// <summary>
        /// Configura el formulario para login o registro: muestra/oculta el campo
        /// de nombre, cambia el texto del botón y del enlace de alternancia.
        /// </summary>
        private void SetAuthMode(bool register)
        {
            _isRegisterMode = register;

            DisplayNameBox.Visibility = register ? Visibility.Visible : Visibility.Collapsed;
            AuthSubmitButton.Content = UiHelpers.Content(
                register ? Icon.PersonAdd : Icon.PersonLock,
                register ? "Crear cuenta" : "Iniciar sesión");
            AuthToggleButton.Content = register
                ? "¿Ya tienes cuenta? Iniciar sesión"
                : "¿No tienes cuenta? Crear una";
            HideAuthStates();
        }

        /// <summary>
        /// Envía el formulario: inicia sesión o crea una cuenta según
        /// <see cref="_isRegisterMode"/>. Bloquea el formulario durante la
        /// operación de red y muestra el error amigable si algo falla.
        ///
        /// Gate de verificación: si las credenciales son correctas pero el correo
        /// no está verificado, el servicio no abre la sesión y aquí se muestra el
        /// panel ámbar con el botón para reenviar el enlace.
        ///
        /// Primer uso: en modo login, si la cuenta aún no existe el servicio la
        /// crea automáticamente y envía el correo de verificación
        /// (<see cref="LoginResult.AccountCreated"/>).
        /// </summary>
        private async void AuthSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailBox.Text.Trim();
            var password = PasswordBox.Password;

            if (!IsValidEmail(email) || string.IsNullOrEmpty(password))
            {
                ShowAuthError("Ingresa un correo válido y tu contraseña.");
                return;
            }

            SetAuthBusy(true);
            try
            {
                if (_isRegisterMode)
                {
                    var register = await AuthService.Instance.RegisterAsync(email, password, DisplayNameBox.Text.Trim());
                    SetAuthMode(false);
                    PasswordBox.Password = "";
                    ShowAuthSuccess(
                        "Cuenta creada. Te enviamos un enlace de verificación",
                        register.EmailVerificationSent
                            ? "Revisa tu bandeja de entrada (y la carpeta de spam), confirma el enlace y te loguearemos automáticamente."
                            : "No se pudo enviar el correo de verificación ahora. Usa 'Iniciar sesión' y luego 'Reenviar enlace de verificación'.");
                    StartVerificationPoll();
                }
                else
                {
                    var login = await AuthService.Instance.LoginOrRegisterAsync(email, password, DisplayNameBox.Text.Trim());
                    if (login.AccountCreated)
                    {
                        PasswordBox.Password = "";
                        ShowAuthSuccess(
                            "Correo de verificación enviado",
                            login.EmailVerificationSent
                                ? "Revisa tu bandeja de entrada (y el spam), confirma el enlace y te loguearemos automáticamente."
                                : "No se pudo enviar el correo de verificación ahora. Usa 'Reenviar enlace de verificación'.");
                        StartVerificationPoll();
                    }
                    else if (login.RequiresEmailVerification)
                    {
                        ShowAuthVerifyNote();
                        StartVerificationPoll();
                    }
                    else if (login.User != null)
                    {
                        ResetAuthForm();
                        RefreshProfile();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowAuthError(AuthService.GetAuthErrorMessage(ex));
            }
            finally
            {
                SetAuthBusy(false);
            }
        }

        /// <summary>Alterna entre el modo login y el modo registro.</summary>
        private void AuthToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SetAuthMode(!_isRegisterMode);
        }

        /// <summary>
        /// Muestra/oculta la contraseña (icono de ojo). Cambia el
        /// <c>PasswordRevealMode</c> del campo y el icono del botón.
        /// </summary>
        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            bool show = PasswordBox.PasswordRevealMode == PasswordRevealMode.Hidden;
            PasswordBox.PasswordRevealMode = show ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
            TogglePasswordIcon.Icon = show ? Icon.EyeOff : Icon.Eye;
            ToolTipService.SetToolTip(TogglePasswordButton, show ? "Ocultar contraseña" : "Mostrar contraseña");
        }

        /// <summary>Permite enviar el formulario pulsando Enter en cualquier campo.</summary>
        private void AuthField_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                AuthSubmitButton_Click(sender, e);
            }
        }

        /// <summary>Cierra la sesión del usuario actual y refresca la UI.</summary>
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            StopVerificationPoll();
            AuthService.Instance.SignOut();
            ResetAuthForm();
            RefreshProfile();
        }

        /// <summary>Deshabilita/oculta los controles del formulario durante una operación de red.</summary>
        private void SetAuthBusy(bool busy)
        {
            AuthSubmitButton.IsEnabled = !busy;
            AuthToggleButton.IsEnabled = !busy;
            TogglePasswordButton.IsEnabled = !busy;
            EmailBox.IsEnabled = !busy;
            PasswordBox.IsEnabled = !busy;
            DisplayNameBox.IsEnabled = !busy;
            AuthProgressRing.IsActive = busy;
            AuthProgressRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

            if (busy)
            {
                HideAuthStates();
            }
        }

        /// <summary>Limpia los campos del formulario de autenticación.</summary>
        private void ResetAuthForm()
        {
            EmailBox.Text = "";
            PasswordBox.Password = "";
            DisplayNameBox.Text = "";
            HideAuthStates();
        }

        /// <summary>Oculta todos los mensajes de estado del formulario de autenticación.</summary>
        private void HideAuthStates()
        {
            AuthErrorText.Visibility = Visibility.Collapsed;
            AuthVerifyNotePanel.Visibility = Visibility.Collapsed;
            AuthSuccessPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Muestra el mensaje de error de autenticación.</summary>
        private void ShowAuthError(string message)
        {
            HideAuthStates();
            AuthErrorText.Text = message;
            AuthErrorText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Muestra el panel ámbar: el correo no está verificado y la sesión no
        /// se abrió. El usuario puede reenviar el enlace de verificación.
        /// </summary>
        private void ShowAuthVerifyNote()
        {
            HideAuthStates();
            AuthVerifyNotePanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Muestra el panel verde de éxito (cuenta creada / enlace reenviado).
        /// </summary>
        private void ShowAuthSuccess(string title, string subtitle)
        {
            HideAuthStates();
            AuthSuccessTitleText.Text = title;
            AuthSuccessSubtitleText.Text = subtitle;
            AuthSuccessPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Reenvía el correo de verificación de la cuenta sin verificar.
        /// Usa el correo y la contraseña actuales del formulario.
        /// </summary>
        private async void ResendVerifyButton_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailBox.Text.Trim();
            var password = PasswordBox.Password;

            if (!IsValidEmail(email) || string.IsNullOrEmpty(password))
            {
                ShowAuthError("Ingresa tu correo y tu contraseña para reenviar el enlace.");
                return;
            }

            SetAuthBusy(true);
            try
            {
                await AuthService.Instance.SendVerificationEmailAsync(email, password);
                ShowAuthSuccess(
                    "Enlace reenviado",
                    "Te enviamos otro correo de verificación. Revisa tu bandeja (y la carpeta de spam) y confirma el enlace.");
                StartVerificationPoll();
            }
            catch (Exception ex)
            {
                ShowAuthError(AuthService.GetAuthErrorMessage(ex));
            }
            finally
            {
                SetAuthBusy(false);
            }
        }

        /// <summary>Valida el formato de un correo antes de enviarlo al servidor.</summary>
        private static bool IsValidEmail(string email)
        {
            return !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
        }

        // ================================================================
        // AUTO-LOGIN TRAS VERIFICAR EL CORREO
        // ================================================================

        /// <summary>
        /// Inicia el polling que comprueba periódicamente si el correo pendiente
        /// ya se verificó en el navegador. Cuando ocurre, inicia la sesión solo
        /// (auto-login) y refresca la página (perfil + sugerencias).
        /// </summary>
        private void StartVerificationPoll()
        {
            if (_verifyPollTimer != null)
            {
                return;
            }

            _verifyPollTicks = 0;
            _verifyPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(VerifyPollIntervalSeconds) };
            _verifyPollTimer.Tick += VerifyPollTimer_Tick;
            _verifyPollTimer.Start();
        }

        /// <summary>Detiene el polling de verificación (si está activo).</summary>
        private void StopVerificationPoll()
        {
            if (_verifyPollTimer != null)
            {
                _verifyPollTimer.Stop();
                _verifyPollTimer.Tick -= VerifyPollTimer_Tick;
                _verifyPollTimer = null;
            }
        }

        /// <summary>
        /// Tick del polling: consulta el estado real del correo y, si ya está
        /// verificado, hace el auto-login y refresca la UI.
        /// </summary>
        private async void VerifyPollTimer_Tick(object? sender, object e)
        {
            if (++_verifyPollTicks > VerifyPollMaxTicks)
            {
                // Se esperó suficiente: se deja de consultar (el usuario puede
                // reenviar el enlace o volver a entrar a la página).
                StopVerificationPoll();
                return;
            }

            bool logged = await AuthService.Instance.CompleteVerificationAsync();
            if (logged)
            {
                StopVerificationPoll();
                ResetAuthForm();
                RefreshProfile();
            }
        }

        // ================================================================
        // SUGERENCIAS (solo con sesión iniciada)
        // ================================================================

        /// <summary>
        /// Envía la sugerencia del usuario logueado a Firestore vía
        /// <see cref="AuthService.SubmitSuggestionAsync"/>. Bloquea el botón y
        /// muestra un ProgressRing mientras la red trabaja.
        /// </summary>
        private async void SendSuggestionButton_Click(object sender, RoutedEventArgs e)
        {
            var message = SuggestionBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                ShowSuggestionInfo(InfoBarSeverity.Warning, "Escribe tu sugerencia antes de enviar.");
                return;
            }

            SendSuggestionButton.IsEnabled = false;
            SuggestionProgressRing.IsActive = true;
            SuggestionProgressRing.Visibility = Visibility.Visible;
            SuggestionInfoBar.IsOpen = false;

            try
            {
                await AuthService.Instance.SubmitSuggestionAsync(message);
                SuggestionBox.Text = "";
                UpdateSuggestionCounter();
                ShowSuggestionInfo(
                    InfoBarSeverity.Success,
                    "¡Gracias por tu feedback!",
                    "Tu sugerencia se envió correctamente y la revisaremos.");
            }
            catch (Exception ex)
            {
                ShowSuggestionInfo(
                    InfoBarSeverity.Error,
                    "No se pudo enviar la sugerencia.",
                    AuthService.GetAuthErrorMessage(ex));
            }
            finally
            {
                SendSuggestionButton.IsEnabled = true;
                SuggestionProgressRing.IsActive = false;
                SuggestionProgressRing.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Actualiza el contador de caracteres del cuadro de sugerencias.</summary>
        private void UpdateSuggestionCounter()
        {
            SuggestionCounterText.Text = $"{SuggestionBox.Text.Length}/{AppLimits.SuggestionsMaxLength}";
        }

        /// <summary>
        /// Refresca el contador y cierra el InfoBar de sugerencias cuando el
        /// usuario empieza a escribir de nuevo.
        /// </summary>
        private void SuggestionBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSuggestionCounter();
            SuggestionInfoBar.IsOpen = false;
        }

        /// <summary>Muestra el feedback del envío de sugerencias en el InfoBar.</summary>
        private void ShowSuggestionInfo(InfoBarSeverity severity, string title, string? message = null)
        {
            SuggestionInfoBar.Severity = severity;
            SuggestionInfoBar.Title = title;
            SuggestionInfoBar.Message = message;
            SuggestionInfoBar.IsOpen = true;
        }

        // ================================================================
        // NOVEDADES (release notes)
        // ================================================================

        /// <summary>
        /// Carga las notas de versión desde Assets/release_notes.txt
        /// y las muestra en el TextBlock de novedades.
        /// </summary>
        private void LoadReleaseNotes()
        {
            try
            {
                var notesPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "release_notes.txt");
                if (System.IO.File.Exists(notesPath))
                {
                    var content = System.IO.File.ReadAllText(notesPath);
                    ReleaseNotesText.Text = content.Trim();
                }
                else
                {
                    ReleaseNotesText.Text = "No hay notas de versión disponibles.";
                }
            }
            catch
            {
                ReleaseNotesText.Text = "No se pudieron cargar las notas de versión.";
            }
        }

        /// <summary>Muestra u oculta la sección de novedades al pulsar el icono [i].</summary>
        private void ShowReleaseNotesButton_Click(object sender, RoutedEventArgs e)
        {
            ReleaseNotesSection.Visibility =
                ReleaseNotesSection.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        // ================================================================
        // ACTUALIZACIONES
        // ================================================================

        /// <summary>
        /// Comprueba si hay actualizaciones usando Velopack + GitHub Releases.
        /// </summary>
        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            DownloadUpdateButton.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = "Buscando...";
            UpdateStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 168, 143));

            var result = await UpdateChecker.Instance.CheckForUpdatesAsync();
            ApplyUpdateResult(result);

            CheckUpdatesButton.IsEnabled = true;
        }

        /// <summary>Refleja en la UI el resultado de la comprobación de actualizaciones.</summary>
        private void ApplyUpdateResult(UpdateCheckResult result)
        {
            InstalledVersionText.Text = $"Versión instalada: {result.InstalledVersion}";
            LatestVersionText.Text = result.IsUpdateAvailable
                ? $"Disponible: {result.LatestVersion}"
                : $"Última versión: {result.LatestVersion}";

            if (result.IsUpdateAvailable)
            {
                // Badge iluminado (ámbar) cuando hay una actualización disponible.
                UpdateStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE6, 0x7E, 0x22));
                UpdateStatusIcon.Icon = FluentIcons.Common.Icon.ArrowDownload;
                UpdateStatusText.Text = "¡Actualización disponible!";
                DownloadUpdateButton.Visibility = Visibility.Visible;
                DownloadUpdateText.Text = $"Descargar v{result.LatestVersion}";
            }
            else
            {
                // Badge verde: la app está al día.
                UpdateStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 112, 173, 71));
                UpdateStatusIcon.Icon = FluentIcons.Common.Icon.CheckmarkCircle;
                UpdateStatusText.Text = "Al día";
                DownloadUpdateButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Descarga la actualización mostrando progreso y luego la aplica (reinicia la app).
        /// </summary>
        private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadUpdateButton.IsEnabled = false;
            CheckUpdatesButton.IsEnabled = false;
            DownloadProgressRing.Visibility = Visibility.Visible;
            DownloadProgressRing.Value = 0;

            try
            {
                await UpdateChecker.Instance.DownloadUpdateAsync(progress =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        DownloadProgressRing.Value = progress;
                        DownloadUpdateText.Text = progress < 100
                            ? $"Descargando... {progress}%"
                            : "Instalando...";
                    });
                });

                // Aplicar y reiniciar
                UpdateChecker.Instance.ApplyUpdate();
            }
            catch (Exception)
            {
                DownloadUpdateButton.IsEnabled = true;
                CheckUpdatesButton.IsEnabled = true;
                DownloadProgressRing.Visibility = Visibility.Collapsed;
                DownloadUpdateText.Text = "Error al descargar";
            }
        }

    }
}

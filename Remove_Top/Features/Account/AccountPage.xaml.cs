using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Remove_Top.Helpers;

namespace Remove_Top.Features.Account
{
    /// <summary>
    /// Página "Cuenta": perfil y actualizaciones de la aplicación.
    /// Sigue el mismo patrón del resto de funcionalidades:
    ///   - ViewModel inline en el code-behind.
    ///   - Textos del encabezado (título/subtítulo/badge) centralizados en AppLimits.
    ///   - Secciones en tarjetas (LayerFillColorDefaultBrush + borde de elevación).
    ///
    /// Estado actual:
    ///   - Perfil: solo la interfaz; el login con Google se implementa luego
    ///     (ver <see cref="AuthService"/>).
    ///   - Actualizaciones: en modo simulado (ver <see cref="UpdateChecker"/>).
    /// </summary>
    public sealed partial class AccountPage : Page
    {
        public AccountPage()
        {
            InitializeComponent();

            // Identidad de la app y textos del encabezado, centralizados en AppLimits.
            BrandText.Text = AppLimits.AppName;
            PageTitleText.Text = AppLimits.AccountPageTitle;
            PageSubtitleText.Text = AppLimits.AccountPageSubtitle;
            AppSiteText.Text = AppLimits.AppBrandSite;

            Loaded += AccountPage_Loaded;
        }

        /// <summary>Al cargar, refresca el perfil.</summary>
        private void AccountPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshProfile();
        }

        // ================================================================
        // PERFIL / LOGIN
        // ================================================================

        /// <summary>Actualiza la sección de perfil según el estado de la sesión.</summary>
        private void RefreshProfile()
        {
            var user = AuthService.Instance.CurrentUser;
            bool logged = AuthService.Instance.IsLoggedIn;

            ProfileNameText.Text = logged ? user!.DisplayName : "No has iniciado sesión";
            ProfileEmailText.Text = logged ? user!.Email : "Inicia sesión para sincronizar tu perfil";
            LoginButton.Visibility = logged ? Visibility.Collapsed : Visibility.Visible;
            LogoutButton.Visibility = logged ? Visibility.Visible : Visibility.Collapsed;
            LoginNoteText.Visibility = logged ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Intenta iniciar sesión con Google. Hoy el servicio devuelve null
        /// (login pendiente de implementar), así que solo se informa al usuario.
        /// </summary>
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            var user = await AuthService.Instance.LoginWithGoogleAsync();
            LoginButton.IsEnabled = true;

            if (user != null)
            {
                RefreshProfile();
            }
            else
            {
                LoginNoteText.Text = "El inicio de sesión con Google estará disponible en una próxima versión.";
            }
        }

        /// <summary>Cierra la sesión del usuario actual y refresca la UI.</summary>
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            AuthService.Instance.SignOut();
            LoginNoteText.Text = "El inicio de sesión con Google estará disponible en una próxima versión.";
            RefreshProfile();
        }

        // ================================================================
        // ACTUALIZACIONES
        // ================================================================

        /// <summary>
        /// Comprueba si hay actualizaciones y actualiza el badge de estado.
        /// Hoy el checker está en modo simulado (no consulta la red).
        /// </summary>
        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
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
            }
            else
            {
                // Badge verde: la app está al día.
                UpdateStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 112, 173, 71));
                UpdateStatusIcon.Icon = FluentIcons.Common.Icon.CheckmarkCircle;
                UpdateStatusText.Text = "Al día";
            }
        }

    }
}

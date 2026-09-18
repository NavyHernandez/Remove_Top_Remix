using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remove_Top.Helpers;
using System;
using System.Threading.Tasks;

namespace Remove_Top.Features.Downloader
{
    /// <summary>
    /// Diálogo de login de YouTube con WebView2: el usuario inicia sesión en la
    /// página real de Google; la app detecta la sesión por cookies, exporta el
    /// cookies.txt y se cierra sola. La contraseña nunca pasa por nuestro código.
    /// </summary>
    public sealed partial class YouTubeLoginDialog : ContentDialog
    {
        private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool _connected;

        /// <summary>True si el usuario conectó su cuenta en este diálogo.</summary>
        public bool Connected { get; private set; }

        public YouTubeLoginDialog()
        {
            InitializeComponent();
            NoteText.Text = AppLimits.YouTubeAccountNote;
            StatusText.Text = AppLimits.YouTubeAccountWaiting;
            _pollTimer.Tick += PollTimer_Tick;
            Closed += (_, _) => Cleanup();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var env = await YouTubeSession.EnsureEnvironmentAsync();
                await LoginWeb.EnsureCoreWebView2Async(env);
                if (await YouTubeSession.IsLoggedInAsync(LoginWeb.CoreWebView2))
                {
                    await FinishConnectedAsync();
                    return;
                }
                LoginWeb.Source = new Uri(YouTubeSession.LoginPageUrl);
                _pollTimer.Start();
            }
            catch (Exception ex)
            {
                App.Log("YouTubeLoginDialog.Init", ex.Message, ex.StackTrace);
                StatusText.Text = AppLimits.YouTubeAccountError;
            }
        }

        private async void PollTimer_Tick(object? sender, object e)
        {
            try
            {
                if (LoginWeb.CoreWebView2 == null || _connected)
                    return;
                if (await YouTubeSession.IsLoggedInAsync(LoginWeb.CoreWebView2))
                    await FinishConnectedAsync();
            }
            catch (Exception ex)
            {
                App.Log("YouTubeLoginDialog.Poll", ex.Message, ex.StackTrace);
            }
        }

        private async Task FinishConnectedAsync()
        {
            _connected = true;
            _pollTimer.Stop();
            var count = 0;
            try { count = await YouTubeSession.ExportCookiesAsync(LoginWeb.CoreWebView2!); } catch { }
            App.Log("YouTubeLoginDialog.Connected", $"cookies exportadas: {count}");
            Connected = count > 0;
            StatusText.Text = AppLimits.YouTubeAccountConnected;
            await Task.Delay(600);
            Hide();
        }

        private void Cleanup()
        {
            try { _pollTimer.Stop(); } catch { }
            try { LoginWeb.Close(); } catch { }
        }
    }
}

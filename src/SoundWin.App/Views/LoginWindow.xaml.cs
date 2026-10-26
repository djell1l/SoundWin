using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.Views;

public sealed partial class LoginWindow : Window
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly CredentialService _credentials;

    public event EventHandler<UserSession>? LoginSucceeded;

    public LoginWindow()
    {
        InitializeComponent();

        _apiClient = App.Services.GetRequiredService<SoundCloudApiClient>();
        _credentials = App.Services.GetRequiredService<CredentialService>();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(540, 620));
    }

    private void OnOpenBrowserClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://soundcloud.com/signin",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async void OnPasteClipboardClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    OAuthTokenBox.Password = text.Trim().Trim('"');
                }
            }
        }
        catch { }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        var rawToken = OAuthTokenBox.Password?.Trim();
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Title = "Jeton manquant";
            StatusInfoBar.Message = "Veuillez coller votre jeton oauth_token SoundCloud.";
            StatusInfoBar.IsOpen = true;
            return;
        }

        // Clean prefix if user pasted "OAuth 2-..." or "oauth_token=..."
        if (rawToken.StartsWith("OAuth ", StringComparison.OrdinalIgnoreCase))
            rawToken = rawToken.Substring(6).Trim();
        if (rawToken.StartsWith("oauth_token=", StringComparison.OrdinalIgnoreCase))
            rawToken = rawToken.Substring(12).Trim();
        rawToken = rawToken.Trim('"', '\'', ';', ' ');

        ValidationProgress.Visibility = Visibility.Visible;
        ConnectButton.IsEnabled = false;
        StatusInfoBar.IsOpen = false;

        try
        {
            _apiClient.OAuthToken = rawToken;
            var me = await _apiClient.GetMeAsync();

            if (me == null || me.UserId <= 0)
            {
                _apiClient.OAuthToken = null;
                StatusInfoBar.Severity = InfoBarSeverity.Error;
                StatusInfoBar.Title = "Échec d'authentification";
                StatusInfoBar.Message = "Ce jeton oauth_token est invalide ou expiré. Vérifiez la valeur copiée depuis soundcloud.com.";
                StatusInfoBar.IsOpen = true;
                return;
            }

            _credentials.SaveSession(me);
            LoginSucceeded?.Invoke(this, me);
            Close();
        }
        catch (Exception ex)
        {
            _apiClient.OAuthToken = null;
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = "Erreur réseau";
            StatusInfoBar.Message = ex.Message;
            StatusInfoBar.IsOpen = true;
        }
        finally
        {
            ValidationProgress.Visibility = Visibility.Collapsed;
            ConnectButton.IsEnabled = true;
        }
    }
}

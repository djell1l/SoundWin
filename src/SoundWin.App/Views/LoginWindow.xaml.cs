using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.Views;

public sealed partial class LoginWindow : Window
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly CredentialService _credentials;
    private bool _hasCapturedToken;

    public event EventHandler<UserSession>? LoginSucceeded;

    public LoginWindow()
    {
        InitializeComponent();

        _apiClient = App.Services.GetRequiredService<SoundCloudApiClient>();
        _credentials = App.Services.GetRequiredService<CredentialService>();

        // Center and set window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 720));

        InitializeWebView();
    }

    private async void InitializeWebView()
    {
        try
        {
            await LoginWebView.EnsureCoreWebView2Async();
            LoginWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            LoginWebView.Source = new Uri("https://soundcloud.com/signin");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoginWindow] WebView2 error: {ex.Message}");
        }
    }

    private async void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        LoginProgress.Visibility = Visibility.Collapsed;

        if (_hasCapturedToken) return;

        try
        {
            var cookieManager = LoginWebView.CoreWebView2.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync("https://soundcloud.com");

            foreach (var cookie in cookies)
            {
                if (cookie.Name == "oauth_token" && !string.IsNullOrEmpty(cookie.Value))
                {
                    _hasCapturedToken = true;
                    Debug.WriteLine($"[LoginWindow] OAuth token captured: {cookie.Value.Substring(0, 10)}...");

                    _apiClient.OAuthToken = cookie.Value;
                    var me = await _apiClient.GetMeAsync();

                    var session = new UserSession
                    {
                        UserId = me?.UserId ?? 0,
                        Username = me?.Username ?? "User",
                        AvatarUrl = me?.AvatarUrl,
                        OAuthToken = cookie.Value
                    };

                    _credentials.SaveSession(session);
                    LoginSucceeded?.Invoke(this, session);

                    Close();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoginWindow] Cookie extraction error: {ex.Message}");
        }
    }
}

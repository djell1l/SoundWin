using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SoundWin.Core.Models;
using SoundWin.Core.Services;
using SoundWin.Core.Utils;

namespace SoundWin.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CredentialService _credentials;
    private readonly SoundCloudApiClient _apiClient;

    [ObservableProperty]
    private UserSession? _session;

    [ObservableProperty]
    private string _headerSearchText = string.Empty;

    [ObservableProperty]
    private string _activeUsername = "Invité";

    [ObservableProperty]
    private string? _activeUserAvatar;

    [ObservableProperty]
    private bool _isUserAuthenticated;

    [ObservableProperty]
    private bool _hasUpdateBanner;

    [ObservableProperty]
    private string _updateBannerMessage = string.Empty;

    [ObservableProperty]
    private string _updateBannerUrl = string.Empty;

    public MainViewModel(CredentialService credentials, SoundCloudApiClient apiClient)
    {
        _credentials = credentials;
        _apiClient = apiClient;
        RefreshSession();
    }

    public void ShowUpdateBanner(string tagName, string htmlUrl, string channel)
    {
        UpdateBannerMessage = $"Une nouvelle version ({tagName}) est disponible sur le canal {channel}.";
        UpdateBannerUrl = htmlUrl;
        HasUpdateBanner = true;
    }

    public void RefreshSession()
    {
        var session = _credentials.LoadSession();
        if (session != null && !string.IsNullOrEmpty(session.OAuthToken))
        {
            _apiClient.OAuthToken = session.OAuthToken;
        }
        UpdateSession(session);
        if (session != null && session.IsAuthenticated)
        {
            _ = SyncUserProfileOnlineAsync();
        }
    }

    public void UpdateSession(UserSession? session)
    {
        Session = session;
        if (session != null && session.IsAuthenticated)
        {
            ActiveUsername = string.IsNullOrWhiteSpace(session.Username) ? "Utilisateur" : session.Username;
            ActiveUserAvatar = session.AvatarUrl?.Replace("-large.", "-t500x500.");
            IsUserAuthenticated = true;
        }
        else
        {
            ActiveUsername = "Invité";
            ActiveUserAvatar = null;
            IsUserAuthenticated = false;
        }
    }

    public async Task SyncUserProfileOnlineAsync()
    {
        try
        {
            var me = await _apiClient.GetMeAsync();
            if (me != null && me.UserId > 0)
            {
                _credentials.SaveSession(me);
                AppDispatcher.Dispatch(() =>
                {
                    UpdateSession(me);
                });
            }
        }
        catch { }
    }
}

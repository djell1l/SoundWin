using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;
using SoundWin.Core.Utils;

namespace SoundWin.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly DiscordRpcService _rpc;
    private readonly CredentialService _credentials;
    private readonly SoundCloudApiClient _apiClient;

    [ObservableProperty]
    private bool _isDiscordRpcEnabled;

    [ObservableProperty]
    private bool _discordRpcShowCoverArt;

    [ObservableProperty]
    private bool _discordRpcShowButtons;

    [ObservableProperty]
    private bool _discordRpcShowPaused;

    [ObservableProperty]
    private string _discordRpcClientId = string.Empty;

    [ObservableProperty]
    private bool _isDiscordConnected;

    [ObservableProperty]
    private string _discordStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBridgeEnabled;

    [ObservableProperty]
    private bool _isUserLoggedIn;

    [ObservableProperty]
    private string _username = "Invité";

    [ObservableProperty]
    private long _userId;

    public string DiscordStatusTooltip => !IsDiscordRpcEnabled
        ? "Discord RPC : Désactivé"
        : IsDiscordConnected
            ? "Discord RPC : Connecté"
            : "Discord RPC : En attente de Discord...";

    public string DiscordStatusBadgeText => !IsDiscordRpcEnabled
        ? "Désactivé"
        : IsDiscordConnected
            ? "Connecté"
            : "En recherche...";

    public string DiscordStatusBadgeColor => !IsDiscordRpcEnabled
        ? "#6B7280" // Gray
        : IsDiscordConnected
            ? "#10B981" // Emerald / Green
            : "#F59E0B"; // Amber / Orange

    public SettingsViewModel(
        SettingsService settings,
        DiscordRpcService rpc,
        CredentialService credentials,
        SoundCloudApiClient apiClient)
    {
        _settings = settings;
        _rpc = rpc;
        _credentials = credentials;
        _apiClient = apiClient;

        // Initialize from settings
        _isDiscordRpcEnabled = _settings.Current.EnableDiscordRpc;
        _discordRpcShowCoverArt = _settings.Current.DiscordRpcShowCoverArt;
        _discordRpcShowButtons = _settings.Current.DiscordRpcShowButtons;
        _discordRpcShowPaused = _settings.Current.DiscordRpcShowPaused;
        _discordRpcClientId = _settings.Current.DiscordRpcClientId;
        _isBridgeEnabled = _settings.Current.EnableSoundWinBridge;

        _isDiscordConnected = _rpc.IsConnected;
        _discordStatusMessage = _rpc.StatusMessage;

        _rpc.ConnectionStatusChanged += isConnected =>
        {
            AppDispatcher.Dispatch(() =>
            {
                IsDiscordConnected = isConnected;
                DiscordStatusMessage = _rpc.StatusMessage;
                OnPropertyChanged(nameof(DiscordStatusTooltip));
                OnPropertyChanged(nameof(DiscordStatusBadgeText));
                OnPropertyChanged(nameof(DiscordStatusBadgeColor));
            });
        };

        RefreshUserSession();
    }

    public void RefreshUserSession()
    {
        var session = _credentials.LoadSession();
        if (session != null && session.IsAuthenticated)
        {
            IsUserLoggedIn = true;
            Username = session.Username;
            UserId = session.UserId;
        }
        else
        {
            IsUserLoggedIn = false;
            Username = "Invité";
            UserId = 0;
        }
    }

    partial void OnIsDiscordRpcEnabledChanged(bool value)
    {
        _settings.Current.EnableDiscordRpc = value;
        _settings.Save();
        DiscordStatusMessage = _rpc.StatusMessage;
        OnPropertyChanged(nameof(DiscordStatusTooltip));
        OnPropertyChanged(nameof(DiscordStatusBadgeText));
        OnPropertyChanged(nameof(DiscordStatusBadgeColor));
    }

    partial void OnDiscordRpcShowCoverArtChanged(bool value)
    {
        _settings.Current.DiscordRpcShowCoverArt = value;
        _settings.Save();
    }

    partial void OnDiscordRpcShowButtonsChanged(bool value)
    {
        _settings.Current.DiscordRpcShowButtons = value;
        _settings.Save();
    }

    partial void OnDiscordRpcShowPausedChanged(bool value)
    {
        _settings.Current.DiscordRpcShowPaused = value;
        _settings.Save();
    }

    partial void OnDiscordRpcClientIdChanged(string value)
    {
        _settings.Current.DiscordRpcClientId = value;
        _settings.Save();
    }

    partial void OnIsBridgeEnabledChanged(bool value)
    {
        _settings.Current.EnableSoundWinBridge = value;
        _settings.Save();
    }

    [RelayCommand]
    public void ResetDiscordClientId()
    {
        DiscordRpcClientId = SoundWinSettings.DefaultDiscordClientId;
    }

    [RelayCommand]
    public void ToggleDiscordRpc()
    {
        IsDiscordRpcEnabled = !IsDiscordRpcEnabled;
    }

    [RelayCommand]
    public void Logout()
    {
        _credentials.ClearSession();
        _apiClient.OAuthToken = null;
        RefreshUserSession();
    }
}

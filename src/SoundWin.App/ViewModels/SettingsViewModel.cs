using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.App.Services;
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

    [ObservableProperty]
    private string _currentVersion = UpdateService.CurrentVersion;

    [ObservableProperty]
    private string _updateChannel;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private bool _autoCheckUpdatesOnStartup;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private string _updateCheckResult = string.Empty;

    [ObservableProperty]
    private bool _isUpdateResultOpen;

    [ObservableProperty]
    private bool _hasUpdateAvailable;

    [ObservableProperty]
    private string _latestUpdateUrl = string.Empty;

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

        _updateChannel = _settings.Current.UpdateChannel;
        _minimizeToTrayOnClose = _settings.Current.MinimizeToTrayOnClose;
        _autoCheckUpdatesOnStartup = _settings.Current.AutoCheckUpdatesOnStartup;

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

    partial void OnUpdateChannelChanged(string value)
    {
        _settings.Current.UpdateChannel = value;
        _settings.Save();
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        _settings.Current.MinimizeToTrayOnClose = value;
        _settings.Save();
    }

    partial void OnAutoCheckUpdatesOnStartupChanged(bool value)
    {
        _settings.Current.AutoCheckUpdatesOnStartup = value;
        _settings.Save();
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        IsUpdateResultOpen = false;
        HasUpdateAvailable = false;

        try
        {
            var update = await UpdateService.CheckForUpdatesAsync(UpdateChannel);
            if (update != null)
            {
                HasUpdateAvailable = true;
                LatestUpdateUrl = update.HtmlUrl;
                UpdateCheckResult = $"Nouvelle version disponible : {update.TagName} !";
                IsUpdateResultOpen = true;
            }
            else
            {
                UpdateCheckResult = $"Vous utilisez déjà la dernière version de SCwin ({CurrentVersion}).";
                IsUpdateResultOpen = true;
            }
        }
        catch (Exception ex)
        {
            UpdateCheckResult = $"Erreur lors de la vérification : {ex.Message}";
            IsUpdateResultOpen = true;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public void OpenLatestUpdateUrl()
    {
        if (!string.IsNullOrEmpty(LatestUpdateUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LatestUpdateUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    public void OpenGitHubReleases()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/djell1l/SoundWin/releases",
                UseShellExecute = true
            });
        }
        catch { }
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

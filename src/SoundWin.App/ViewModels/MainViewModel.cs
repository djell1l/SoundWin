using CommunityToolkit.Mvvm.ComponentModel;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CredentialService _credentials;
    private readonly SoundCloudApiClient _apiClient;

    [ObservableProperty]
    private UserSession? _session;

    [ObservableProperty]
    private string _headerSearchText = string.Empty;

    public MainViewModel(CredentialService credentials, SoundCloudApiClient apiClient)
    {
        _credentials = credentials;
        _apiClient = apiClient;
        Session = _credentials.LoadSession();
    }

    public void RefreshSession()
    {
        Session = _credentials.LoadSession();
    }
}

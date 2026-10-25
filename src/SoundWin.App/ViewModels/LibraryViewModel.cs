using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly AudioPlayerService _player;
    private readonly CredentialService _credentials;

    [ObservableProperty]
    private UserSession? _session;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isAuthenticated;

    public ObservableCollection<Track> LikedTracks { get; } = new();

    public LibraryViewModel(SoundCloudApiClient apiClient, AudioPlayerService player, CredentialService credentials)
    {
        _apiClient = apiClient;
        _player = player;
        _credentials = credentials;

        Session = _credentials.LoadSession();
        IsAuthenticated = Session?.IsAuthenticated == true;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Session = _credentials.LoadSession();
        IsAuthenticated = Session?.IsAuthenticated == true;

        if (!IsAuthenticated || Session == null) return;

        IsLoading = true;
        try
        {
            var likes = await _apiClient.GetLikedTracksAsync(Session.UserId, 50);
            LikedTracks.Clear();
            foreach (var t in likes) LikedTracks.Add(t);
        }
        catch { }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task PlayTrack(Track track)
    {
        if (track == null) return;
        var queue = new ObservableCollection<Track>(LikedTracks);
        var idx = LikedTracks.IndexOf(track);
        await _player.PlayTrackAsync(track, queue, idx);
    }

    [RelayCommand]
    public void Logout()
    {
        _credentials.ClearSession();
        _apiClient.OAuthToken = null;
        Session = null;
        IsAuthenticated = false;
        LikedTracks.Clear();
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;
using SoundWin.Core.Utils;

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
    public ObservableCollection<Playlist> UserPlaylists { get; } = new();

    public LibraryViewModel(SoundCloudApiClient apiClient, AudioPlayerService player, CredentialService credentials)
    {
        _apiClient = apiClient;
        _player = player;
        _credentials = credentials;

        Session = _credentials.LoadSession();
        IsAuthenticated = Session?.IsAuthenticated == true;

        _apiClient.TrackLikeChanged += OnTrackLikeChanged;
        _apiClient.UserPlaylistsChanged += OnUserPlaylistsChanged;
    }

    private async void OnTrackLikeChanged(long trackId, bool isLiked)
    {
        if (isLiked)
        {
            if (LikedTracks.Any(t => t.Id == trackId)) return;
            try
            {
                var tracks = await _apiClient.GetTracksByIdsAsync(new[] { trackId });
                var track = tracks.FirstOrDefault();
                if (track != null)
                {
                    track.IsLiked = true;
                    AppDispatcher.Dispatch(() =>
                    {
                        if (!LikedTracks.Any(t => t.Id == trackId))
                        {
                            LikedTracks.Insert(0, track);
                        }
                    });
                }
            }
            catch { }
        }
        else
        {
            AppDispatcher.Dispatch(() =>
            {
                var existing = LikedTracks.FirstOrDefault(t => t.Id == trackId);
                if (existing != null)
                {
                    LikedTracks.Remove(existing);
                }
            });
        }
    }

    private async void OnUserPlaylistsChanged()
    {
        if (Session == null || !IsAuthenticated) return;
        try
        {
            var pls = await _apiClient.GetUserPlaylistsAsync(Session.UserId, 50);
            AppDispatcher.Dispatch(() =>
            {
                UserPlaylists.Clear();
                foreach (var p in pls) UserPlaylists.Add(p);
            });
        }
        catch { }
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
            await _apiClient.RefreshLikedTrackIdsAsync(Session.UserId);

            var likesTask = _apiClient.GetLikedTracksAsync(Session.UserId, 50);
            var playlistsTask = _apiClient.GetUserPlaylistsAsync(Session.UserId, 50);

            await Task.WhenAll(likesTask, playlistsTask);

            var likes = likesTask.Result;
            var playlists = playlistsTask.Result;

            LikedTracks.Clear();
            foreach (var t in likes) LikedTracks.Add(t);

            UserPlaylists.Clear();
            foreach (var p in playlists) UserPlaylists.Add(p);
        }
        catch { }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<Playlist?> CreatePlaylistAsync(string title, string? description, string sharing, IEnumerable<long>? initialTrackIds = null)
    {
        return await _apiClient.CreatePlaylistAsync(title, description, sharing, initialTrackIds);
    }

    [RelayCommand]
    public async Task PlayTrack(Track track)
    {
        if (track == null) return;
        var queue = new ObservableCollection<Track>(LikedTracks);
        var idx = LikedTracks.IndexOf(track);
        await _player.PlayTrackAsync(track, queue, idx >= 0 ? idx : 0);
    }

    [RelayCommand]
    public void Logout()
    {
        _credentials.ClearSession();
        _apiClient.OAuthToken = null;
        Session = null;
        IsAuthenticated = false;
        LikedTracks.Clear();
        UserPlaylists.Clear();
    }
}

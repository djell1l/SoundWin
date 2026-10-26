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

    private List<User> _allFollowings = new();

    [ObservableProperty]
    private UserSession? _session;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _followingFilterText = string.Empty;

    public ObservableCollection<Track> LikedTracks { get; } = new();
    public ObservableCollection<Playlist> UserPlaylists { get; } = new();
    public ObservableCollection<Playlist> UserAlbums { get; } = new();
    public ObservableCollection<Playlist> UserStations { get; } = new();
    public ObservableCollection<User> FollowedArtists { get; } = new();
    public ObservableCollection<Track> HistoryTracks { get; } = new();
    public ObservableCollection<User> SuggestedArtists { get; } = new();

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

    partial void OnFollowingFilterTextChanged(string value)
    {
        ApplyFollowingFilter();
    }

    public void ApplyFollowingFilter()
    {
        FollowedArtists.Clear();
        var query = FollowingFilterText?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(query)
            ? _allFollowings
            : _allFollowings.Where(u => u.Username.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var u in filtered)
        {
            FollowedArtists.Add(u);
        }
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
        if (Session != null && !string.IsNullOrEmpty(Session.OAuthToken))
        {
            _apiClient.OAuthToken = Session.OAuthToken;
        }
        IsAuthenticated = Session?.IsAuthenticated == true;

        if (!IsAuthenticated || Session == null) return;

        IsLoading = true;
        try
        {
            await _apiClient.RefreshLikedTrackIdsAsync(Session.UserId);

            async Task<List<T>> SafeFetch<T>(Task<List<T>> task)
            {
                try { return await task; }
                catch { return new List<T>(); }
            }

            var likesTask = SafeFetch(_apiClient.GetLikedTracksAsync(Session.UserId, 50));
            var playlistsTask = SafeFetch(_apiClient.GetUserPlaylistsAsync(Session.UserId, 50));
            var followingsTask = SafeFetch(_apiClient.GetUserFollowingsAsync(Session.UserId, 50));
            var historyTask = SafeFetch(_apiClient.GetPlayHistoryAsync(40));
            var suggestionsTask = SafeFetch(_apiClient.GetWhoToFollowAsync(24));

            await Task.WhenAll(likesTask, playlistsTask, followingsTask, historyTask, suggestionsTask);

            var likes = await likesTask;
            var playlists = await playlistsTask;
            var followings = await followingsTask;
            var history = await historyTask;
            var suggestions = await suggestionsTask;

            foreach (var f in followings) f.IsFollowed = true;

            AppDispatcher.Dispatch(() =>
            {
                LikedTracks.Clear();
                foreach (var t in likes) LikedTracks.Add(t);

                UserPlaylists.Clear();
                UserAlbums.Clear();
                UserStations.Clear();
                foreach (var p in playlists)
                {
                    UserPlaylists.Add(p);
                    if (p.IsSystemPlaylist) UserStations.Add(p);
                }

                _allFollowings = followings;
                ApplyFollowingFilter();

                HistoryTracks.Clear();
                foreach (var h in history) HistoryTracks.Add(h);

                SuggestedArtists.Clear();
                foreach (var s in suggestions) SuggestedArtists.Add(s);
            });
        }
        catch { }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ToggleFollowArtistAsync(User artist)
    {
        if (artist == null || Session == null || !IsAuthenticated) return;
        bool targetState = !artist.IsFollowed;
        artist.IsFollowed = targetState;

        bool ok = targetState
            ? await _apiClient.FollowUserAsync(Session.UserId, artist.Id)
            : await _apiClient.UnfollowUserAsync(Session.UserId, artist.Id);

        if (!ok)
        {
            artist.IsFollowed = !targetState;
        }
        else
        {
            if (targetState && !_allFollowings.Any(u => u.Id == artist.Id))
            {
                _allFollowings.Insert(0, artist);
                ApplyFollowingFilter();
            }
            else if (!targetState)
            {
                _allFollowings.RemoveAll(u => u.Id == artist.Id);
                ApplyFollowingFilter();
            }
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
        var source = LikedTracks.Contains(track) ? LikedTracks : HistoryTracks;
        var queue = source.Count > 0 ? new ObservableCollection<Track>(source) : new ObservableCollection<Track> { track };
        var idx = queue.IndexOf(track);
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
        UserAlbums.Clear();
        UserStations.Clear();
        FollowedArtists.Clear();
        HistoryTracks.Clear();
        SuggestedArtists.Clear();
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;
using SoundWin.Core.Utils;

namespace SoundWin.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly AudioPlayerService _player;
    private readonly CredentialService _credentials;

    public ObservableCollection<Shelf> Shelves { get; } = new();
    public ObservableCollection<User> SuggestedUsers { get; } = new();
    public ObservableCollection<Track> SidebarLikedTracks { get; } = new();
    public ObservableCollection<Track> PlayHistoryTracks { get; } = new();

    [ObservableProperty]
    private int _totalLikesCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public HomeViewModel(SoundCloudApiClient apiClient, AudioPlayerService player, CredentialService credentials)
    {
        _apiClient = apiClient;
        _player = player;
        _credentials = credentials;

        _apiClient.TrackLikeChanged += OnTrackLikeChanged;
    }

    private async void OnTrackLikeChanged(long trackId, bool isLiked)
    {
        var session = _credentials.LoadSession();
        if (session == null || !session.IsAuthenticated || session.UserId <= 0) return;

        if (isLiked)
        {
            TotalLikesCount++;
            if (!SidebarLikedTracks.Any(t => t.Id == trackId))
            {
                try
                {
                    var tracks = await _apiClient.GetTracksByIdsAsync(new[] { trackId });
                    var track = tracks.FirstOrDefault();
                    if (track != null)
                    {
                        track.IsLiked = true;
                        AppDispatcher.Dispatch(() =>
                        {
                            if (!SidebarLikedTracks.Any(t => t.Id == trackId))
                            {
                                SidebarLikedTracks.Insert(0, track);
                                if (SidebarLikedTracks.Count > 4)
                                {
                                    SidebarLikedTracks.RemoveAt(SidebarLikedTracks.Count - 1);
                                }
                            }
                        });
                    }
                }
                catch { }
            }
        }
        else
        {
            if (TotalLikesCount > 0) TotalLikesCount--;
            AppDispatcher.Dispatch(() =>
            {
                var existing = SidebarLikedTracks.FirstOrDefault(t => t.Id == trackId);
                if (existing != null)
                {
                    SidebarLikedTracks.Remove(existing);
                }
            });
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            if (Shelves.Count == 0)
            {
                var shelves = await _apiClient.GetHomeMixedSelectionsAsync(12);
                Shelves.Clear();
                foreach (var shelf in shelves)
                {
                    Shelves.Add(shelf);
                }
            }

            await RefreshSidebarAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Impossible de charger les sélections : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshSidebarAsync()
    {
        try
        {
            var session = _credentials.LoadSession();
            var whoTask = _apiClient.GetWhoToFollowAsync(4);
            var historyTask = _apiClient.GetPlayHistoryAsync(4);

            Task<System.Collections.Generic.List<Track>>? likesTask = null;
            Task<User?>? userTask = null;

            if (session != null && session.IsAuthenticated && session.UserId > 0)
            {
                likesTask = _apiClient.GetLikedTracksAsync(session.UserId, 4);
                userTask = _apiClient.GetUserDetailsAsync(session.UserId);
            }

            await Task.WhenAll(
                whoTask,
                historyTask,
                likesTask ?? Task.FromResult(new System.Collections.Generic.List<Track>()),
                userTask ?? Task.FromResult<User?>(null));

            AppDispatcher.Dispatch(() =>
            {
                SuggestedUsers.Clear();
                foreach (var u in whoTask.Result) SuggestedUsers.Add(u);

                PlayHistoryTracks.Clear();
                foreach (var h in historyTask.Result.Take(4)) PlayHistoryTracks.Add(h);

                if (likesTask != null)
                {
                    SidebarLikedTracks.Clear();
                    foreach (var l in likesTask.Result.Take(4)) SidebarLikedTracks.Add(l);
                }

                if (userTask?.Result != null)
                {
                    TotalLikesCount = userTask.Result.LikesCount;
                }
                else
                {
                    TotalLikesCount = SidebarLikedTracks.Count;
                }
            });
        }
        catch { }
    }

    public async Task FollowUserAsync(User target)
    {
        var session = _credentials.LoadSession();
        if (session == null || !session.IsAuthenticated || target == null) return;

        bool ok = await _apiClient.FollowUserAsync(session.UserId, target.Id);
        if (ok)
        {
            SuggestedUsers.Remove(target);
        }
    }

    [RelayCommand]
    public async Task PlayTrack(Track track)
    {
        if (track == null) return;

        foreach (var shelf in Shelves)
        {
            var idx = shelf.Tracks.IndexOf(track);
            if (idx >= 0)
            {
                var queue = new ObservableCollection<Track>(shelf.Tracks);
                await _player.PlayTrackAsync(track, queue, idx);
                return;
            }
        }

        await _player.PlayTrackAsync(track);
    }

    [RelayCommand]
    public async Task PlayPlaylist(Playlist playlist)
    {
        if (playlist == null) return;

        try
        {
            if (playlist.Tracks.Count == 0 || (playlist.TrackCount > playlist.Tracks.Count && !string.IsNullOrEmpty(playlist.Id)))
            {
                var full = await _apiClient.GetPlaylistDetailsAsync(playlist.Id);
                if (full.Tracks.Count > 0)
                {
                    playlist.Tracks = full.Tracks;
                    playlist.TrackIds = full.TrackIds;
                }
                else
                {
                    await _apiClient.HydratePlaylistTracksAsync(playlist);
                }
            }

            if (playlist.Tracks.Count > 0)
            {
                var queue = new ObservableCollection<Track>(playlist.Tracks);
                await _player.PlayTrackAsync(playlist.Tracks[0], queue, 0);
            }
        }
        catch { }
    }
}

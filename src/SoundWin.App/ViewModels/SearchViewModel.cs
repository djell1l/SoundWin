using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly AudioPlayerService _player;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private int _selectedFilterIndex; // 0 = Tracks, 1 = Playlists, 2 = Artists

    public ObservableCollection<Track> TrackResults { get; } = new();
    public ObservableCollection<Playlist> PlaylistResults { get; } = new();
    public ObservableCollection<User> UserResults { get; } = new();
    public ObservableCollection<string> Suggestions { get; } = new();

    public SearchViewModel(SoundCloudApiClient apiClient, AudioPlayerService player)
    {
        _apiClient = apiClient;
        _player = player;
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Suggestions.Clear();
            return;
        }

        _ = UpdateSuggestionsAsync(value);
    }

    private async Task UpdateSuggestionsAsync(string query)
    {
        try
        {
            var list = await _apiClient.GetSearchSuggestionsAsync(query);
            Suggestions.Clear();
            foreach (var item in list) Suggestions.Add(item);
        }
        catch { }
    }

    [RelayCommand]
    public async Task PerformSearchAsync(string? query = null)
    {
        var q = query ?? SearchQuery;
        if (string.IsNullOrWhiteSpace(q)) return;

        SearchQuery = q;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsSearching = true;

        try
        {
            TrackResults.Clear();
            PlaylistResults.Clear();
            UserResults.Clear();

            var tracksTask = _apiClient.SearchTracksAsync(q, 30, 0, ct);
            var playlistsTask = _apiClient.SearchPlaylistsAsync(q, 15, 0, ct);
            var usersTask = _apiClient.SearchUsersAsync(q, 10, 0, ct);

            await Task.WhenAll(tracksTask, playlistsTask, usersTask);

            foreach (var t in await tracksTask) TrackResults.Add(t);
            foreach (var p in await playlistsTask) PlaylistResults.Add(p);
            foreach (var u in await usersTask) UserResults.Add(u);
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public async Task PlayTrack(Track track)
    {
        if (track == null) return;
        var queue = new ObservableCollection<Track>(TrackResults);
        var idx = TrackResults.IndexOf(track);
        await _player.PlayTrackAsync(track, queue, idx);
    }
}

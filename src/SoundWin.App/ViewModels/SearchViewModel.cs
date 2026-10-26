using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;
using SoundWin.Core.Utils;

namespace SoundWin.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly AudioPlayerService _player;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _searchCts;
    private bool _recentsLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveQuery), nameof(ShowRecentsPanel))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private int _selectedFilterIndex;

    public ObservableCollection<Track> TrackResults { get; } = new();
    public ObservableCollection<Playlist> PlaylistResults { get; } = new();
    public ObservableCollection<User> UserResults { get; } = new();
    public ObservableCollection<string> Suggestions { get; } = new();

    // Spotify-style Recent Searches & Recently Played from Search
    public ObservableCollection<Track> RecentSearchedTracks { get; } = new();
    public ObservableCollection<string> RecentQueries { get; } = new();

    public bool HasActiveQuery => !string.IsNullOrWhiteSpace(SearchQuery) &&
                                  (TrackResults.Count > 0 || PlaylistResults.Count > 0 || UserResults.Count > 0 || IsSearching);

    public bool ShowRecentsPanel => !HasActiveQuery;

    public SearchViewModel(SoundCloudApiClient apiClient, AudioPlayerService player, SettingsService settings)
    {
        _apiClient = apiClient;
        _player = player;
        _settings = settings;

        foreach (var q in _settings.Current.RecentSearchQueries)
        {
            RecentQueries.Add(q);
        }
    }

    public async Task EnsureRecentsLoadedAsync()
    {
        if (_recentsLoaded) return;
        _recentsLoaded = true;

        try
        {
            var ids = _settings.Current.RecentSearchedTrackIds.Distinct().Take(20).ToList();
            if (ids.Count > 0)
            {
                var fetched = await _apiClient.GetTracksByIdsAsync(ids);
                var dict = fetched.ToDictionary(t => t.Id, t => t);

                AppDispatcher.Dispatch(() =>
                {
                    RecentSearchedTracks.Clear();
                    foreach (var id in ids)
                    {
                        if (dict.TryGetValue(id, out var trk))
                        {
                            RecentSearchedTracks.Add(trk);
                        }
                    }
                });
            }
            else
            {
                // Populate with user's recent play history if available so it's never empty on first run
                var hist = await _apiClient.GetPlayHistoryAsync(8);
                if (hist.Count > 0)
                {
                    AppDispatcher.Dispatch(() =>
                    {
                        RecentSearchedTracks.Clear();
                        foreach (var h in hist) RecentSearchedTracks.Add(h);
                    });
                }
            }
        }
        catch { }
    }

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveQuery));
        OnPropertyChanged(nameof(ShowRecentsPanel));

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

    public void AddRecentQuery(string query)
    {
        query = query.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        var existing = RecentQueries.FirstOrDefault(x => string.Equals(x, query, StringComparison.OrdinalIgnoreCase));
        if (existing != null) RecentQueries.Remove(existing);
        RecentQueries.Insert(0, query);
        while (RecentQueries.Count > 12) RecentQueries.RemoveAt(RecentQueries.Count - 1);

        _settings.Current.RecentSearchQueries = RecentQueries.ToList();
        _settings.Save();
    }

    public void AddRecentSearchedTrack(Track track)
    {
        if (track == null || track.Id <= 0) return;

        var existing = RecentSearchedTracks.FirstOrDefault(t => t.Id == track.Id);
        if (existing != null) RecentSearchedTracks.Remove(existing);
        RecentSearchedTracks.Insert(0, track);
        while (RecentSearchedTracks.Count > 20) RecentSearchedTracks.RemoveAt(RecentSearchedTracks.Count - 1);

        _settings.Current.RecentSearchedTrackIds = RecentSearchedTracks.Select(t => t.Id).ToList();
        _settings.Save();
    }

    public void RemoveRecentSearchedTrack(Track track)
    {
        if (track == null) return;
        RecentSearchedTracks.Remove(track);
        _settings.Current.RecentSearchedTrackIds = RecentSearchedTracks.Select(t => t.Id).ToList();
        _settings.Save();
    }

    public void ClearAllRecents()
    {
        RecentSearchedTracks.Clear();
        RecentQueries.Clear();
        _settings.Current.RecentSearchedTrackIds.Clear();
        _settings.Current.RecentSearchQueries.Clear();
        _settings.Save();
    }

    [RelayCommand]
    public async Task PerformSearchAsync(string? query = null)
    {
        var q = query ?? SearchQuery;
        if (string.IsNullOrWhiteSpace(q))
        {
            TrackResults.Clear();
            PlaylistResults.Clear();
            UserResults.Clear();
            OnPropertyChanged(nameof(HasActiveQuery));
            OnPropertyChanged(nameof(ShowRecentsPanel));
            return;
        }

        SearchQuery = q;
        AddRecentQuery(q);

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsSearching = true;
        OnPropertyChanged(nameof(HasActiveQuery));
        OnPropertyChanged(nameof(ShowRecentsPanel));

        try
        {
            TrackResults.Clear();
            PlaylistResults.Clear();
            UserResults.Clear();

            var tracksTask = _apiClient.SearchTracksAsync(q, 30, 0, ct);
            var playlistsTask = _apiClient.SearchPlaylistsAsync(q, 15, 0, ct);
            var usersTask = _apiClient.SearchUsersAsync(q, 12, 0, ct);

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
            OnPropertyChanged(nameof(HasActiveQuery));
            OnPropertyChanged(nameof(ShowRecentsPanel));
        }
    }

    [RelayCommand]
    public async Task PlayTrack(Track track)
    {
        if (track == null) return;
        AddRecentSearchedTrack(track);

        if (TrackResults.Contains(track))
        {
            var queue = new ObservableCollection<Track>(TrackResults);
            var idx = TrackResults.IndexOf(track);
            await _player.PlayTrackAsync(track, queue, idx);
        }
        else
        {
            var queue = new ObservableCollection<Track>(RecentSearchedTracks);
            var idx = RecentSearchedTracks.IndexOf(track);
            await _player.PlayTrackAsync(track, queue, idx >= 0 ? idx : 0);
        }
    }
}

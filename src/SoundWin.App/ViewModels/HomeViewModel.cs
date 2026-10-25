using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly AudioPlayerService _player;

    public ObservableCollection<Shelf> Shelves { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public HomeViewModel(SoundCloudApiClient apiClient, AudioPlayerService player)
    {
        _apiClient = apiClient;
        _player = player;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (Shelves.Count > 0) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var shelves = await _apiClient.GetHomeMixedSelectionsAsync(12);
            Shelves.Clear();
            foreach (var shelf in shelves)
            {
                Shelves.Add(shelf);
            }
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

    [RelayCommand]
    public async Task PlayTrack(Track track)
    {
        if (track == null) return;
        await _player.PlayTrackAsync(track);
    }

    [RelayCommand]
    public async Task PlayPlaylist(Playlist playlist)
    {
        if (playlist == null) return;

        try
        {
            if (playlist.Tracks.Count == 0)
            {
                var full = await _apiClient.GetPlaylistDetailsAsync(playlist.Id);
                playlist.Tracks = full.Tracks;
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

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.ViewModels;

public partial class StreamViewModel : ObservableObject
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly AudioPlayerService _player;
    private readonly CredentialService _credentials;

    public ObservableCollection<StreamPost> Posts { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public StreamViewModel(SoundCloudApiClient apiClient, AudioPlayerService player, CredentialService credentials)
    {
        _apiClient = apiClient;
        _player = player;
        _credentials = credentials;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (Posts.Count > 0) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var posts = await _apiClient.GetStreamAsync(35);
            Posts.Clear();
            foreach (var post in posts)
            {
                Posts.Add(post);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Impossible de charger le flux : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task PlayPost(StreamPost post)
    {
        if (post?.Track != null)
        {
            var tracks = new ObservableCollection<Track>();
            foreach (var p in Posts)
            {
                if (p.Track != null) tracks.Add(p.Track);
            }
            int idx = tracks.IndexOf(post.Track);
            await _player.PlayTrackAsync(post.Track, tracks, idx);
        }
        else if (post?.Playlist?.Tracks.Count > 0)
        {
            var tracks = new ObservableCollection<Track>(post.Playlist.Tracks);
            await _player.PlayTrackAsync(post.Playlist.Tracks[0], tracks, 0);
        }
    }
}

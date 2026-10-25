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

public partial class ProfileViewModel : ObservableObject
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly CredentialService _credentials;
    private readonly AudioPlayerService _player;

    [ObservableProperty]
    private User? _user;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editUsername = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private string _editCity = string.Empty;

    [ObservableProperty]
    private string _editCountry = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<Track> UserTracks { get; } = new();
    public ObservableCollection<Playlist> UserPlaylists { get; } = new();
    public ObservableCollection<Track> LikedTracks { get; } = new();

    public ProfileViewModel(SoundCloudApiClient apiClient, CredentialService credentials, AudioPlayerService player)
    {
        _apiClient = apiClient;
        _credentials = credentials;
        _player = player;

        _apiClient.TrackLikeChanged += OnTrackLikeChanged;
        _apiClient.UserPlaylistsChanged += OnUserPlaylistsChanged;
    }

    private async void OnTrackLikeChanged(long trackId, bool isLiked)
    {
        var session = _credentials.LoadSession();
        if (User == null || session == null || User.Id != session.UserId) return;

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
        if (User == null) return;
        try
        {
            var pls = await _apiClient.GetUserPlaylistsAsync(User.Id, 40);
            AppDispatcher.Dispatch(() =>
            {
                UserPlaylists.Clear();
                foreach (var p in pls) UserPlaylists.Add(p);
            });
        }
        catch { }
    }

    public async Task InitializeAsync(long? targetUserId = null)
    {
        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var session = _credentials.LoadSession();
            long userId = targetUserId ?? session?.UserId ?? 0;

            if (userId == 0)
            {
                StatusMessage = "Veuillez vous connecter pour voir votre profil.";
                IsLoading = false;
                return;
            }

            var user = await _apiClient.GetUserDetailsAsync(userId);
            if (user != null)
            {
                User = user;
                EditUsername = user.Username;
                EditDescription = user.Description ?? string.Empty;
                EditCity = user.City ?? string.Empty;
                EditCountry = user.Country ?? string.Empty;
            }

            var tracksTask = _apiClient.GetUserTracksAsync(userId);
            var playlistsTask = _apiClient.GetUserPlaylistsAsync(userId, 40);
            var likesTask = _apiClient.GetLikedTracksAsync(userId, 40);

            await Task.WhenAll(tracksTask, playlistsTask, likesTask);

            UserTracks.Clear();
            foreach (var t in tracksTask.Result) UserTracks.Add(t);

            UserPlaylists.Clear();
            foreach (var p in playlistsTask.Result) UserPlaylists.Add(p);

            LikedTracks.Clear();
            foreach (var l in likesTask.Result) LikedTracks.Add(l);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur de chargement: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ToggleEditMode()
    {
        IsEditing = !IsEditing;
    }

    [RelayCommand]
    public async Task SaveProfile()
    {
        if (User == null) return;

        IsLoading = true;
        StatusMessage = "Enregistrement du profil...";

        try
        {
            bool ok = await _apiClient.UpdateProfileAsync(
                EditUsername.Trim(),
                EditDescription.Trim(),
                EditCity.Trim(),
                EditCountry.Trim());

            if (ok)
            {
                User.Username = EditUsername.Trim();
                User.Description = EditDescription.Trim();
                User.City = EditCity.Trim();
                User.Country = EditCountry.Trim();
                IsEditing = false;
                StatusMessage = "Profil mis à jour avec succès !";
            }
            else
            {
                StatusMessage = "Impossible de mettre à jour le profil SoundCloud.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
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
        await _player.PlayTrackAsync(track, UserTracks);
    }
}

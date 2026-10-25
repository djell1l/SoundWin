using System;
using System.Collections.ObjectModel;
using System.IO;
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
    private bool _isOwnProfile;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editUsername = string.Empty;

    [ObservableProperty]
    private string _editPermalink = string.Empty;

    [ObservableProperty]
    private string _editFirstName = string.Empty;

    [ObservableProperty]
    private string _editLastName = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private string _editCity = string.Empty;

    [ObservableProperty]
    private string _editCountry = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<Track> AllRecentTracks { get; } = new();
    public ObservableCollection<Track> PopularTracks { get; } = new();
    public ObservableCollection<Track> UserTracks { get; } = new();
    public ObservableCollection<Playlist> UserPlaylists { get; } = new();
    public ObservableCollection<Track> RepostedTracks { get; } = new();
    public ObservableCollection<Track> LikedTracks { get; } = new();
    public ObservableCollection<Track> SidebarLikedTracks { get; } = new();
    public ObservableCollection<User> Followings { get; } = new();
    public ObservableCollection<WebProfileLink> WebLinks { get; } = new();

    public ProfileViewModel(SoundCloudApiClient apiClient, CredentialService credentials, AudioPlayerService player)
    {
        _apiClient = apiClient;
        _credentials = credentials;
        _player = player;

        _apiClient.TrackLikeChanged += OnTrackLikeChanged;
        _apiClient.TrackRepostChanged += OnTrackRepostChanged;
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
                            RefreshSidebarLikes();
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
                    RefreshSidebarLikes();
                }
            });
        }
    }

    private async void OnTrackRepostChanged(long trackId, bool isReposted)
    {
        var session = _credentials.LoadSession();
        if (User == null || session == null || User.Id != session.UserId) return;

        if (isReposted)
        {
            if (RepostedTracks.Any(t => t.Id == trackId)) return;
            try
            {
                var tracks = await _apiClient.GetTracksByIdsAsync(new[] { trackId });
                var track = tracks.FirstOrDefault();
                if (track != null)
                {
                    track.IsReposted = true;
                    AppDispatcher.Dispatch(() =>
                    {
                        if (!RepostedTracks.Any(t => t.Id == trackId))
                        {
                            RepostedTracks.Insert(0, track);
                            RebuildRecentAndPopular();
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
                var existing = RepostedTracks.FirstOrDefault(t => t.Id == trackId);
                if (existing != null)
                {
                    RepostedTracks.Remove(existing);
                    RebuildRecentAndPopular();
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

            IsOwnProfile = session != null && session.IsAuthenticated && session.UserId == userId;

            var user = await _apiClient.GetUserDetailsAsync(userId);
            if (user != null)
            {
                User = user;
                PopulateEditFieldsFromUser(user);
            }

            var tracksTask = _apiClient.GetUserTracksAsync(userId, 40);
            var playlistsTask = _apiClient.GetUserPlaylistsAsync(userId, 40);
            var repostsTask = _apiClient.GetUserRepostsAsync(userId, 30);
            var likesTask = _apiClient.GetLikedTracksAsync(userId, 40);
            var followingsTask = _apiClient.GetUserFollowingsAsync(userId, 16);
            var linksTask = _apiClient.GetUserWebProfilesAsync(userId);

            await Task.WhenAll(tracksTask, playlistsTask, repostsTask, likesTask, followingsTask, linksTask);

            UserTracks.Clear();
            foreach (var t in tracksTask.Result) UserTracks.Add(t);

            UserPlaylists.Clear();
            foreach (var p in playlistsTask.Result) UserPlaylists.Add(p);

            RepostedTracks.Clear();
            foreach (var r in repostsTask.Result) RepostedTracks.Add(r);

            LikedTracks.Clear();
            foreach (var l in likesTask.Result) LikedTracks.Add(l);
            RefreshSidebarLikes();

            Followings.Clear();
            foreach (var f in followingsTask.Result) Followings.Add(f);

            WebLinks.Clear();
            foreach (var w in linksTask.Result) WebLinks.Add(w);

            RebuildRecentAndPopular();
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

    public void PopulateEditFieldsFromUser(User u)
    {
        EditUsername = u.Username;
        EditPermalink = u.Permalink ?? string.Empty;
        EditFirstName = u.FirstName ?? string.Empty;
        EditLastName = u.LastName ?? string.Empty;
        EditDescription = u.Description ?? string.Empty;
        EditCity = u.City ?? string.Empty;
        EditCountry = u.Country ?? string.Empty;
    }

    private void RefreshSidebarLikes()
    {
        SidebarLikedTracks.Clear();
        foreach (var l in LikedTracks.Take(4))
        {
            SidebarLikedTracks.Add(l);
        }
    }

    private void RebuildRecentAndPopular()
    {
        AllRecentTracks.Clear();
        foreach (var t in UserTracks) AllRecentTracks.Add(t);
        foreach (var r in RepostedTracks)
        {
            if (!AllRecentTracks.Any(x => x.Id == r.Id))
            {
                AllRecentTracks.Add(r);
            }
        }

        PopularTracks.Clear();
        var sorted = UserTracks.Concat(RepostedTracks)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .OrderByDescending(t => t.PlaybackCount)
            .Take(20);
        foreach (var p in sorted) PopularTracks.Add(p);
    }

    public async Task<bool> SaveFullProfileAsync()
    {
        if (User == null) return false;

        IsLoading = true;
        StatusMessage = "Enregistrement des modifications du profil...";

        try
        {
            bool ok = await _apiClient.UpdateProfileAsync(
                EditUsername.Trim(),
                EditDescription.Trim(),
                EditCity.Trim(),
                EditCountry.Trim(),
                EditFirstName.Trim(),
                EditLastName.Trim(),
                EditPermalink.Trim());

            if (WebLinks.Count >= 0)
            {
                await _apiClient.UpdateUserWebProfilesAsync(User.Id, WebLinks);
            }

            if (ok)
            {
                var refreshed = await _apiClient.GetUserDetailsAsync(User.Id);
                if (refreshed != null)
                {
                    User = refreshed;
                    PopulateEditFieldsFromUser(refreshed);
                }
                else
                {
                    User.Username = EditUsername.Trim();
                    User.Permalink = EditPermalink.Trim();
                    User.FirstName = EditFirstName.Trim();
                    User.LastName = EditLastName.Trim();
                    User.Description = EditDescription.Trim();
                    User.City = EditCity.Trim();
                    User.Country = EditCountry.Trim();
                }

                StatusMessage = "Profil mis à jour avec succès !";
                return true;
            }
            else
            {
                StatusMessage = "Impossible de mettre à jour certaines informations du profil.";
                return false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> UploadAvatarFromFileAsync(string filePath)
    {
        if (User == null || !File.Exists(filePath)) return false;
        IsLoading = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            var res = await _apiClient.UploadAvatarAsync(bytes, Path.GetFileName(filePath));
            if (res != null)
            {
                var refreshed = await _apiClient.GetUserDetailsAsync(User.Id);
                if (refreshed != null) User = refreshed;
                else if (res != "updated") User.AvatarUrl = res;
                return true;
            }
        }
        catch { }
        finally
        {
            IsLoading = false;
        }
        return false;
    }

    public async Task<bool> UploadBannerFromFileAsync(string filePath)
    {
        if (User == null || !File.Exists(filePath)) return false;
        IsLoading = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            var res = await _apiClient.UploadHeaderImageAsync(bytes, Path.GetFileName(filePath));
            if (res != null)
            {
                var refreshed = await _apiClient.GetUserDetailsAsync(User.Id);
                if (refreshed != null) User = refreshed;
                else if (res != "updated") User.BannerUrl = res;
                return true;
            }
        }
        catch { }
        finally
        {
            IsLoading = false;
        }
        return false;
    }

    [RelayCommand]
    public async Task PlayTrack(Track track)
    {
        if (track == null) return;
        var queue = AllRecentTracks.Count > 0 ? AllRecentTracks : UserTracks;
        int idx = queue.IndexOf(track);
        await _player.PlayTrackAsync(track, queue, idx >= 0 ? idx : 0);
    }
}

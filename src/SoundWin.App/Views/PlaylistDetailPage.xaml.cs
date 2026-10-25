using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.Views;

public sealed partial class PlaylistDetailPage : Page
{
    private readonly SoundCloudApiClient _apiClient;
    private readonly AudioPlayerService _player;
    private readonly CredentialService _credentials;
    private Playlist? _playlist;
    public ObservableCollection<Track> Tracks { get; } = new();

    public PlaylistDetailPage()
    {
        InitializeComponent();
        _apiClient = App.Services.GetRequiredService<SoundCloudApiClient>();
        _player = App.Services.GetRequiredService<AudioPlayerService>();
        _credentials = App.Services.GetRequiredService<CredentialService>();
        TracksListView.ItemsSource = Tracks;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Playlist pl)
        {
            _playlist = pl;
            PopulateHeader(pl);

            TracksLoadingRing.IsActive = true;
            TracksLoadingRing.Visibility = Visibility.Visible;

            try
            {
                if (pl.Tracks.Count == 0 || (pl.TrackCount > pl.Tracks.Count && !string.IsNullOrEmpty(pl.Id)))
                {
                    var full = await _apiClient.GetPlaylistDetailsAsync(pl.Id);
                    if (full.Tracks.Count > 0)
                    {
                        pl.Tracks = full.Tracks;
                        pl.TrackIds = full.TrackIds;
                        if (!string.IsNullOrEmpty(full.Description)) pl.Description = full.Description;
                        if (string.IsNullOrEmpty(pl.Author) && !string.IsNullOrEmpty(full.Author)) pl.Author = full.Author;
                        if (pl.AuthorId == 0 && full.AuthorId != 0) pl.AuthorId = full.AuthorId;
                    }
                    else
                    {
                        await _apiClient.HydratePlaylistTracksAsync(pl);
                    }
                }

                PopulateHeader(pl);
                Tracks.Clear();
                foreach (var t in pl.Tracks)
                {
                    Tracks.Add(t);
                }
            }
            catch { }
            finally
            {
                TracksLoadingRing.IsActive = false;
                TracksLoadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void PopulateHeader(Playlist pl)
    {
        PlaylistTitleText.Text = string.IsNullOrEmpty(pl.Title) ? "Playlist SoundCloud" : pl.Title;
        PlaylistAuthorText.Text = string.IsNullOrEmpty(pl.Author) ? "SoundCloud" : pl.Author;
        int count = Math.Max(pl.TrackCount, pl.Tracks.Count);
        PlaylistCountText.Text = $"{count} {(count > 1 ? "titres" : "titre")}";
        PlaylistTypeBadge.Text = pl.IsSystemPlaylist ? "SÉLECTION SOUNDCLOUD" : "PLAYLIST / ALBUM";

        if (!string.IsNullOrWhiteSpace(pl.Description))
        {
            PlaylistDescriptionText.Text = pl.Description;
            PlaylistDescriptionText.Visibility = Visibility.Visible;
        }
        else
        {
            PlaylistDescriptionText.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrEmpty(pl.DisplayArtworkUrl))
        {
            try
            {
                PlaylistCoverImage.Source = new BitmapImage(new Uri(pl.DisplayArtworkUrl));
            }
            catch { }
        }

        var session = _credentials.LoadSession();
        if (session != null && session.IsAuthenticated && pl.AuthorId > 0 && pl.AuthorId == session.UserId && !pl.IsSystemPlaylist)
        {
            DeletePlaylistButton.Visibility = Visibility.Visible;
        }
        else
        {
            DeletePlaylistButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            MainWindowContent.Current?.NavigateToHome();
        }
    }

    private async void OnPlayAllClicked(object sender, RoutedEventArgs e)
    {
        if (Tracks.Count == 0) return;
        var queue = new ObservableCollection<Track>(Tracks);
        await _player.PlayTrackAsync(Tracks[0], queue, 0);
    }

    private async void OnShufflePlayClicked(object sender, RoutedEventArgs e)
    {
        if (Tracks.Count == 0) return;
        var shuffled = Tracks.OrderBy(_ => Random.Shared.Next()).ToList();
        var queue = new ObservableCollection<Track>(shuffled);
        await _player.PlayTrackAsync(shuffled[0], queue, 0);
    }

    private async void OnTrackPlayRequested(object? sender, Track track)
    {
        if (track == null) return;
        var queue = new ObservableCollection<Track>(Tracks);
        int idx = Tracks.IndexOf(track);
        if (idx < 0) idx = 0;
        await _player.PlayTrackAsync(track, queue, idx);
    }

    private async void OnDeletePlaylistClicked(object sender, RoutedEventArgs e)
    {
        if (_playlist == null || string.IsNullOrEmpty(_playlist.Id)) return;

        var dialog = new ContentDialog
        {
            Title = "Supprimer cette playlist ?",
            Content = $"Voulez-vous vraiment supprimer définitivement la playlist \"{_playlist.Title}\" de votre compte SoundCloud ?",
            PrimaryButtonText = "Supprimer",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            bool deleted = await _apiClient.DeletePlaylistAsync(_playlist.Id);
            if (deleted)
            {
                if (Frame.CanGoBack) Frame.GoBack();
                else MainWindowContent.Current?.NavigateToLibrary();
            }
        }
    }
}

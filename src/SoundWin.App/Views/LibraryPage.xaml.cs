using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;

namespace SoundWin.App.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }
    private string _pendingTabTag = "all";

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string tabTag && !string.IsNullOrWhiteSpace(tabTag))
        {
            _pendingTabTag = tabTag;
            SelectTabByTag(tabTag);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateUiState();
        SelectTabByTag(_pendingTabTag);

        if (ViewModel.IsAuthenticated)
        {
            LikesLoadingProgress.Visibility = Visibility.Visible;
            await ViewModel.LoadAsync();
            LikesLoadingProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectTabByTag(string tag)
    {
        if (LibrarySelectorBar == null) return;
        foreach (var item in LibrarySelectorBar.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                LibrarySelectorBar.SelectedItem = item;
                break;
            }
        }
        ApplyActiveTabVisibility(tag);
    }

    private void OnLibraryTabSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag)
        {
            _pendingTabTag = tag;
            ApplyActiveTabVisibility(tag);
        }
    }

    private void ApplyActiveTabVisibility(string tag)
    {
        if (!ViewModel.IsAuthenticated)
        {
            PlaylistsSection.Visibility = Visibility.Collapsed;
            LikesSection.Visibility = Visibility.Collapsed;
            FollowingSection.Visibility = Visibility.Collapsed;
            HistorySection.Visibility = Visibility.Collapsed;
            SuggestionsSection.Visibility = Visibility.Collapsed;
            return;
        }

        PlaylistsSection.Visibility = (tag is "all" or "playlists" or "albums" or "stations") ? Visibility.Visible : Visibility.Collapsed;
        LikesSection.Visibility = (tag is "all" or "likes") ? Visibility.Visible : Visibility.Collapsed;
        FollowingSection.Visibility = (tag is "all" or "following") ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = (tag is "all" or "history") ? Visibility.Visible : Visibility.Collapsed;
        SuggestionsSection.Visibility = (tag is " suggestions" or "suggestions") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateUiState()
    {
        if (ViewModel.IsAuthenticated && ViewModel.Session != null)
        {
            LoginPromptCard.Visibility = Visibility.Collapsed;
            LibraryHeaderBar.Visibility = Visibility.Visible;
            ApplyActiveTabVisibility(_pendingTabTag);
        }
        else
        {
            LoginPromptCard.Visibility = Visibility.Visible;
            LibraryHeaderBar.Visibility = Visibility.Collapsed;
            ApplyActiveTabVisibility(_pendingTabTag);
        }
    }

    private void OnLoginButtonClick(object sender, RoutedEventArgs e)
    {
        var loginWin = new LoginWindow();
        loginWin.LoginSucceeded += async (_, _) =>
        {
            UpdateUiState();
            LikesLoadingProgress.Visibility = Visibility.Visible;
            await ViewModel.LoadAsync();
            LikesLoadingProgress.Visibility = Visibility.Collapsed;
            UpdateUiState();
        };
        loginWin.Activate();
    }

    private async void OnRefreshLibraryClick(object sender, RoutedEventArgs e)
    {
        LikesLoadingProgress.Visibility = Visibility.Visible;
        await ViewModel.LoadAsync();
        LikesLoadingProgress.Visibility = Visibility.Collapsed;
    }

    private async void OnCreatePlaylistClick(object sender, RoutedEventArgs e)
    {
        var titleBox = new TextBox
        {
            Header = "Nom de la playlist",
            PlaceholderText = "Ex: Favoris Nocturnes..."
        };

        var descBox = new TextBox
        {
            Header = "Description (optionnel)",
            PlaceholderText = "Ajoutez une description pour votre playlist..."
        };

        var privateToggle = new ToggleSwitch
        {
            Header = "Confidentialité",
            OffContent = "Publique",
            OnContent = "Privée",
            IsOn = false
        };

        var panel = new StackPanel { Spacing = 12, Width = 360 };
        panel.Children.Add(titleBox);
        panel.Children.Add(descBox);
        panel.Children.Add(privateToggle);

        var dialog = new ContentDialog
        {
            Title = "Créer une nouvelle playlist SoundCloud",
            Content = panel,
            PrimaryButtonText = "Créer la playlist",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(titleBox.Text))
        {
            string sharing = privateToggle.IsOn ? "private" : "public";
            var created = await ViewModel.CreatePlaylistAsync(titleBox.Text.Trim(), descBox.Text?.Trim(), sharing);
            if (created != null)
            {
                MainWindowContent.Current?.NavigateToPlaylist(created);
            }
        }
    }

    private void OnPlaylistItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist)
        {
            MainWindowContent.Current?.NavigateToPlaylist(playlist);
        }
    }

    private void OnArtistCardItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is User artist && artist.Id > 0)
        {
            MainWindowContent.Current?.NavigateToProfile(artist.Id);
        }
    }

    private async void OnToggleFollowArtistCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: User artist })
        {
            await ViewModel.ToggleFollowArtistAsync(artist);
        }
    }

    private async void OnPlayTrackRequested(object? sender, Track track)
    {
        await ViewModel.PlayTrack(track);
    }
}

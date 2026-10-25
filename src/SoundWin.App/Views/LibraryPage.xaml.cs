using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;

namespace SoundWin.App.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }

    public LibraryPage() { ViewModel = App.Services.GetRequiredService<LibraryViewModel>(); InitializeComponent();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateUiState();
        if (ViewModel.IsAuthenticated)
        {
            LikesLoadingProgress.Visibility = Visibility.Visible;
            await ViewModel.LoadAsync();
            LikesLoadingProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateUiState()
    {
        if (ViewModel.IsAuthenticated && ViewModel.Session != null)
        {
            LoginPromptCard.Visibility = Visibility.Collapsed;
            UserProfileCard.Visibility = Visibility.Visible;
            PlaylistsSection.Visibility = Visibility.Visible;
            LikesSection.Visibility = Visibility.Visible;

            UsernameText.Text = ViewModel.Session.Username;
            UserAvatar.DisplayName = ViewModel.Session.Username;
            if (!string.IsNullOrEmpty(ViewModel.Session.AvatarUrl))
            {
                try
                {
                    UserAvatar.ProfilePicture = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(ViewModel.Session.AvatarUrl));
                }
                catch { }
            }
        }
        else
        {
            LoginPromptCard.Visibility = Visibility.Visible;
            UserProfileCard.Visibility = Visibility.Collapsed;
            PlaylistsSection.Visibility = Visibility.Collapsed;
            LikesSection.Visibility = Visibility.Collapsed;
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
        };
        loginWin.Activate();
    }

    private async void OnRefreshLibraryClick(object sender, RoutedEventArgs e)
    {
        LikesLoadingProgress.Visibility = Visibility.Visible;
        await ViewModel.LoadAsync();
        LikesLoadingProgress.Visibility = Visibility.Collapsed;
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Logout();
        UpdateUiState();
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

    private async void OnPlayTrackRequested(object? sender, Track track)
    {
        await ViewModel.PlayTrack(track);
    }
}

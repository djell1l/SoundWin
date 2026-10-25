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

    public LibraryPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();

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
            LikesSection.Visibility = Visibility.Visible;

            UsernameText.Text = ViewModel.Session.Username;
            UserAvatar.DisplayName = ViewModel.Session.Username;
            if (!string.IsNullOrEmpty(ViewModel.Session.AvatarUrl))
            {
                UserAvatar.ProfilePicture = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(ViewModel.Session.AvatarUrl));
            }
        }
        else
        {
            LoginPromptCard.Visibility = Visibility.Visible;
            UserProfileCard.Visibility = Visibility.Collapsed;
            LikesSection.Visibility = Visibility.Collapsed;
        }
    }

    private void OnLoginButtonClick(object sender, RoutedEventArgs e)
    {
        var loginWin = new LoginWindow();
        loginWin.LoginSucceeded += async (_, session) =>
        {
            UpdateUiState();
            LikesLoadingProgress.Visibility = Visibility.Visible;
            await ViewModel.LoadAsync();
            LikesLoadingProgress.Visibility = Visibility.Collapsed;
        };
        loginWin.Activate();
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Logout();
        UpdateUiState();
    }

    private async void OnPlayTrackRequested(object? sender, Track track)
    {
        await ViewModel.PlayTrack(track);
    }
}

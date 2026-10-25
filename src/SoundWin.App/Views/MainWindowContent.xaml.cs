using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;

namespace SoundWin.App.Views;

public sealed partial class MainWindowContent : UserControl
{
    public static MainWindowContent? Current { get; private set; }

    public PlayerViewModel PlayerVm { get; }
    public MainViewModel MainVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public FrameworkElement TitleBarElement => AppTitleBar;

    public MainWindowContent()
    {
        Current = this;
        InitializeComponent();

        PlayerVm = App.Services.GetRequiredService<PlayerViewModel>();
        MainVm = App.Services.GetRequiredService<MainViewModel>();
        SettingsVm = App.Services.GetRequiredService<SettingsViewModel>();

        // Default page: Home
        MainNavView.SelectedItem = MainNavView.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));
    }

    public void NavigateToPlaylist(Playlist playlist)
    {
        if (playlist == null) return;
        ContentFrame.Navigate(typeof(PlaylistDetailPage), playlist);
    }

    public void NavigateToProfile(long userId)
    {
        ContentFrame.Navigate(typeof(ProfilePage), userId);
    }

    public void NavigateToHome()
    {
        MainNavView.SelectedItem = MainNavView.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));
    }

    public void NavigateToLibrary()
    {
        MainNavView.SelectedItem = MainNavView.MenuItems[4];
        ContentFrame.Navigate(typeof(LibraryPage));
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            switch (tag)
            {
                case "home":
                    ContentFrame.Navigate(typeof(HomePage));
                    break;
                case "stream":
                    ContentFrame.Navigate(typeof(StreamPage));
                    break;
                case "search":
                    ContentFrame.Navigate(typeof(SearchPage));
                    break;
                case "profile":
                    ContentFrame.Navigate(typeof(ProfilePage));
                    break;
                case "library":
                    ContentFrame.Navigate(typeof(LibraryPage));
                    break;
            }
        }
    }

    private void OnTitleDiscordClicked(object sender, RoutedEventArgs e)
    {
        MainNavView.SelectedItem = MainNavView.SettingsItem;
        ContentFrame.Navigate(typeof(SettingsPage));
    }

    private void OnTitleSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.QueryText)) return;

        // Switch to search view and trigger query
        MainNavView.SelectedItem = MainNavView.MenuItems[2];
        ContentFrame.Navigate(typeof(SearchPage));
        if (ContentFrame.Content is SearchPage searchPage)
        {
            searchPage.TriggerSearch(args.QueryText);
        }
    }

    private void OnUserAvatarTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        MainNavView.SelectedItem = MainNavView.MenuItems[3];
        ContentFrame.Navigate(typeof(ProfilePage));
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        PlayerVm.TogglePlayPause();
    }

    private async void OnNextClicked(object sender, RoutedEventArgs e)
    {
        await PlayerVm.Next();
    }

    private async void OnPreviousClicked(object sender, RoutedEventArgs e)
    {
        await PlayerVm.Previous();
    }

    private void OnShuffleClicked(object sender, RoutedEventArgs e)
    {
        PlayerVm.ToggleShuffle();
    }

    private void OnRepeatClicked(object sender, RoutedEventArgs e)
    {
        PlayerVm.ToggleRepeat();
    }

    private async void OnLikeButtonClicked(object sender, RoutedEventArgs e)
    {
        await PlayerVm.ToggleLike();
    }

    private void OnMuteToggleClicked(object sender, RoutedEventArgs e)
    {
        PlayerVm.IsMuted = !PlayerVm.IsMuted;
    }

    private void OnWaveformSeekRequested(object? sender, double ratio)
    {
        PlayerVm.SeekProgress(ratio);
    }
}

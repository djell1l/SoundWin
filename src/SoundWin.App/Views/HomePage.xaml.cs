using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;
using Windows.System;

namespace SoundWin.App.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;

        try
        {
            await ViewModel.LoadAsync();
            ShelvesContainer.ItemsSource = ViewModel.Shelves;
        }
        catch (Exception ex)
        {
            ErrorInfoBar.Message = ex.Message;
            ErrorInfoBar.IsOpen = true;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void OnHorizontalScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // When user scrolls with normal mouse wheel (without Shift) over a horizontal row,
        // forward the vertical scroll directly to the outer MainScrollViewer so scrolling never gets stuck.
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            var props = e.GetCurrentPoint(MainScrollViewer).Properties;
            if (!props.IsHorizontalMouseWheel)
            {
                int delta = props.MouseWheelDelta;
                double targetOffset = Math.Max(0, MainScrollViewer.VerticalOffset - (delta * 0.8));
                MainScrollViewer.ChangeView(null, targetOffset, null, false);
                e.Handled = true;
            }
        }
    }

    private void OnPlaylistCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist playlist })
        {
            MainWindowContent.Current?.NavigateToPlaylist(playlist);
        }
    }

    private async void OnQuickPlayPlaylistClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist playlist })
        {
            await ViewModel.PlayPlaylist(playlist);
        }
    }

    private async void OnTrackCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Track track })
        {
            await ViewModel.PlayTrack(track);
        }
    }

    private async void OnRefreshSidebarClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshSidebarAsync();
    }

    private void OnSuggestedArtistClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SoundWin.Core.Models.User user } && user.Id > 0)
        {
            MainWindowContent.Current?.NavigateToProfile(user.Id);
        }
    }

    private async void OnFollowSuggestedArtistClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SoundWin.Core.Models.User user })
        {
            await ViewModel.FollowUserAsync(user);
        }
    }

    private void OnViewAllLikesClicked(object sender, RoutedEventArgs e)
    {
        MainWindowContent.Current?.NavigateToLibrary();
    }
}

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;

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

    private async void OnPlaylistClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist playlist })
        {
            await ViewModel.PlayPlaylist(playlist);
        }
    }
}

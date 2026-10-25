using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;

namespace SoundWin.App.Views;

public sealed partial class StreamPage : Page
{
    public StreamViewModel ViewModel { get; }

    public StreamPage() { ViewModel = App.Services.GetRequiredService<StreamViewModel>(); InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    private void OnStreamPlaylistClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StreamPost { Playlist: { } pl } })
        {
            MainWindowContent.Current?.NavigateToPlaylist(pl);
        }
    }
}

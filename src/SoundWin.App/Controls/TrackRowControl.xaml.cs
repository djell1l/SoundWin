using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.Controls;

public sealed partial class TrackRowControl : UserControl
{
    private static readonly SolidColorBrush HoverBackground = new(Microsoft.UI.ColorHelper.FromArgb(30, 255, 255, 255));
    private static readonly SolidColorBrush TransparentBackground = new(Microsoft.UI.Colors.Transparent);

    public static readonly DependencyProperty TrackProperty =
        DependencyProperty.Register(nameof(Track), typeof(Track), typeof(TrackRowControl), new PropertyMetadata(null));

    public Track? Track
    {
        get => (Track?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public event EventHandler<Track>? PlayRequested;

    public TrackRowControl()
    {
        InitializeComponent();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        RootBorder.Background = HoverBackground;
        PlayOverlayButton.Opacity = 1;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        RootBorder.Background = TransparentBackground;
        PlayOverlayButton.Opacity = 0;
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e)
    {
        if (Track != null)
        {
            PlayRequested?.Invoke(this, Track);
        }
    }

    private void OnPlayNextClicked(object sender, RoutedEventArgs e)
    {
        if (Track != null)
        {
            var pvm = App.Services.GetService<PlayerViewModel>();
            pvm?.PlayNext(Track);
        }
    }

    private void OnAddToQueueClicked(object sender, RoutedEventArgs e)
    {
        if (Track != null)
        {
            var pvm = App.Services.GetService<PlayerViewModel>();
            pvm?.AddToQueue(Track);
        }
    }

    private void OnStartRadioStationClicked(object sender, RoutedEventArgs e)
    {
        if (Track != null)
        {
            var pvm = App.Services.GetService<PlayerViewModel>();
            _ = pvm?.StartTrackStation(Track);
        }
    }

    private async void OnToggleLikeClicked(object sender, RoutedEventArgs e)
    {
        if (Track == null) return;
        var creds = App.Services.GetService<CredentialService>();
        var api = App.Services.GetService<SoundCloudApiClient>();
        var session = creds?.LoadSession();

        if (session != null && session.IsAuthenticated && api != null)
        {
            Track.IsLiked = !Track.IsLiked;
            if (Track.IsLiked)
            {
                await api.LikeTrackAsync(session.UserId, Track.Id);
            }
            else
            {
                await api.UnlikeTrackAsync(session.UserId, Track.Id);
            }
        }
    }

    private void OnCopyLinkClicked(object sender, RoutedEventArgs e)
    {
        if (Track != null && !string.IsNullOrEmpty(Track.PermalinkUrl))
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(Track.PermalinkUrl);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
    }

    private void OnMoreOptionsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && RootBorder.ContextFlyout is FlyoutBase flyout)
        {
            flyout.ShowAt(btn);
        }
    }
}

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SoundWin.Core.Models;

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
}

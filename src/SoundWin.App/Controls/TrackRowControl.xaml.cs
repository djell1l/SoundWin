using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SoundWin.App.ViewModels;
using SoundWin.App.Views;
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

    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        // Ignore if user tapped one of the action buttons on the right
        if (e.OriginalSource is DependencyObject src)
        {
            var cur = src;
            while (cur != null && cur != RootBorder)
            {
                if (cur == LikeActionBtn || cur == RepostActionBtn || cur == MoreOptionsBtn || cur == PlayOverlayButton)
                {
                    return;
                }
                cur = VisualTreeHelper.GetParent(cur);
            }
        }

        TriggerPlay();
        e.Handled = true;
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e)
    {
        TriggerPlay();
    }

    private async void TriggerPlay()
    {
        if (Track == null) return;

        if (PlayRequested != null)
        {
            PlayRequested.Invoke(this, Track);
        }
        else
        {
            var player = App.Services.GetService<AudioPlayerService>();
            if (player != null)
            {
                await player.PlayTrackAsync(Track);
            }
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

        if (session == null || !session.IsAuthenticated || api == null)
        {
            await PromptLoginIfNeededAsync();
            return;
        }

        bool newState = !Track.IsLiked;
        Track.IsLiked = newState;

        bool ok = newState
            ? await api.LikeTrackAsync(session.UserId, Track.Id)
            : await api.UnlikeTrackAsync(session.UserId, Track.Id);

        if (!ok)
        {
            Track.IsLiked = !newState;
        }
    }

    private async void OnToggleRepostClicked(object sender, RoutedEventArgs e)
    {
        if (Track == null) return;
        var creds = App.Services.GetService<CredentialService>();
        var api = App.Services.GetService<SoundCloudApiClient>();
        var session = creds?.LoadSession();

        if (session == null || !session.IsAuthenticated || api == null)
        {
            await PromptLoginIfNeededAsync();
            return;
        }

        bool newState = !Track.IsReposted;
        Track.IsReposted = newState;

        bool ok = newState
            ? await api.RepostTrackAsync(Track.Id)
            : await api.UnrepostTrackAsync(Track.Id);

        if (!ok)
        {
            Track.IsReposted = !newState;
        }
    }

    private void OnOpenArtistProfileClicked(object sender, RoutedEventArgs e)
    {
        if (Track != null && Track.ArtistId > 0)
        {
            MainWindowContent.Current?.NavigateToProfile(Track.ArtistId);
        }
    }

    private async void OnFollowArtistFromMenuClicked(object sender, RoutedEventArgs e)
    {
        if (Track == null || Track.ArtistId <= 0) return;
        var creds = App.Services.GetService<CredentialService>();
        var api = App.Services.GetService<SoundCloudApiClient>();
        var session = creds?.LoadSession();

        if (session == null || !session.IsAuthenticated || api == null)
        {
            await PromptLoginIfNeededAsync();
            return;
        }

        await api.FollowUserAsync(session.UserId, Track.ArtistId);
    }

    private async void OnOpenCommentsDialogClicked(object sender, RoutedEventArgs e)
    {
        if (Track == null) return;
        var api = App.Services.GetService<SoundCloudApiClient>();
        if (api == null) return;

        var comments = await api.GetTrackCommentsAsync(Track.Id, 60);
        var commentsCollection = new System.Collections.ObjectModel.ObservableCollection<TrackComment>(comments);

        var replyBox = new TextBox
        {
            PlaceholderText = "Écrire un commentaire ou répondre (@pseudo)...",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var list = new ListView
        {
            ItemsSource = commentsCollection,
            SelectionMode = ListViewSelectionMode.None,
            Height = 320
        };

        foreach (var c in commentsCollection)
        {
            // Render clean interactive items
        }

        var itemsStack = new StackPanel { Spacing = 8 };
        void RebuildList()
        {
            itemsStack.Children.Clear();
            foreach (var c in commentsCollection)
            {
                var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(8), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(25, 255, 255, 255)), CornerRadius = new CornerRadius(6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var textStack = new StackPanel { Spacing = 2 };
                textStack.Children.Add(new TextBlock { Text = $"{c.Username} • {c.FormattedTimestamp}", FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 12 });
                textStack.Children.Add(new TextBlock { Text = c.Body, FontSize = 12, TextWrapping = TextWrapping.Wrap });

                var repBtn = new Button { Content = "Répondre", FontSize = 11, Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Top };
                repBtn.Click += (_, _) =>
                {
                    replyBox.Text = $"@{c.Username} ";
                    replyBox.Focus(FocusState.Programmatic);
                    replyBox.SelectionStart = replyBox.Text.Length;
                };

                Grid.SetColumn(textStack, 0);
                Grid.SetColumn(repBtn, 1);
                row.Children.Add(textStack);
                row.Children.Add(repBtn);
                itemsStack.Children.Add(row);
            }
        }
        RebuildList();

        var scroll = new ScrollViewer { Content = itemsStack, Height = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var sendBtn = new Button
        {
            Content = "Publier",
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 85, 0)),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };

        sendBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(replyBox.Text)) return;
            var posted = await api.PostTrackCommentAsync(Track.Id, replyBox.Text.Trim(), 0);
            if (posted != null)
            {
                commentsCollection.Insert(0, posted);
                replyBox.Text = string.Empty;
                RebuildList();
            }
        };

        var inputGrid = new Grid { ColumnSpacing = 8 };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(replyBox, 0);
        Grid.SetColumn(sendBtn, 1);
        inputGrid.Children.Add(replyBox);
        inputGrid.Children.Add(sendBtn);

        var rootPanel = new StackPanel { Width = 460, Spacing = 12 };
        rootPanel.Children.Add(scroll);
        rootPanel.Children.Add(inputGrid);

        var dlg = new ContentDialog
        {
            Title = $"Réactions — {Track.Title}",
            Content = rootPanel,
            CloseButtonText = "Fermer",
            XamlRoot = this.XamlRoot
        };

        await dlg.ShowAsync();
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

    private void OnContextFlyoutOpening(object? sender, object e)
    {
        if (Track == null) return;

        LikeMenuItem.Text = Track.IsLiked ? "Retirer des coups de cœur" : "Ajouter aux coups de cœur";
        RepostMenuItem.Text = Track.IsReposted ? "Retirer le repost" : "Reposter sur mon flux";

        AddToPlaylistSubItem.Items.Clear();

        var newPlItem = new MenuFlyoutItem
        {
            Text = "Nouvelle playlist avec ce titre...",
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        newPlItem.Click += OnCreateNewPlaylistWithTrackClicked;
        AddToPlaylistSubItem.Items.Add(newPlItem);

        var libVm = App.Services.GetService<LibraryViewModel>();
        if (libVm != null && libVm.UserPlaylists.Count > 0)
        {
            AddToPlaylistSubItem.Items.Add(new MenuFlyoutSeparator());
            foreach (var pl in libVm.UserPlaylists)
            {
                var item = new MenuFlyoutItem
                {
                    Text = pl.Title,
                    Tag = pl
                };
                item.Click += OnAddToSpecificPlaylistClicked;
                AddToPlaylistSubItem.Items.Add(item);
            }
        }
    }

    private async void OnAddToSpecificPlaylistClicked(object sender, RoutedEventArgs e)
    {
        if (Track == null || sender is not MenuFlyoutItem { Tag: Playlist pl }) return;
        var api = App.Services.GetService<SoundCloudApiClient>();
        if (api != null)
        {
            await api.AddTrackToPlaylistAsync(pl.Id, Track.Id);
        }
    }

    private async void OnCreateNewPlaylistWithTrackClicked(object sender, RoutedEventArgs e)
    {
        if (Track == null) return;
        var creds = App.Services.GetService<CredentialService>();
        var session = creds?.LoadSession();
        if (session == null || !session.IsAuthenticated)
        {
            await PromptLoginIfNeededAsync();
            return;
        }

        var titleBox = new TextBox
        {
            Header = "Titre de la playlist",
            PlaceholderText = "Ma sélection SoundWin",
            Text = $"Mix - {Track.Title}"
        };

        var descBox = new TextBox
        {
            Header = "Description (optionnel)",
            PlaceholderText = "Description..."
        };

        var privateToggle = new ToggleSwitch
        {
            Header = "Visibilité",
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
            Title = "Créer une nouvelle playlist",
            Content = panel,
            PrimaryButtonText = "Créer et ajouter",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(titleBox.Text))
        {
            var api = App.Services.GetService<SoundCloudApiClient>();
            if (api != null)
            {
                string sharing = privateToggle.IsOn ? "private" : "public";
                await api.CreatePlaylistAsync(titleBox.Text, descBox.Text, sharing, new[] { Track.Id });
            }
        }
    }

    private async System.Threading.Tasks.Task PromptLoginIfNeededAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Connexion SoundCloud requise",
            Content = "Connectez votre compte SoundCloud pour synchroniser vos Likes, Reposts et Playlists en temps réel.",
            PrimaryButtonText = "Se connecter",
            CloseButtonText = "Plus tard",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var res = await dialog.ShowAsync();
        if (res == ContentDialogResult.Primary)
        {
            var loginWin = new LoginWindow();
            loginWin.LoginSucceeded += async (_, _) =>
            {
                var libVm = App.Services.GetService<LibraryViewModel>();
                if (libVm != null) await libVm.LoadAsync();
            };
            loginWin.Activate();
        }
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;
using SoundWin.Core.Services;
using Windows.System;

namespace SoundWin.App.Views;

public sealed partial class MainWindowContent : UserControl
{
    public static MainWindowContent? Current { get; private set; }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;

    public PlayerViewModel PlayerVm { get; }
    public MainViewModel MainVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public FrameworkElement TitleBarElement => AppTitleBar;

    public MainWindowContent()
    {
        Current = this;
        PlayerVm = App.Services.GetRequiredService<PlayerViewModel>();
        MainVm = App.Services.GetRequiredService<MainViewModel>();
        SettingsVm = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();

        // Default page: Home
        MainNavView.SelectedItem = MainNavView.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var settings = App.Services.GetService<SettingsService>();
        if (settings?.Current.EnableStartupIntro ?? true)
        {
            // Play assembled startup sound via Win32 PlaySound
            var wavPath = Path.Combine(AppContext.BaseDirectory, "Assets", "StartupIntro.wav");
            if (File.Exists(wavPath))
            {
                PlaySound(wavPath, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
            }

            try
            {
                StartupIntroStoryboard.Begin();
            }
            catch
            {
                IntroOverlay.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            IntroOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnIntroOverlayTapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            StartupIntroStoryboard.Stop();
        }
        catch { }
        IntroOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnIntroStoryboardCompleted(object? sender, object e)
    {
        IntroOverlay.Visibility = Visibility.Collapsed;
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

    public void NavigateToLibrary(string? tabTag = null)
    {
        MainNavView.SelectedItem = MainNavView.MenuItems[4];
        ContentFrame.Navigate(typeof(LibraryPage), tabTag);
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

    private void OnMenuProfileClicked(object sender, RoutedEventArgs e)
    {
        MainNavView.SelectedItem = MainNavView.MenuItems[3];
        ContentFrame.Navigate(typeof(ProfilePage));
    }

    private void OnMenuLikesClicked(object sender, RoutedEventArgs e)
    {
        NavigateToLibrary("likes");
    }

    private void OnMenuPlaylistsClicked(object sender, RoutedEventArgs e)
    {
        NavigateToLibrary("playlists");
    }

    private void OnMenuStationsClicked(object sender, RoutedEventArgs e)
    {
        NavigateToLibrary("stations");
    }

    private void OnMenuFollowingsClicked(object sender, RoutedEventArgs e)
    {
        NavigateToLibrary("following");
    }

    private void OnMenuSuggestionsClicked(object sender, RoutedEventArgs e)
    {
        NavigateToLibrary("suggestions");
    }

    private void OnMenuSettingsClicked(object sender, RoutedEventArgs e)
    {
        MainNavView.SelectedItem = MainNavView.SettingsItem;
        ContentFrame.Navigate(typeof(SettingsPage));
    }

    private void OnMenuLogoutClicked(object sender, RoutedEventArgs e)
    {
        SettingsVm.Logout();
        MainVm.RefreshSession();
        MainNavView.SelectedItem = MainNavView.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));
    }

    private void OnDownloadUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(MainVm.UpdateBannerUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = MainVm.UpdateBannerUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
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

    // ==========================================
    // REACTIONS & COMMENTS HANDLERS
    // ==========================================
    private void OnReplyToCommentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackComment comment })
        {
            if (PlayerVm.DurationMs > 0)
            {
                double ratio = Math.Clamp(comment.TimestampMs / PlayerVm.DurationMs, 0.0, 1.0);
                PlayerVm.SeekProgress(ratio);
            }
            NewCommentTextBox.Text = $"@{comment.Username} ";
            NewCommentTextBox.Focus(FocusState.Programmatic);
            NewCommentTextBox.SelectionStart = NewCommentTextBox.Text.Length;
        }
    }

    private async void OnQuickReactionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string reactionText } && !string.IsNullOrWhiteSpace(reactionText))
        {
            await PlayerVm.PostComment(reactionText);
        }
    }

    private async void OnSendCommentClicked(object sender, RoutedEventArgs e)
    {
        await SubmitNewCommentAsync();
    }

    private async void OnNewCommentKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SubmitNewCommentAsync();
        }
    }

    private async System.Threading.Tasks.Task SubmitNewCommentAsync()
    {
        var text = NewCommentTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        NewCommentTextBox.Text = string.Empty;
        await PlayerVm.PostComment(text);
    }
}

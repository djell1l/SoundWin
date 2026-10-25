using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;
using SoundWin.Core.Services;

namespace SoundWin.App.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }

    public SearchPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();

        Loaded += async (_, _) => await ViewModel.EnsureRecentsLoadedAsync();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.IsSearching))
            {
                SearchProgress.Visibility = ViewModel.IsSearching ? Visibility.Visible : Visibility.Collapsed;
            }
        };
    }

    public void TriggerSearch(string query)
    {
        SearchBox.Text = query;
        _ = ViewModel.PerformSearchAsync(query);
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchQuery = sender.Text;
            if (string.IsNullOrWhiteSpace(sender.Text))
            {
                _ = ViewModel.PerformSearchAsync("");
            }
            else
            {
                sender.ItemsSource = ViewModel.Suggestions;
            }
        }
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _ = ViewModel.PerformSearchAsync(args.QueryText);
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string query)
        {
            _ = ViewModel.PerformSearchAsync(query);
        }
    }

    private void OnSearchButtonClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.PerformSearchAsync(SearchBox.Text);
    }

    private void OnRecentQueryChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string q })
        {
            TriggerSearch(q);
        }
    }

    private void OnClearRecentsClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAllRecents();
    }

    private async void OnRecentTrackItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            await ViewModel.PlayTrack(track);
        }
    }

    private void OnRemoveRecentTrackClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Track track })
        {
            ViewModel.RemoveRecentSearchedTrack(track);
        }
    }

    private async void OnPlayTrackRequested(object? sender, Track track)
    {
        await ViewModel.PlayTrack(track);
    }

    private void OnPlaylistItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist)
        {
            MainWindowContent.Current?.NavigateToPlaylist(playlist);
        }
    }

    private void OnUserItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is User user && user.Id > 0)
        {
            MainWindowContent.Current?.NavigateToProfile(user.Id);
        }
    }

    private async void OnFollowArtistResultClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: User user }) return;

        var creds = App.Services.GetService<CredentialService>();
        var api = App.Services.GetService<SoundCloudApiClient>();
        var session = creds?.LoadSession();
        if (session == null || !session.IsAuthenticated || api == null) return;

        bool newState = !user.IsFollowed;
        user.IsFollowed = newState;

        bool ok = newState
            ? await api.FollowUserAsync(session.UserId, user.Id)
            : await api.UnfollowUserAsync(session.UserId, user.Id);

        if (!ok)
        {
            user.IsFollowed = !newState;
        }
    }
}

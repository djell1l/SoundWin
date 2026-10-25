using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;

namespace SoundWin.App.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }

    public SearchPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();

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
            sender.ItemsSource = ViewModel.Suggestions;
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

    private async void OnTrackItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            await ViewModel.PlayTrack(track);
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
}

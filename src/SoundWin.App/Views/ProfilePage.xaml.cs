using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SoundWin.App.Views;

public sealed partial class ProfilePage : Page
{
    public ProfileViewModel Vm { get; }

    public ProfilePage() { Vm = App.Services.GetRequiredService<ProfileViewModel>(); InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            long? userId = e.Parameter is long id ? id : null;
            await Vm.InitializeAsync(userId);
        }
        catch (Exception ex)
        {
            App.Trace($"ProfilePage OnNavigatedTo error: {ex}");
        }
    }

    private void OnAvatarAreaPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (Vm.IsOwnProfile)
        {
            UpdateAvatarOverlayBtn.Visibility = Visibility.Visible;
        }
    }

    private void OnAvatarAreaPointerExited(object sender, PointerRoutedEventArgs e)
    {
        UpdateAvatarOverlayBtn.Visibility = Visibility.Collapsed;
    }

    private void OnSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is not string tag) return;

        switch (tag)
        {
            case "all":
                PinnedTracksSection.Visibility = Visibility.Visible;
                ActiveSectionTitle.Text = "Récent";
                MainTracksListView.Visibility = Visibility.Visible;
                PlaylistsDetailedList.Visibility = Visibility.Collapsed;
                MainTracksListView.ItemsSource = Vm.AllRecentTracks;
                break;

            case "popular":
                PinnedTracksSection.Visibility = Visibility.Collapsed;
                ActiveSectionTitle.Text = "Titres populaires";
                MainTracksListView.Visibility = Visibility.Visible;
                PlaylistsDetailedList.Visibility = Visibility.Collapsed;
                MainTracksListView.ItemsSource = Vm.PopularTracks;
                break;

            case "tracks":
                PinnedTracksSection.Visibility = Visibility.Collapsed;
                ActiveSectionTitle.Text = "Titres publiés";
                MainTracksListView.Visibility = Visibility.Visible;
                PlaylistsDetailedList.Visibility = Visibility.Collapsed;
                MainTracksListView.ItemsSource = Vm.UserTracks;
                break;

            case "playlists":
                PinnedTracksSection.Visibility = Visibility.Collapsed;
                ActiveSectionTitle.Text = "Playlists";
                MainTracksListView.Visibility = Visibility.Collapsed;
                PlaylistsDetailedList.Visibility = Visibility.Visible;
                break;

            case "reposts":
                PinnedTracksSection.Visibility = Visibility.Collapsed;
                ActiveSectionTitle.Text = "Reposts";
                MainTracksListView.Visibility = Visibility.Visible;
                PlaylistsDetailedList.Visibility = Visibility.Collapsed;
                MainTracksListView.ItemsSource = Vm.RepostedTracks;
                break;

            case "likes":
                PinnedTracksSection.Visibility = Visibility.Collapsed;
                ActiveSectionTitle.Text = "Mentions J'aime";
                MainTracksListView.Visibility = Visibility.Visible;
                PlaylistsDetailedList.Visibility = Visibility.Collapsed;
                MainTracksListView.ItemsSource = Vm.LikedTracks;
                break;
        }
    }

    private void OnShowAllLikesClicked(object sender, RoutedEventArgs e)
    {
        foreach (var item in ProfileSelectorBar.Items)
        {
            if (item.Tag as string == "likes")
            {
                ProfileSelectorBar.SelectedItem = item;
                break;
            }
        }
    }

    private async void OnTrackPlayRequested(object? sender, Track track)
    {
        if (track != null)
        {
            await Vm.PlayTrack(track);
        }
    }

    private async void OnSidebarTrackClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Track track })
        {
            await Vm.PlayTrack(track);
        }
    }

    private void OnOpenPlaylistDetailClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist pl })
        {
            MainWindowContent.Current?.NavigateToPlaylist(pl);
        }
    }

    private async void OnPlayPlaylistDirectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Playlist pl })
        {
            var homeVm = App.Services.GetRequiredService<HomeViewModel>();
            await homeVm.PlayPlaylist(pl);
        }
    }

    private void OnFollowingUserClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: User u } && u.Id > 0)
        {
            MainWindowContent.Current?.NavigateToProfile(u.Id);
        }
    }

    private async void OnToggleFollowProfileClicked(object sender, RoutedEventArgs e)
    {
        if (Vm.User == null) return;
        var creds = App.Services.GetService<SoundWin.Core.Services.CredentialService>();
        var api = App.Services.GetService<SoundWin.Core.Services.SoundCloudApiClient>();
        var session = creds?.LoadSession();
        if (session == null || !session.IsAuthenticated || api == null) return;

        bool newState = !Vm.User.IsFollowed;
        Vm.User.IsFollowed = newState;

        bool ok = newState
            ? await api.FollowUserAsync(session.UserId, Vm.User.Id)
            : await api.UnfollowUserAsync(session.UserId, Vm.User.Id);

        if (!ok)
        {
            Vm.User.IsFollowed = !newState;
        }
    }

    private void OnShareProfileClicked(object sender, RoutedEventArgs e)
    {
        if (Vm.User?.PermalinkUrl != null)
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(Vm.User.PermalinkUrl);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);

            ProfileStatusInfoBar.Severity = InfoBarSeverity.Success;
            ProfileStatusInfoBar.Title = "Lien copié";
            ProfileStatusInfoBar.Message = $"Le lien du profil ({Vm.User.PermalinkUrl}) a été copié dans le presse-papiers.";
            ProfileStatusInfoBar.IsOpen = true;
        }
    }

    private async void OnUploadAvatarClicked(object sender, RoutedEventArgs e)
    {
        var path = await PickImageFileAsync();
        if (!string.IsNullOrEmpty(path))
        {
            bool ok = await Vm.UploadAvatarFromFileAsync(path);
            ProfileStatusInfoBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            ProfileStatusInfoBar.Title = ok ? "Photo de profil mise à jour" : "Mise à jour de l'avatar";
            ProfileStatusInfoBar.Message = ok
                ? "Votre nouvelle photo de profil a été synchronisée avec SoundCloud."
                : "L'API SoundCloud a restreint le téléversement direct. Réessayez avec une image JPG/PNG carrée (< 2 Mo).";
            ProfileStatusInfoBar.IsOpen = true;
        }
    }

    private async void OnUploadHeaderBannerClicked(object sender, RoutedEventArgs e)
    {
        var path = await PickImageFileAsync();
        if (!string.IsNullOrEmpty(path))
        {
            bool ok = await Vm.UploadBannerFromFileAsync(path);
            ProfileStatusInfoBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            ProfileStatusInfoBar.Title = ok ? "Bannière d'en-tête mise à jour" : "Mise à jour de la bannière";
            ProfileStatusInfoBar.Message = ok
                ? "Votre nouvelle image d'en-tête a été envoyée sur SoundCloud."
                : "Impossible de modifier l'en-tête via cette session. Vérifiez le format de l'image (JPG/PNG).";
            ProfileStatusInfoBar.IsOpen = true;
        }
    }

    private async Task<string?> PickImageFileAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            if (MainWindow.CurrentWindowHandle != IntPtr.Zero)
            {
                InitializeWithWindow.Initialize(picker, MainWindow.CurrentWindowHandle);
            }

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch
        {
            return null;
        }
    }

    // =========================================================
    // EXACT SOUNDCLOUD "MODIFIER VOTRE PROFIL" MODAL (Screenshot #5)
    // =========================================================
    private async void OnEditProfileModalClicked(object sender, RoutedEventArgs e)
    {
        if (Vm.User == null) return;
        Vm.PopulateEditFieldsFromUser(Vm.User);

        var rootGrid = new Grid
        {
            Width = 560,
            ColumnSpacing = 24
        };
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left Column: Circular Avatar Preview + "Mettre à jour l'image" + "Changer l'image d'en-tête"
        var leftStack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        var avatarPreview = new PersonPicture
        {
            Width = 148,
            Height = 148,
            DisplayName = Vm.User.Username
        };
        if (!string.IsNullOrEmpty(Vm.User.HighResAvatarUrl))
        {
            try
            {
                avatarPreview.ProfilePicture = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Vm.User.HighResAvatarUrl));
            }
            catch { }
        }

        var changeAvatarBtn = new Button
        {
            Content = "Mettre à jour l'image",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        changeAvatarBtn.Click += async (_, _) =>
        {
            var path = await PickImageFileAsync();
            if (!string.IsNullOrEmpty(path))
            {
                await Vm.UploadAvatarFromFileAsync(path);
                if (!string.IsNullOrEmpty(Vm.User?.HighResAvatarUrl))
                {
                    try
                    {
                        avatarPreview.ProfilePicture = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Vm.User.HighResAvatarUrl));
                    }
                    catch { }
                }
            }
        };

        var changeHeaderBtn = new Button
        {
            Content = "Image d'en-tête...",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        changeHeaderBtn.Click += async (_, _) =>
        {
            var path = await PickImageFileAsync();
            if (!string.IsNullOrEmpty(path))
            {
                await Vm.UploadBannerFromFileAsync(path);
            }
        };

        leftStack.Children.Add(avatarPreview);
        leftStack.Children.Add(changeAvatarBtn);
        leftStack.Children.Add(changeHeaderBtn);
        Grid.SetColumn(leftStack, 0);
        rootGrid.Children.Add(leftStack);

        // Right Column: Form Fields matching SoundCloud's Edit Modal
        var rightStack = new StackPanel { Spacing = 12 };

        var pseudoBox = new TextBox
        {
            Header = "Pseudonyme *",
            Text = Vm.EditUsername
        };

        var permalinkBox = new TextBox
        {
            Header = "URL de profil (soundcloud.com/...)",
            Text = Vm.EditPermalink
        };

        var namesGrid = new Grid { ColumnSpacing = 12 };
        namesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        namesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var firstNameBox = new TextBox { Header = "Prénom", Text = Vm.EditFirstName };
        var lastNameBox = new TextBox { Header = "Nom", Text = Vm.EditLastName };
        Grid.SetColumn(firstNameBox, 0);
        Grid.SetColumn(lastNameBox, 1);
        namesGrid.Children.Add(firstNameBox);
        namesGrid.Children.Add(lastNameBox);

        var locationGrid = new Grid { ColumnSpacing = 12 };
        locationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        locationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cityBox = new TextBox { Header = "Ville", Text = Vm.EditCity };
        var countryBox = new TextBox { Header = "Pays (ex: FR)", Text = Vm.EditCountry };
        Grid.SetColumn(cityBox, 0);
        Grid.SetColumn(countryBox, 1);
        locationGrid.Children.Add(cityBox);
        locationGrid.Children.Add(countryBox);

        var bioBox = new TextBox
        {
            Header = "À propos de vous",
            PlaceholderText = "Décrivez-vous en quelques mots seulement pour vous présenter au monde entier.",
            Text = Vm.EditDescription,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 76
        };

        // Links Section ("Vos liens" + "Ajouter un lien")
        var linksSection = new StackPanel { Spacing = 8 };
        linksSection.Children.Add(new TextBlock { Text = "Vos liens", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var linksRowsPanel = new StackPanel { Spacing = 6 };
        foreach (var link in Vm.WebLinks)
        {
            var row = CreateLinkEditorRow(link.Title, link.Url, linksRowsPanel);
            linksRowsPanel.Children.Add(row);
        }

        var addLinkBtn = new Button
        {
            Content = "Ajouter un lien",
            FontSize = 12
        };
        addLinkBtn.Click += (_, _) =>
        {
            linksRowsPanel.Children.Add(CreateLinkEditorRow("", "", linksRowsPanel));
        };

        linksSection.Children.Add(linksRowsPanel);
        linksSection.Children.Add(addLinkBtn);

        rightStack.Children.Add(pseudoBox);
        rightStack.Children.Add(permalinkBox);
        rightStack.Children.Add(namesGrid);
        rightStack.Children.Add(locationGrid);
        rightStack.Children.Add(bioBox);
        rightStack.Children.Add(linksSection);

        Grid.SetColumn(rightStack, 1);
        rootGrid.Children.Add(rightStack);

        var scroll = new ScrollViewer
        {
            Content = rootGrid,
            MaxHeight = 520,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var dialog = new ContentDialog
        {
            Title = "Modifier votre profil",
            Content = scroll,
            PrimaryButtonText = "Sauvegarder les modifications",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var res = await dialog.ShowAsync();
        if (res == ContentDialogResult.Primary)
        {
            Vm.EditUsername = pseudoBox.Text;
            Vm.EditPermalink = permalinkBox.Text;
            Vm.EditFirstName = firstNameBox.Text;
            Vm.EditLastName = lastNameBox.Text;
            Vm.EditCity = cityBox.Text;
            Vm.EditCountry = countryBox.Text;
            Vm.EditDescription = bioBox.Text;

            // Collect links from editor rows
            Vm.WebLinks.Clear();
            foreach (var child in linksRowsPanel.Children)
            {
                if (child is Grid g && g.Children.Count >= 2 &&
                    g.Children[0] is TextBox tBox &&
                    g.Children[1] is TextBox uBox &&
                    !string.IsNullOrWhiteSpace(uBox.Text))
                {
                    Vm.WebLinks.Add(new WebProfileLink
                    {
                        Title = string.IsNullOrWhiteSpace(tBox.Text) ? uBox.Text.Trim() : tBox.Text.Trim(),
                        Url = uBox.Text.Trim()
                    });
                }
            }

            bool saved = await Vm.SaveFullProfileAsync();
            ProfileStatusInfoBar.Severity = saved ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            ProfileStatusInfoBar.Title = saved ? "Profil mis à jour" : "Mise à jour partielle";
            ProfileStatusInfoBar.Message = Vm.StatusMessage;
            ProfileStatusInfoBar.IsOpen = true;
        }
    }

    private static Grid CreateLinkEditorRow(string title, string url, StackPanel parentPanel)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBox = new TextBox { PlaceholderText = "Titre", Text = title };
        var urlBox = new TextBox { PlaceholderText = "https://...", Text = url };
        var delBtn = new Button { Content = "X", Padding = new Thickness(8, 4, 8, 4) };
        delBtn.Click += (_, _) => parentPanel.Children.Remove(row);

        Grid.SetColumn(titleBox, 0);
        Grid.SetColumn(urlBox, 1);
        Grid.SetColumn(delBtn, 2);

        row.Children.Add(titleBox);
        row.Children.Add(urlBox);
        row.Children.Add(delBtn);
        return row;
    }
}

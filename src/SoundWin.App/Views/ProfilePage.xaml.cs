using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SoundWin.App.ViewModels;
using SoundWin.Core.Models;

namespace SoundWin.App.Views;

public sealed partial class ProfilePage : Page
{
    public ProfileViewModel Vm { get; }

    public ProfilePage()
    {
        InitializeComponent();
        Vm = App.Services.GetRequiredService<ProfileViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        long? userId = e.Parameter is long id ? id : null;
        await Vm.InitializeAsync(userId);
    }

    private async void OnTrackClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            await Vm.PlayTrack(track);
        }
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }
}
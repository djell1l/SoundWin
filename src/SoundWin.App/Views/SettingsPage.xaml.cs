using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SoundWin.App.ViewModels;

namespace SoundWin.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel Vm { get; }

    public SettingsPage() { Vm = App.Services.GetRequiredService<SettingsViewModel>(); InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Vm.RefreshUserSession();
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundWin.App.ViewModels;

namespace SoundWin.App.Views;

public sealed partial class StreamPage : Page
{
    public StreamViewModel ViewModel { get; }

    public StreamPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<StreamViewModel>();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }
}

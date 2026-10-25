using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SoundWin.App.ViewModels;
using SoundWin.App.Views;
using SoundWin.Core.Services;

namespace SoundWin.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        Trace("App() constructor started");
        InitializeComponent();
        Trace("InitializeComponent() completed");

        UnhandledException += (s, e) =>
        {
            var log = $"[UnhandledException] {e.Message}\n{e.Exception}\n";
            Trace(log);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var log = $"[AppDomain] {e.ExceptionObject}\n";
            Trace(log);
        };

        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dq != null)
        {
            SoundWin.Core.Utils.AppDispatcher.RunOnUiThread = action =>
            {
                if (dq.HasThreadAccess) action();
                else dq.TryEnqueue(() => action());
            };
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        Trace("Services built");
    }

    internal static void Trace(string msg)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            System.IO.File.AppendAllText(System.IO.Path.Combine(desktop, "SoundWin_debug.txt"), $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<SoundCloudApiClient>();
        services.AddSingleton<AudioPlayerService>();
        services.AddSingleton<DiscordRpcService>();
        services.AddSingleton<CredentialService>();
        services.AddSingleton<SoundWinBridgeServer>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<LibraryViewModel>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Trace("OnLaunched started");
        try
        {
            var credentials = Services.GetRequiredService<CredentialService>();
            var apiClient = Services.GetRequiredService<SoundCloudApiClient>();
            var rpc = Services.GetRequiredService<DiscordRpcService>();
            var bridge = Services.GetRequiredService<SoundWinBridgeServer>();

            var session = credentials.LoadSession();
            if (session != null && !string.IsNullOrEmpty(session.OAuthToken))
            {
                apiClient.OAuthToken = session.OAuthToken;
            }

            // Start Discord Rich Presence in background
            _ = rpc.StartAsync();

            Trace("Creating MainWindow...");
            var window = new MainWindow();
            MainWindow = window;

            var windowQueue = window.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (windowQueue != null)
            {
                SoundWin.Core.Utils.AppDispatcher.RunOnUiThread = action =>
                {
                    if (windowQueue.HasThreadAccess) action();
                    else windowQueue.TryEnqueue(() => action());
                };
            }

            SoundWinBridgeServer.BringToFrontHandler = () =>
            {
                window.Activate();
            };

            // Start Jay Ecosystem Bridge (Diswin IPC)
            _ = bridge.StartAsync();

            Trace("Activating MainWindow...");
            window.Activate();
            Trace("MainWindow Activated!");
        }
        catch (Exception ex)
        {
            Trace($"[OnLaunched Exception] {ex}");
            throw;
        }
    }
}

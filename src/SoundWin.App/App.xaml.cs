using System;
using System.Threading.Tasks;
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
        services.AddSingleton<SettingsService>();
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
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ProfileViewModel>();
        services.AddSingleton<StreamViewModel>();
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
                _ = apiClient.RefreshLikedTrackIdsAsync(session.UserId);
                var libVm = Services.GetRequiredService<LibraryViewModel>();
                _ = libVm.LoadAsync();
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

            // Restore last played track state (without auto-starting audio)
            var settings = Services.GetRequiredService<SettingsService>();
            if (settings.Current.LastPlayedTrackId > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var tracks = await apiClient.GetTracksByIdsAsync(new[] { settings.Current.LastPlayedTrackId });
                        var track = System.Linq.Enumerable.FirstOrDefault(tracks);
                        if (track != null)
                        {
                            var playerVm = Services.GetRequiredService<PlayerViewModel>();
                            var pos = TimeSpan.FromSeconds(settings.Current.LastPlayedPositionSeconds);
                            SoundWin.Core.Utils.AppDispatcher.Dispatch(() =>
                            {
                                playerVm.CurrentTrack = track;
                                playerVm.Position = pos;
                            });
                        }
                    }
                    catch { }
                });
            }

            // Optional auto-check updates on startup (non-blocking banner)
            if (settings.Current.AutoCheckUpdatesOnStartup)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(3000); // Wait for UI to settle
                        var update = await SoundWin.App.Services.UpdateService.CheckForUpdatesAsync(settings.Current.UpdateChannel);
                        if (update != null && !string.IsNullOrEmpty(update.TagName))
                        {
                            var mainVm = Services.GetRequiredService<MainViewModel>();
                            SoundWin.Core.Utils.AppDispatcher.Dispatch(() =>
                            {
                                mainVm.ShowUpdateBanner(update.TagName, update.HtmlUrl ?? "https://github.com/djell1l/SoundWin/releases", settings.Current.UpdateChannel);
                            });
                        }
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            Trace($"[OnLaunched Exception] {ex}");
            throw;
        }
    }
}

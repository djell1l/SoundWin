using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SoundWin.App.Services;
using SoundWin.App.ViewModels;
using SoundWin.Core.Services;
using WinRT.Interop;

namespace SoundWin.App.Views;

public sealed partial class MainWindow : Window
{
    public static IntPtr CurrentWindowHandle { get; private set; }
    private IntPtr _hWnd;
    private AppWindow? _appWindow;
    private bool _isRealExit;

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);
    private SUBCLASSPROC? _subclassDelegate;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

    public MainWindow()
    {
        App.Trace("MainWindow() constructor started");
        InitializeComponent();
        App.Trace("MainWindow InitializeComponent() done");

        Title = "SCwin";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(RootContent.TitleBarElement);

        _hWnd = WindowNative.GetWindowHandle(this);
        CurrentWindowHandle = _hWnd;
        var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));

        SetWindowIcon();
        SetupTrayAndClosing();

        // Ensure window is shown and brought to foreground
        ShowWindow(_hWnd, 5); // SW_SHOW
        SetForegroundWindow(_hWnd);
        App.Trace("MainWindow() constructor completed");
    }

    private void SetupTrayAndClosing()
    {
        TrayIconService.Initialize(_hWnd);

        if (_appWindow != null)
        {
            _appWindow.Closing += (s, e) =>
            {
                if (!_isRealExit)
                {
                    var settings = App.Services.GetService<SettingsService>();
                    SaveCurrentPlayerState();

                    if (settings?.Current.MinimizeToTrayOnClose ?? true)
                    {
                        e.Cancel = true;
                        _appWindow.Hide();
                    }
                    else
                    {
                        ExitApplication();
                    }
                }
            };
        }

        // Subclass window safely using comctl32 SetWindowSubclass to receive TrayIcon callbacks
        _subclassDelegate = new SUBCLASSPROC(SubclassProc);
        SetWindowSubclass(_hWnd, _subclassDelegate, new UIntPtr(101), UIntPtr.Zero);
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == TrayIconService.WM_TRAYICON)
        {
            int action = (int)lParam & 0xFFFF;
            if (action == TrayIconService.WM_LBUTTONUP || action == 0x0203 /* WM_LBUTTONDBLCLK */)
            {
                DispatcherQueue.TryEnqueue(ShowAndActivate);
            }
            else if (action == TrayIconService.WM_RBUTTONUP || action == 0x007B /* WM_CONTEXTMENU */)
            {
                DispatcherQueue.TryEnqueue(ShowTrayContextMenu);
            }
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowTrayContextMenu()
    {
        try
        {
            GetCursorPos(out var pt);

            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            const uint MF_STRING = 0x00000000;
            const uint MF_SEPARATOR = 0x00000800;
            const uint TPM_RETURNCMD = 0x0100;
            const uint TPM_RIGHTBUTTON = 0x0002;

            AppendMenuW(hMenu, MF_STRING, 1001, "Afficher SCwin");
            AppendMenuW(hMenu, MF_STRING, 1002, "Lecture / Pause");
            AppendMenuW(hMenu, MF_STRING, 1003, "Titre suivant");
            AppendMenuW(hMenu, MF_SEPARATOR, 0, string.Empty);
            AppendMenuW(hMenu, MF_STRING, 1004, "Quitter");

            SetForegroundWindow(_hWnd);
            uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, _hWnd, IntPtr.Zero);
            DestroyMenu(hMenu);

            if (cmd == 1001)
            {
                ShowAndActivate();
            }
            else if (cmd == 1002)
            {
                var playerVm = App.Services.GetService<PlayerViewModel>();
                playerVm?.TogglePlayPause();
            }
            else if (cmd == 1003)
            {
                var playerVm = App.Services.GetService<PlayerViewModel>();
                _ = playerVm?.Next();
            }
            else if (cmd == 1004)
            {
                ExitApplication();
            }
        }
        catch { }
    }

    public void ShowAndActivate()
    {
        if (_appWindow != null)
        {
            _appWindow.Show();
        }
        Activate();
        ShowWindow(_hWnd, 9); // SW_RESTORE
        SetForegroundWindow(_hWnd);
    }

    public void ExitApplication()
    {
        _isRealExit = true;
        SaveCurrentPlayerState();
        if (_subclassDelegate != null)
        {
            RemoveWindowSubclass(_hWnd, _subclassDelegate, new UIntPtr(101));
        }
        TrayIconService.Remove();
        Close();
    }

    public void SaveCurrentPlayerState()
    {
        try
        {
            var playerVm = App.Services.GetService<PlayerViewModel>();
            var settings = App.Services.GetService<SettingsService>();
            if (playerVm?.CurrentTrack != null && settings != null)
            {
                settings.Current.LastPlayedTrackId = playerVm.CurrentTrack.Id;
                settings.Current.LastPlayedPositionSeconds = playerVm.Position.TotalSeconds;
                settings.Save();
            }
        }
        catch { }
    }

    private void SetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            }

            if (File.Exists(iconPath))
            {
                _appWindow?.SetIcon(iconPath);
                ApplyWin32WindowIcon(_hWnd, iconPath);
            }
        }
        catch { }
    }

    internal static void ApplyWin32WindowIcon(IntPtr hWnd, string iconPath)
    {
        try
        {
            const uint IMAGE_ICON = 1;
            const uint LR_LOADFROMFILE = 0x00000010;
            const uint WM_SETICON = 0x0080;
            IntPtr hIconSmall = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            IntPtr hIconBig = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 48, 48, LR_LOADFROMFILE);
            if (hIconSmall != IntPtr.Zero) SendMessageW(hWnd, WM_SETICON, (IntPtr)0, hIconSmall);
            if (hIconBig != IntPtr.Zero) SendMessageW(hWnd, WM_SETICON, (IntPtr)1, hIconBig);
        }
        catch { }
    }
}

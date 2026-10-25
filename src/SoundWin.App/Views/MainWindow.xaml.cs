using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace SoundWin.App.Views;

public sealed partial class MainWindow : Window
{
    public static IntPtr CurrentWindowHandle { get; private set; }
    private IntPtr _hWnd;
    private AppWindow? _appWindow;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public MainWindow()
    {
        App.Trace("MainWindow() constructor started");
        InitializeComponent();
        App.Trace("MainWindow InitializeComponent() done");

        Title = "SoundWin";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(RootContent.TitleBarElement);

        _hWnd = WindowNative.GetWindowHandle(this);
        CurrentWindowHandle = _hWnd;
        var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(1150, 780));

        SetWindowIcon();

        // Ensure window is shown and brought to foreground
        ShowWindow(_hWnd, 5); // SW_SHOW
        SetForegroundWindow(_hWnd);
        App.Trace("MainWindow() constructor completed");
    }

    private void SetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
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

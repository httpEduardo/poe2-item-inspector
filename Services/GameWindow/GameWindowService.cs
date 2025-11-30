using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PoE2Inspector.Services.Logging;

namespace PoE2Inspector.Services.GameWindow;

public class GameWindowService : IGameWindowService
{
    private readonly ILogService _log;
    private readonly DispatcherTimer _timer;
    private IntPtr? _cachedHwnd;
    private Rect? _lastRect;

    public event Action<Rect>? GameWindowRectChanged;

    public GameWindowService(ILogService log)
    {
        _log = log;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += OnTimerTick;
    }

    public void StartMonitoring()
    {
        _timer.Start();
        _log.Info("GameWindowService monitoring started");
    }

    public void StopMonitoring()
    {
        _timer.Stop();
        _log.Info("GameWindowService monitoring stopped");
    }

    public IntPtr? GetGameWindowHandle()
    {
        if (_cachedHwnd.HasValue && IsWindow(_cachedHwnd.Value))
        {
            return _cachedHwnd;
        }

        IntPtr hwnd = FindWindow(null, "Path of Exile 2");
        if (hwnd == IntPtr.Zero)
        {
            hwnd = FindWindowByTitle("Path of Exile 2");
        }

        if (hwnd != IntPtr.Zero)
        {
            _cachedHwnd = hwnd;
            _log.Info($"Found PoE2 window: {hwnd}");
        }

        return hwnd == IntPtr.Zero ? null : hwnd;
    }

    public bool IsGameInForeground()
    {
        var hwnd = GetGameWindowHandle();
        if (!hwnd.HasValue) return false;

        var foreground = GetForegroundWindow();
        return foreground == hwnd.Value;
    }

    public Rect? GetGameWindowRect()
    {
        var hwnd = GetGameWindowHandle();
        if (!hwnd.HasValue) return null;

        if (GetWindowRect(hwnd.Value, out RECT rect))
        {
            return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        return null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var rect = GetGameWindowRect();
        if (rect.HasValue && rect != _lastRect)
        {
            _lastRect = rect;
            GameWindowRectChanged?.Invoke(rect.Value);
        }
    }

    private IntPtr FindWindowByTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, lParam) =>
        {
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            if (sb.ToString().Contains(title, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    #region WinAPI

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion
}

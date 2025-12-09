using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PoE2Inspector.Domain;
using PoE2Inspector.Services.Logging;
using PoE2Inspector.Services.Trade;
using PoE2Inspector.UI;

namespace PoE2Inspector.Services.Overlay;

public class OverlayService : IOverlayService
{
    private readonly ILogService _log;
    private readonly ITradeService _trade;
    private OverlayWindow? _overlayWindow;
    private Rect _gameRect;

    public OverlayService(ILogService log, ITradeService trade)
    {
        _log = log;
        _trade = trade;
    }

    public void Show()
    {
        if (_overlayWindow == null)
        {
            _overlayWindow = new OverlayWindow();
            _overlayWindow.SetOverlayService(this);
            _overlayWindow.SetTradeService(_trade);
            _overlayWindow.Show();
            MakeClickThrough(_overlayWindow);
            _log.Info("Overlay window created and shown");
        }
    }

    public void Hide()
    {
        if (_overlayWindow != null)
        {
            _overlayWindow.Hide();
        }
    }

    public void UpdateGameRect(Rect rect)
    {
        _gameRect = rect;

        if (_overlayWindow != null)
        {
            _overlayWindow.Left = rect.Left;
            _overlayWindow.Top = rect.Top;
            _overlayWindow.Width = rect.Width;
            _overlayWindow.Height = rect.Height;
        }
    }

    public void ShowItemAtCursor(Item item, Point cursorPositionScreen)
    {
        if (_overlayWindow == null)
        {
            _log.Warn("Overlay window not initialized");
            return;
        }

        var relativePos = new Point(
            cursorPositionScreen.X - _gameRect.Left,
            cursorPositionScreen.Y - _gameRect.Top
        );

        MakeInteractive(_overlayWindow);

        _overlayWindow.ShowItem(item, relativePos);
        _log.Info($"Showing item at cursor position: {relativePos.X}, {relativePos.Y}");
    }

    private void MakeClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            _log.Error("Failed to get window handle for overlay");
            return;
        }

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        _log.Info("Overlay window made click-through");
    }

    private void MakeInteractive(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            _log.Error("Failed to get window handle for overlay");
            return;
        }

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle &= ~WS_EX_TRANSPARENT; 
        exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        _log.Info("Overlay window made interactive");
    }

    public void MakeOverlayClickThrough()
    {
        if (_overlayWindow != null)
        {
            MakeClickThrough(_overlayWindow);
        }
    }

    #region WinAPI

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    #endregion
}

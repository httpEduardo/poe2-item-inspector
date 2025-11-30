using System;
using System.Threading;
using System.Windows;
using PoE2Inspector.Domain;
using PoE2Inspector.Services.Clipboard;
using PoE2Inspector.Services.Config;
using PoE2Inspector.Services.EventBus;
using PoE2Inspector.Services.GameWindow;
using PoE2Inspector.Services.Hotkey;
using PoE2Inspector.Services.Input;
using PoE2Inspector.Services.Logging;
using PoE2Inspector.Services.Overlay;
using PoE2Inspector.Services.Parser;
using PoE2Inspector.Services.Trade;

namespace PoE2Inspector;

public class AppHost
{
    private readonly ILogService _log;
    private readonly IConfigService _config;
    private readonly IEventBus _bus;
    private readonly IGameWindowService _gameWindow;
    private readonly HotkeyService _hotkeys;
    private readonly IInputService _input;
    private readonly IClipboardService _clipboard;
    private readonly IItemParser _parser;
    private readonly IOverlayService _overlay;
    private readonly ITradeService _trade;

    private bool _isProcessing;

    public AppHost(
        ILogService log,
        IConfigService config,
        IEventBus bus,
        IGameWindowService gameWindow,
        HotkeyService hotkeys,
        IInputService input,
        IClipboardService clipboard,
        IItemParser parser,
        IOverlayService overlay,
        ITradeService trade)
    {
        _log = log;
        _config = config;
        _bus = bus;
        _gameWindow = gameWindow;
        _hotkeys = hotkeys;
        _input = input;
        _clipboard = clipboard;
        _parser = parser;
        _overlay = overlay;
        _trade = trade;
    }

    public void Start(Window mainWindow)
    {
        _log.Info("=== PoE2 Inspector Starting ===");

        _hotkeys.Initialize(mainWindow);

        _hotkeys.HotkeyPressed += (action) =>
        {
            _log.Info($"Hotkey pressed event received: {action}");
            _bus.Publish(new HotkeyEvent(action));
        };

        _bus.Subscribe<HotkeyEvent>(HandleHotkey);
        _bus.Subscribe<ItemTextCapturedEvent>(HandleItemText);

        var config = _config.Load();
        _hotkeys.Register(
            HotkeyAction.InspectItem,
            config.InspectItemHotkey.Modifiers,
            config.InspectItemHotkey.Key);

        _gameWindow.StartMonitoring();
        _gameWindow.GameWindowRectChanged += rect =>
        {
            _overlay.UpdateGameRect(rect);
        };

        _overlay.Show();

        _log.Info("AppHost started successfully");
    }

    public void Stop()
    {
        _log.Info("Stopping AppHost");
        _gameWindow.StopMonitoring();
        _hotkeys.UnregisterAll();
        _overlay.Hide();
    }

    private async void HandleHotkey(HotkeyEvent evt)
    {
        if (_isProcessing)
        {
            _log.Warn("Already processing hotkey, ignoring");
            return;
        }

        _isProcessing = true;

        try
        {
            _log.Info($"Hotkey triggered: {evt.Action}");

            if (!_gameWindow.IsGameInForeground())
            {
                _log.Warn("PoE2 is not in foreground");
                return;
            }

            var rawText = await _clipboard.CaptureItemTextAsync(
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                _bus.Publish(new ItemTextCapturedEvent(rawText));
            }
        }
        catch (Exception ex)
        {
            _log.Error("Error handling hotkey", ex);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void HandleItemText(ItemTextCapturedEvent evt)
    {
        try
        {

            var item = _parser.Parse(evt.RawText);

            _bus.Publish(new ItemParsedEvent(item));

            GetCursorPos(out var point);
            var cursorPos = new Point(point.X, point.Y);

            _overlay.ShowItemAtCursor(item, cursorPos);
        }
        catch (Exception ex)
        {
            _log.Error("Error handling item text", ex);
        }
    }

    #region WinAPI

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    #endregion
}

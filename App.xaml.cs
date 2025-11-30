using System;
using System.Windows;
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

public partial class App : Application
{
    private AppHost? _appHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var log = new LogService();
        var config = new ConfigService();
        var bus = new EventBus();
        var gameWindow = new GameWindowService(log);
        var hotkeys = new HotkeyService(log);
        var input = new InputService(log);
        var clipboard = new ClipboardService(input, log);
        var parser = new ItemParser(log);
        var trade = new TradeService(log);
        var overlay = new OverlayService(log, trade);

        _appHost = new AppHost(log, config, bus, gameWindow, hotkeys, input, clipboard, parser, overlay, trade);

        var mainWindow = new MainWindow();
        mainWindow.Loaded += (s, args) =>
        {
            _appHost.Start(mainWindow);
        };
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appHost?.Stop();
        base.OnExit(e);
    }
}

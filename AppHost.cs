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

public class AppHost
{
    public ILogService Log { get; }
    public IConfigService Config { get; }
    public IEventBus Bus { get; }
    public IGameWindowService GameWindow { get; }
    public IHotkeyService Hotkeys { get; }
    public IInputService Input { get; }
    public IClipboardService Clipboard { get; }
    public IItemParser Parser { get; }
    public IOverlayService Overlay { get; }
    public ITradeService Trade { get; }
    
    private MainWindow? _mainWindow;

    public AppHost()
    {
        Log = new LogService();
        Config = new ConfigService();
        Bus = new EventBus();
        GameWindow = new GameWindowService(Log);
        Input = new InputService(Log);
        Clipboard = new ClipboardService(Input, Log);
        Parser = new ItemParser(Log);
        Trade = new TradeService(Log);
        Overlay = new OverlayService(Log, Trade);
        Hotkeys = new HotkeyService(Log);
    }

    public void Start(Window mainWindow)
    {
        Log.Info("AppHost starting...");
        
        _mainWindow = mainWindow as MainWindow;
        
        if (Hotkeys is HotkeyService service)
        {
            service.Initialize(mainWindow);
        }
        
        Hotkeys.Register(HotkeyAction.InspectItem, System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.E);
        
        Hotkeys.HotkeyPressed += OnHotkeyPressed;
        
        // Don't show overlay by default - only use main window
        // Overlay.Show();
    }

    private async void OnHotkeyPressed(HotkeyAction action)
    {
        if (action == HotkeyAction.InspectItem)
        {
            Log.Info("Hotkey pressed: InspectItem");
            
            var text = await Clipboard.CaptureItemTextAsync(TimeSpan.FromSeconds(1), System.Threading.CancellationToken.None);
            if (string.IsNullOrWhiteSpace(text))
            {
                Log.Warn("Clipboard is empty or capture failed");
                return;
            }

            var item = Parser.Parse(text);
            if (item != null)
            {
                // Only update the main window - overlay was causing issues
                if (_mainWindow != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _mainWindow.Activate();
                        _ = _mainWindow.EvaluateItemAsync(text);
                    });
                }
            }
        }
    }

    public void Stop()
    {
        if (Hotkeys is IDisposable disposable)
        {
            disposable.Dispose();
        }
        Overlay.Hide();
        Log.Info("AppHost stopped.");
    }
}

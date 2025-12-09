using System.Windows.Input;

namespace PoE2Inspector.Services.Config;

public class AppConfig
{
    public HotkeyConfig InspectItemHotkey { get; set; } = new()
    {
        Modifiers = ModifierKeys.Alt,
        Key = Key.E
    };

    public int ClipboardTimeoutMs { get; set; } = 2000;
    
    // UI Settings
    public string HotkeyText { get; set; } = "Alt+E";
    public bool AutoEvaluate { get; set; } = true;
    public bool ShowPriceRange { get; set; } = true;
    
    // Currency Rates (in chaos orbs)
    public decimal DivineRate { get; set; } = 150m;
    public decimal ExaltedRate { get; set; } = 20m;
}

public class HotkeyConfig
{
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }

    public HotkeyConfig()
    {
        Modifiers = ModifierKeys.None;
        Key = Key.None;
    }
}


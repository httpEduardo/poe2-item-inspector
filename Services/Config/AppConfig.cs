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

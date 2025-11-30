using System;
using System.Windows.Input;
using PoE2Inspector.Services.EventBus;

namespace PoE2Inspector.Services.Hotkey;

public interface IHotkeyService
{
    void Register(HotkeyAction action, ModifierKeys modifiers, Key key);
    void UnregisterAll();
    event Action<HotkeyAction>? HotkeyPressed;
}

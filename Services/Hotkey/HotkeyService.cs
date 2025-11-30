using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PoE2Inspector.Services.EventBus;
using PoE2Inspector.Services.Logging;

namespace PoE2Inspector.Services.Hotkey;

public class HotkeyService : IHotkeyService, IDisposable
{
    private readonly ILogService _log;
    private readonly Dictionary<int, HotkeyAction> _hotkeyMap = new();
    private IntPtr _hwnd;
    private HwndSource? _source;
    private int _currentId = 1;

    public event Action<HotkeyAction>? HotkeyPressed;

    public HotkeyService(ILogService log)
    {
        _log = log;
    }

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        _log.Info("HotkeyService initialized");
    }

    public void Register(HotkeyAction action, ModifierKeys modifiers, Key key)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _log.Error("HotkeyService not initialized");
            return;
        }

        var id = _currentId++;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        var mod = ConvertModifiers(modifiers);

        if (RegisterHotKey(_hwnd, id, mod, (uint)vk))
        {
            _hotkeyMap[id] = action;
            _log.Info($"Registered hotkey {id}: {modifiers}+{key} for {action}");
        }
        else
        {
            _log.Error($"Failed to register hotkey: {modifiers}+{key}");
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _hotkeyMap.Keys.ToList())
        {
            UnregisterHotKey(_hwnd, id);
        }
        _hotkeyMap.Clear();
        _log.Info("All hotkeys unregistered");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_hotkeyMap.TryGetValue(id, out var action))
            {
                _log.Info($"Hotkey pressed: {action}");
                HotkeyPressed?.Invoke(action);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private uint ConvertModifiers(ModifierKeys modifiers)
    {
        uint mod = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mod |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) mod |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mod |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mod |= MOD_WIN;
        return mod;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }

    #region WinAPI

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    #endregion
}

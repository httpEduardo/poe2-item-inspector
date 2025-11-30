using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace PoE2Inspector.Services.GameWindow;

public interface IGameWindowService
{
    IntPtr? GetGameWindowHandle();
    bool IsGameInForeground();
    Rect? GetGameWindowRect();
    event Action<Rect>? GameWindowRectChanged;
    void StartMonitoring();
    void StopMonitoring();
}

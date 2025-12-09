using System.Windows;

namespace PoE2Inspector.Services.Input;

public interface IInputService
{
    void SendCtrlC();
    Point GetCursorPosition();
}

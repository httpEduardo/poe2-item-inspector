using System;
using System.Runtime.InteropServices;
using System.Windows;
using PoE2Inspector.Services.Logging;

namespace PoE2Inspector.Services.Input;

public class InputService : IInputService
{
    private readonly ILogService _log;

    public InputService(ILogService log)
    {
        _log = log;
    }

    public Point GetCursorPosition()
    {
        if (GetCursorPos(out var point))
        {
            return new Point(point.X, point.Y);
        }
        return new Point(0, 0);
    }

    public void SendCtrlC()
    {
        _log.Info("Sending Ctrl+C...");

        try
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            _log.Info("Ctrl+C sent successfully via keybd_event");
            return;
        }
        catch (Exception ex)
        {
            _log.Error("keybd_event failed, trying SendInput", ex);
        }

        var inputs = new INPUT[4];

        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_CONTROL,
                    dwFlags = 0
                }
            }
        };

        inputs[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_C,
                    dwFlags = 0
                }
            }
        };

        inputs[2] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_C,
                    dwFlags = KEYEVENTF_KEYUP
                }
            }
        };

        inputs[3] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_CONTROL,
                    dwFlags = KEYEVENTF_KEYUP
                }
            }
        };

        uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));

        if (result != inputs.Length)
        {
            _log.Error($"SendInput failed. Expected {inputs.Length}, sent {result}");
        }
        else
        {
            _log.Info("Sent Ctrl+C");
        }
    }

    #region WinAPI

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private const int INPUT_KEYBOARD = 1;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_C = 0x43;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion
}

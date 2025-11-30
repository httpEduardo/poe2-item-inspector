using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PoE2Inspector.Services.Input;
using PoE2Inspector.Services.Logging;

namespace PoE2Inspector.Services.Clipboard;

public class ClipboardService : IClipboardService
{
    private readonly IInputService _input;
    private readonly ILogService _log;

    public ClipboardService(IInputService input, ILogService log)
    {
        _input = input;
        _log = log;
    }

    public async Task<string?> CaptureItemTextAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {

            string? originalText = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        originalText = System.Windows.Clipboard.GetText();
                    }
                }
                catch { }
            });

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    System.Windows.Clipboard.Clear();
                }
                catch { }
            });

            _input.SendCtrlC();

            var startTime = DateTime.UtcNow;
            string? capturedText = null;

            while (DateTime.UtcNow - startTime < timeout && !ct.IsCancellationRequested)
            {
                await Task.Delay(50, ct);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (System.Windows.Clipboard.ContainsText())
                        {
                            var text = System.Windows.Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(text) && text != originalText)
                            {
                                capturedText = text;
                            }
                        }
                    }
                    catch { }
                });

                if (capturedText != null)
                    break;
            }

            if (originalText != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(originalText);
                    }
                    catch { }
                });
            }

            if (capturedText != null)
            {
                _log.Info($"Captured item text ({capturedText.Length} chars)");
            }
            else
            {
                _log.Warn("Failed to capture item text (timeout or empty)");
            }

            return capturedText;
        }
        catch (Exception ex)
        {
            _log.Error("Error capturing clipboard", ex);
            return null;
        }
    }
}

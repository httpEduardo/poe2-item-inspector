using System;
using System.Threading;
using System.Threading.Tasks;

namespace PoE2Inspector.Services.Clipboard;

public interface IClipboardService
{
    Task<string?> CaptureItemTextAsync(TimeSpan timeout, CancellationToken ct);
}

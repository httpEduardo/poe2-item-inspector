using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Web;
using System.Windows;
using PoE2Inspector.Domain;
using PoE2Inspector.Services.Logging;

namespace PoE2Inspector.Services.Trade;

public class TradeService : ITradeService
{
    private readonly ILogService _log;
    private const string BASE_URL = "https://www.pathofexile.com/trade2/search/poe2/Rise%20of%20the%20Abyssal";

    public TradeService(ILogService log)
    {
        _log = log;
    }

    public string BuildTradeUrl(Item item)
    {

        _log.Info($"Opening trade site for item: {item.Name ?? item.BaseType ?? "Unknown"}");
        return BASE_URL;
    }

    public void OpenTradeSearch(Item item)
    {
        try
        {

            var searchTerm = item.Name ?? item.BaseType ?? "Unknown Item";

            if (searchTerm != "Unknown Item" && searchTerm != "Parse Error")
            {
                System.Windows.Clipboard.SetText(searchTerm);
                _log.Info($"Copied to clipboard: {searchTerm}");
            }

            var url = BuildTradeUrl(item);
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            _log.Info($"Opened trade search for item: {item.Name}");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open trade search", ex);
        }
    }
}

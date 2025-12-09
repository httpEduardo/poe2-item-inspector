using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using PoE2Inspector.Domain;
using PoE2Inspector.Services.Logging;

namespace PoE2Inspector.Services.Trade;

public class TradeService : ITradeService
{
    private readonly ILogService _log;
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string API_URL = "https://www.pathofexile.com/api/trade2/search/poe2/Standard";
    private const string WEB_URL = "https://www.pathofexile.com/trade2/search/poe2/Standard";

    public TradeService(ILogService log)
    {
        _log = log;
        // Add User-Agent to avoid being blocked
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PoE2Inspector/1.0");
        }
    }

    public string BuildTradeUrl(Item item)
    {
        // Deprecated for direct usage, logic moved to OpenTradeSearch for async support
        return WEB_URL;
    }

    public async void OpenTradeSearch(Item item)
    {
        try
        {
            var searchTerm = !string.IsNullOrEmpty(item.Name) ? item.Name : item.BaseType;

            if (string.IsNullOrEmpty(searchTerm) || searchTerm == "Unknown Item" || searchTerm == "Parse Error")
            {
                _log.Warn("Cannot search for unknown item");
                return;
            }

            // Copy to clipboard as backup
            try
            {
                System.Windows.Clipboard.SetText(searchTerm);
                _log.Info($"Copied to clipboard: {searchTerm}");
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to copy to clipboard: {ex.Message}");
            }

            // Create the query JSON
            var query = new
            {
                query = new
                {
                    status = new { option = "online" },
                    term = searchTerm,
                    stats = new object[] { new { type = "and", filters = new object[] { } } }
                },
                sort = new { price = "asc" }
            };

            var jsonContent = JsonSerializer.Serialize(query);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _log.Info($"Sending trade query for: {searchTerm}...");

            // Send to PoE Trade API
            var response = await _httpClient.PostAsync(API_URL, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _log.Error($"Trade API Error: {response.StatusCode} - {errorBody}");
                
                // Fallback: Just open the site
                OpenUrl(WEB_URL);
                return;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            
            if (doc.RootElement.TryGetProperty("id", out var idElement))
            {
                var searchId = idElement.GetString();
                var finalUrl = $"{WEB_URL}/{searchId}";
                OpenUrl(finalUrl);
            }
            else
            {
                _log.Error("Could not find ID in trade response");
                OpenUrl(WEB_URL);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open trade search", ex);
            MessageBox.Show($"Error opening trade: {ex.Message}");
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            _log.Info($"Opened URL: {url}");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open URL: {url}", ex);
        }
    }
}

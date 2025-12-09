using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PoE2Inspector.Domain;
using PoE2Inspector.Services.Overlay;
using PoE2Inspector.Services.Trade;

namespace PoE2Inspector.UI;

public partial class OverlayWindow : Window
{
    private IOverlayService? _overlayService;
    private ITradeService? _tradeService;
    private Item? _currentItem;
    private string _currentPrice = "";
    private static readonly HttpClient _httpClient = new HttpClient();

    public OverlayWindow()
    {
        InitializeComponent();
        
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PoE2Inspector/1.0");
        }
    }

    public void SetOverlayService(IOverlayService service)
    {
        _overlayService = service;
    }

    public void SetTradeService(ITradeService service)
    {
        _tradeService = service;
    }

    public async void ShowItem(Item item, Point position)
    {
        _currentItem = item;
        
        // Show loading first
        LoadingPanel.Margin = new Thickness(position.X + 20, position.Y, 0, 0);
        LoadingPanel.Visibility = Visibility.Visible;
        AnalysisPanel.Visibility = Visibility.Collapsed;
        
        // Adjust position to not go off screen
        double panelX = position.X + 20;
        double panelY = position.Y;
        
        if (panelX + 400 > ActualWidth)
            panelX = position.X - 420;
        if (panelY + 500 > ActualHeight)
            panelY = ActualHeight - 520;
        if (panelY < 10)
            panelY = 10;
        
        // Update basic info
        TxtItemName.Text = !string.IsNullOrEmpty(item.Name) ? item.Name : item.BaseType;
        TxtItemType.Text = item.BaseType;
        TxtItemRarity.Text = $"Raridade: {item.Rarity}";
        
        // Set name color based on rarity
        TxtItemName.Foreground = item.Rarity switch
        {
            "Unique" => new SolidColorBrush(Color.FromRgb(175, 96, 37)),
            "Rare" => new SolidColorBrush(Color.FromRgb(255, 255, 119)),
            "Magic" => new SolidColorBrush(Color.FromRgb(136, 136, 255)),
            _ => new SolidColorBrush(Color.FromRgb(200, 200, 200))
        };
        
        // Analyze mods
        AnalyzeMods(item);
        
        // Calculate quality
        var (score, description) = CalculateQuality(item);
        TxtQualityScore.Text = score.ToString();
        TxtQualityDesc.Text = description;
        
        // Set quality color
        TxtQualityScore.Foreground = score switch
        {
            >= 8 => new SolidColorBrush(Color.FromRgb(76, 175, 80)),   // Green
            >= 6 => new SolidColorBrush(Color.FromRgb(255, 235, 59)),  // Yellow
            >= 4 => new SolidColorBrush(Color.FromRgb(255, 152, 0)),   // Orange
            _ => new SolidColorBrush(Color.FromRgb(244, 67, 54))       // Red
        };
        
        // Generate verdict
        GenerateVerdict(item, score);
        
        // Position and show panel
        AnalysisPanel.Margin = new Thickness(panelX, panelY, 0, 0);
        
        // Get price estimate (async)
        await GetPriceEstimateAsync(item);
        
        // Hide loading, show analysis
        LoadingPanel.Visibility = Visibility.Collapsed;
        AnalysisPanel.Visibility = Visibility.Visible;
    }

    private void AnalyzeMods(Item item)
    {
        var sb = new StringBuilder();
        
        if (item.ImplicitMods.Any())
        {
            sb.AppendLine("📌 Implícitos:");
            foreach (var mod in item.ImplicitMods)
            {
                sb.AppendLine($"  • {mod.RawText}");
            }
        }
        
        if (item.ExplicitMods.Any())
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("🔹 Explícitos:");
            foreach (var mod in item.ExplicitMods)
            {
                var tier = GetModTierEmoji(mod.RawText);
                sb.AppendLine($"  {tier} {mod.RawText}");
            }
        }
        
        if (!item.ImplicitMods.Any() && !item.ExplicitMods.Any())
        {
            sb.AppendLine("Nenhum mod identificado.");
            sb.AppendLine("Item base ou currency.");
        }
        
        TxtModsAnalysis.Text = sb.ToString();
    }

    private string GetModTierEmoji(string modText)
    {
        var text = modText.ToLower();
        var value = ExtractNumericValue(modText);
        
        if (text.Contains("life") || text.Contains("vida"))
        {
            if (value >= 80) return "🟢";
            if (value >= 60) return "🟡";
            if (value >= 40) return "🟠";
            return "🔴";
        }
        
        if (text.Contains("resist"))
        {
            if (value >= 40) return "🟢";
            if (value >= 30) return "🟡";
            if (value >= 20) return "🟠";
            return "🔴";
        }
        
        if (text.Contains("damage") || text.Contains("dano"))
        {
            if (value >= 100) return "🟢";
            if (value >= 60) return "🟡";
            if (value >= 30) return "🟠";
            return "🔴";
        }
        
        return "⚪";
    }

    private double ExtractNumericValue(string text)
    {
        var match = Regex.Match(text, @"[\+\-]?(\d+(?:\.\d+)?)");
        if (match.Success && double.TryParse(match.Groups[1].Value, out double val))
            return val;
        return 0;
    }

    private (int score, string description) CalculateQuality(Item item)
    {
        int score = 5;
        var reasons = new System.Collections.Generic.List<string>();
        
        // Rarity bonus
        if (item.Rarity == "Unique")
        {
            score += 2;
            reasons.Add("Único");
        }
        else if (item.Rarity == "Rare")
        {
            score += 1;
            reasons.Add("Raro");
        }
        
        // Mod count
        int modCount = item.ExplicitMods.Count + item.ImplicitMods.Count;
        if (modCount >= 6)
        {
            score += 2;
            reasons.Add($"{modCount} mods");
        }
        else if (modCount >= 4)
        {
            score += 1;
            reasons.Add($"{modCount} mods");
        }
        
        // Item level
        if (item.ItemLevel >= 83)
        {
            score += 1;
            reasons.Add($"iLvl {item.ItemLevel}");
        }
        
        // Cap at 10
        score = Math.Min(10, Math.Max(0, score));
        
        return (score, string.Join(" | ", reasons));
    }

    private void GenerateVerdict(Item item, int qualityScore)
    {
        string verdict;
        string bgColor;
        
        if (qualityScore >= 8)
        {
            verdict = "🌟 EXCELENTE! Item muito valioso, vale a pena manter ou vender por bom preço.";
            bgColor = "#1A4D1A"; // Green
        }
        else if (qualityScore >= 6)
        {
            verdict = "👍 BOM item. Pode valer algo no trade, verifique o preço.";
            bgColor = "#4D4D1A"; // Yellow-ish
        }
        else if (qualityScore >= 4)
        {
            verdict = "😐 MÉDIO. Pode ter algum valor para builds específicas.";
            bgColor = "#4D3A1A"; // Orange-ish
        }
        else
        {
            verdict = "👎 FRACO. Provavelmente não vale vender, considere usar para craft.";
            bgColor = "#4D1A1A"; // Red
        }
        
        TxtVerdict.Text = verdict;
        VerdictBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor));
    }

    private async Task GetPriceEstimateAsync(Item item)
    {
        try
        {
            TxtPrice.Text = "Buscando...";
            TxtPriceInfo.Text = "Consultando trade API...";
            
            var league = "Standard"; // TODO: Make configurable
            var searchUrl = $"https://www.pathofexile.com/api/trade2/search/poe2/{league}";
            
            // Build search query
            var query = new
            {
                query = new
                {
                    status = new { option = "online" },
                    type = item.BaseType,
                    stats = new[] { new { type = "and", filters = Array.Empty<object>() } }
                },
                sort = new { price = "asc" }
            };
            
            var json = JsonSerializer.Serialize(query);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(searchUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("result", out var results) && results.GetArrayLength() > 0)
                {
                    var resultCount = results.GetArrayLength();
                    
                    // Fetch first few results for price
                    var fetchIds = results.EnumerateArray().Take(5).Select(r => r.GetString()).ToArray();
                    var fetchUrl = $"https://www.pathofexile.com/api/trade2/fetch/{string.Join(",", fetchIds)}?query={root.GetProperty("id").GetString()}";
                    
                    await Task.Delay(500); // Rate limiting
                    var fetchResponse = await _httpClient.GetAsync(fetchUrl);
                    
                    if (fetchResponse.IsSuccessStatusCode)
                    {
                        var fetchJson = await fetchResponse.Content.ReadAsStringAsync();
                        using var fetchDoc = JsonDocument.Parse(fetchJson);
                        
                        var prices = new System.Collections.Generic.List<string>();
                        foreach (var result in fetchDoc.RootElement.GetProperty("result").EnumerateArray())
                        {
                            if (result.TryGetProperty("listing", out var listing) &&
                                listing.TryGetProperty("price", out var price))
                            {
                                var amount = price.GetProperty("amount").GetDouble();
                                var currency = price.GetProperty("currency").GetString();
                                prices.Add($"{amount} {currency}");
                            }
                        }
                        
                        if (prices.Any())
                        {
                            _currentPrice = prices.First();
                            TxtPrice.Text = _currentPrice;
                            TxtPriceInfo.Text = $"Baseado em {resultCount} listagens";
                            return;
                        }
                    }
                }
                
                TxtPrice.Text = "Sem listagens";
                TxtPriceInfo.Text = "Nenhum item similar encontrado";
            }
            else
            {
                TxtPrice.Text = "Erro na API";
                TxtPriceInfo.Text = $"Status: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            TxtPrice.Text = "Erro";
            TxtPriceInfo.Text = ex.Message.Length > 40 ? ex.Message.Substring(0, 40) + "..." : ex.Message;
        }
    }

    public void HidePanel()
    {
        AnalysisPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
    }

    private void BtnOpenTrade_Click(object sender, RoutedEventArgs e)
    {
        if (_currentItem != null && _tradeService != null)
        {
            _tradeService.OpenTradeSearch(_currentItem);
        }
    }

    private void BtnCopyPrice_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentPrice))
        {
            try
            {
                Clipboard.SetText(_currentPrice);
            }
            catch { }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        HidePanel();
    }
}

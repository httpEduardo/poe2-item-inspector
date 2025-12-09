using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PoE2Inspector.Domain;
using PoE2Inspector.Services.Parser;
using PoE2Inspector.Services.Config;
using PoE2Inspector.Services.Logging;
using PoE2Inspector.Services.PoeNinja;

namespace PoE2Inspector
{
    public partial class MainWindow : Window
    {
        private readonly IItemParser _parser;
        private readonly IConfigService _configService;
        private readonly IPoeNinjaService _poeNinja;
        private readonly ILogService _log;
        private readonly HttpClient _httpClient;
        private AppConfig _config;
        
        private Item? _currentItem;
        private string _currentLeague = "Standard";
        private string _tradeLeague = "Rise of the Abyssal"; // Liga atual do PoE2
        private string _selectedCurrency = "chaos";
        private decimal _divineRate = 150m;
        private decimal _exaltedRate = 20m;
        
        private List<TradeListingInfo> _currentListings = new();
        private List<MarketItemInfo> _marketItems = new();
        private PoeNinjaItem? _currentPoeNinjaItem;
        
        // Cache para Trade API (evitar rate limit)
        private static readonly Dictionary<string, (List<TradeListingInfo> Listings, List<MarketItemInfo> Items, DateTime CachedAt)> _tradeCache = new();
        private const int CACHE_MINUTES = 3;
        
        public MainWindow(IItemParser parser, IConfigService configService, IPoeNinjaService poeNinja, ILogService log)
        {
            InitializeComponent();
            _parser = parser;
            _configService = configService;
            _poeNinja = poeNinja;
            _log = log;
            _config = configService.Load();
            
            // HttpClient para Trade API
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PoE2Inspector/2.0 (contact: poe2inspector@example.com)");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            LoadConfiguration();
        }
        
        public MainWindow() : this(
            new ItemParser(new LogService()), 
            new ConfigService(),
            new PoeNinjaService(new LogService()),
            new LogService()) { }
        
        private void LoadConfiguration()
        {
            TxtHotkey.Text = _config.HotkeyText ?? "Alt+E";
            ChkAutoEvaluate.IsChecked = _config.AutoEvaluate;
            ChkShowPriceRange.IsChecked = _config.ShowPriceRange;
            TxtDivineRate.Text = _config.DivineRate.ToString();
            TxtExaltedRate.Text = _config.ExaltedRate.ToString();
            _divineRate = _config.DivineRate;
            _exaltedRate = _config.ExaltedRate;
            
            // Inicializar liga do ComboBox
            if (CmbLeague.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                _tradeLeague = item.Content.ToString() ?? "Rise of the Abyssal";
                _log.Info($"Liga inicializada: {_tradeLeague}");
            }
        }
        
        // ========== SELETORES ==========
        
        private void CmbLeague_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLeague.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                var league = item.Content.ToString() ?? "Standard";
                // Para Trade API usamos o nome real da liga
                _tradeLeague = league;
                // Para poe.ninja usamos Standard (PoE2 ainda não suportado)
                _currentLeague = "Standard";
            }
        }
        
        private void CmbCurrency_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCurrency.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _selectedCurrency = item.Tag.ToString() ?? "chaos";
                if (_currentListings.Any()) UpdatePriceDisplay();
            }
        }
        
        // ========== BOTÕES PRINCIPAIS ==========
        
        private async void BtnEvaluate_Click(object sender, RoutedEventArgs e)
        {
            await EvaluateItemAsync(TxtItemInput.Text);
        }
        
        private async void BtnPaste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    TxtItemInput.Text = Clipboard.GetText();
                    await EvaluateItemAsync(TxtItemInput.Text);
                }
            }
            catch (Exception ex)
            {
                TxtResult.Text = $"Erro ao colar: {ex.Message}";
            }
        }
        
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtItemInput.Text = "";
            TxtResult.Text = "Aguardando item para avaliar...";
            TxtPriceSummary.Text = "--";
            TxtPriceDetail.Text = "Avalie um item para ver o preço";
            TxtTradeListings.Text = "As listagens do mercado aparecerão aqui após avaliar um item...";
            TxtPriceRange.Text = "A análise de preços aparecerá aqui...";
            TxtCalcResult.Text = "O resultado detalhado aparecerá aqui...";
            TxtSellChance.Text = "--%";
            TxtPriceAdvice.Text = "💡 Avalie um item primeiro";
            ProgressSellChance.Value = 0;
            TxtMyItemStats.Text = "Avalie um item para ver seus stats aqui...";
            TxtComparison.Text = "Clique em 'Analisar Diferenças' para comparar seu item com os do mercado...";
            TxtSalesHistory.Text = "Avalie um item e clique em 'Buscar Histórico' para ver dados de mercado...";
            TxtSoldCount.Text = "--";
            TxtMarketCount.Text = "(Avalie um item para ver)";
            _currentItem = null;
            _currentListings.Clear();
            _marketItems.Clear();
            _currentPoeNinjaItem = null;
        }
        
        // ========== AVALIAÇÃO ==========
        
        public async Task EvaluateItemAsync(string itemText)
        {
            if (string.IsNullOrWhiteSpace(itemText) || itemText.Contains("Cole o texto"))
            {
                TxtResult.Text = "❌ Cole o texto do item (Ctrl+C no jogo) para avaliar.";
                return;
            }
            
            try
            {
                TxtResult.Text = "⏳ Analisando item...";
                TxtPriceSummary.Text = "⏳";
                TxtPriceDetail.Text = "Processando...";
                TxtMyItemStats.Text = "⏳ Carregando...";
                
                _currentItem = _parser.Parse(itemText);
                
                if (_currentItem == null)
                {
                    ShowError("Não foi possível analisar o item");
                    return;
                }
                
                DisplayParsedItem();
                
                // ESTRATÉGIA HÍBRIDA:
                // - Itens ÚNICOS: poe.ninja (rápido, sem rate limit)
                // - Itens RAROS/MÁGICOS: Trade API (pode ter rate limit)
                if (_currentItem.Rarity == "Unique" && !string.IsNullOrEmpty(_currentItem.Name))
                {
                    TxtPriceDetail.Text = "Buscando no poe.ninja...";
                    await FetchPoeNinjaPriceAsync();
                }
                else
                {
                    TxtPriceDetail.Text = "Buscando no Trade...";
                    await FetchTradePriceAsync();
                }
            }
            catch (Exception ex)
            {
                ShowError($"Erro: {ex.Message}");
            }
        }
        
        private void DisplayParsedItem()
        {
            if (_currentItem == null) return;
            
            var sb = new StringBuilder();
            
            if (!string.IsNullOrEmpty(_currentItem.Name))
                sb.AppendLine($"📦 {_currentItem.Name}");
            
            if (!string.IsNullOrEmpty(_currentItem.BaseType) && _currentItem.BaseType != _currentItem.Name)
                sb.AppendLine($"📋 Base: {_currentItem.BaseType}");
            
            if (!string.IsNullOrEmpty(_currentItem.Rarity))
                sb.AppendLine($"⭐ Raridade: {_currentItem.Rarity}");
            
            if (_currentItem.ItemLevel.HasValue)
                sb.AppendLine($"📊 Item Level: {_currentItem.ItemLevel}");
            
            sb.AppendLine();
            
            if (_currentItem.ImplicitMods.Any())
            {
                sb.AppendLine("🔷 IMPLÍCITOS:");
                foreach (var mod in _currentItem.ImplicitMods)
                    sb.AppendLine($"  • {mod.RawText}");
                sb.AppendLine();
            }
            
            if (_currentItem.ExplicitMods.Any())
            {
                sb.AppendLine("🔶 EXPLÍCITOS:");
                foreach (var mod in _currentItem.ExplicitMods)
                    sb.AppendLine($"  • {mod.RawText}");
            }
            
            TxtResult.Text = sb.ToString();
            UpdateMyItemStats();
        }
        
        private void UpdateMyItemStats()
        {
            if (_currentItem == null)
            {
                TxtMyItemStats.Text = "Nenhum item avaliado.";
                return;
            }
            
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"  📦 {_currentItem.Name}");
            if (!string.IsNullOrEmpty(_currentItem.BaseType))
                sb.AppendLine($"  📋 {_currentItem.BaseType}");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine();
            
            if (_currentItem.ImplicitMods.Any())
            {
                sb.AppendLine("🔷 IMPLÍCITOS:");
                foreach (var mod in _currentItem.ImplicitMods)
                    sb.AppendLine($"  • {mod.RawText}");
                sb.AppendLine();
            }
            
            if (_currentItem.ExplicitMods.Any())
            {
                sb.AppendLine("🔶 EXPLÍCITOS:");
                foreach (var mod in _currentItem.ExplicitMods)
                    sb.AppendLine($"  • {mod.RawText}");
            }
            
            TxtMyItemStats.Text = sb.ToString();
        }
        
        private void ShowError(string message)
        {
            TxtResult.Text = $"❌ {message}";
            TxtPriceSummary.Text = "Erro";
            TxtPriceDetail.Text = message;
        }
        
        // ========== POE.NINJA API (PARA ITENS ÚNICOS) ==========
        
        private async Task FetchPoeNinjaPriceAsync()
        {
            if (_currentItem == null)
            {
                TxtPriceSummary.Text = "N/A";
                TxtPriceDetail.Text = "Item inválido";
                return;
            }
            
            try
            {
                string searchName = _currentItem.Name ?? "";
                
                if (string.IsNullOrEmpty(searchName))
                {
                    // Sem nome, usar Trade API
                    await FetchTradePriceAsync();
                    return;
                }
                
                TxtPriceDetail.Text = $"Buscando '{searchName}'...";
                
                // Buscar no poe.ninja
                _currentPoeNinjaItem = await _poeNinja.FindItemByNameAsync(searchName, _currentLeague);
                
                if (_currentPoeNinjaItem != null)
                {
                    // Item único encontrado!
                    DisplayPoeNinjaPrice(_currentPoeNinjaItem);
                    await FetchSimilarItemsAsync(_currentPoeNinjaItem.BaseType);
                }
                else
                {
                    // Não encontrou no poe.ninja, tentar Trade API
                    TxtPriceDetail.Text = "Buscando no Trade...";
                    await FetchTradePriceAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"[PoeNinja] Error: {ex.Message}");
                // Fallback para Trade API
                await FetchTradePriceAsync();
            }
        }
        
        // ========== TRADE API (PARA ITENS RAROS) ==========
        
        private async Task FetchTradePriceAsync()
        {
            if (_currentItem == null || string.IsNullOrEmpty(_currentItem.BaseType))
            {
                TxtPriceSummary.Text = "N/A";
                TxtPriceDetail.Text = "Item inválido";
                return;
            }
            
            _log.Info($"=== FetchTradePriceAsync ===");
            _log.Info($"Item: {_currentItem.Name}");
            _log.Info($"BaseType: {_currentItem.BaseType}");
            _log.Info($"League: {_tradeLeague}");
            _log.Info($"Rarity: {_currentItem.Rarity}");
            
            var cacheKey = $"{_currentItem.BaseType}_{_tradeLeague}_{_currentItem.Rarity}";
            
            // Verificar cache primeiro
            if (_tradeCache.TryGetValue(cacheKey, out var cached) &&
                (DateTime.Now - cached.CachedAt).TotalMinutes < CACHE_MINUTES)
            {
                _currentListings = cached.Listings.ToList();
                _marketItems = cached.Items.ToList();
                TxtPriceDetail.Text = "Via cache...";
                UpdatePriceDisplay();
                DisplayTradeListings();
                return;
            }
            
            try
            {
                // Construir query de busca
                var query = BuildTradeQuery();
                var jsonQuery = JsonSerializer.Serialize(query);
                
                var leagueUrl = Uri.EscapeDataString(_tradeLeague);
                var searchUrl = $"https://www.pathofexile.com/api/trade2/search/poe2/{leagueUrl}";
                
                _log.Info($"Trade API URL: {searchUrl}");
                _log.Info($"Query: {jsonQuery}");
                
                // Delay para evitar rate limit
                await Task.Delay(300);
                
                var content = new StringContent(jsonQuery, Encoding.UTF8, "application/json");
                var searchResponse = await _httpClient.PostAsync(searchUrl, content);
                
                _log.Info($"Response Status: {searchResponse.StatusCode}");
                
                if (searchResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _log.Warn("Rate limited!");
                    ShowRateLimitMessage();
                    return;
                }
                
                if (!searchResponse.IsSuccessStatusCode)
                {
                    var errorBody = await searchResponse.Content.ReadAsStringAsync();
                    _log.Error($"Trade API Error: {searchResponse.StatusCode} - {errorBody}");
                    TxtPriceSummary.Text = "Erro";
                    TxtPriceDetail.Text = $"HTTP {(int)searchResponse.StatusCode}";
                    return;
                }
                
                var searchResult = await searchResponse.Content.ReadAsStringAsync();
                _log.Info($"Search result length: {searchResult.Length}");
                var searchData = JsonSerializer.Deserialize<JsonElement>(searchResult);
                
                if (!searchData.TryGetProperty("result", out var resultArray) || resultArray.GetArrayLength() == 0)
                {
                    _log.Warn("No results from Trade API");
                    TxtPriceSummary.Text = "Sem dados";
                    TxtPriceDetail.Text = "Nenhum encontrado";
                    TxtPriceRange.Text = "📭 Nenhum item deste tipo está à venda.\n\nTente ajustar os filtros ou aguarde novos vendedores.";
                    return;
                }
                
                var queryId = searchData.GetProperty("id").GetString();
                var totalResults = searchData.TryGetProperty("total", out var total) ? total.GetInt32() : 0;
                TxtMarketCount.Text = $"({totalResults} no mercado)";
                
                var resultIds = resultArray.EnumerateArray()
                    .Take(10)
                    .Select(x => x.GetString())
                    .Where(x => x != null)
                    .ToList();
                
                if (!resultIds.Any())
                {
                    TxtPriceSummary.Text = "Sem dados";
                    return;
                }
                
                // Delay antes de fetch
                await Task.Delay(300);
                
                var idsParam = string.Join(",", resultIds);
                var fetchUrl = $"https://www.pathofexile.com/api/trade2/fetch/{idsParam}?query={queryId}";
                var fetchResponse = await _httpClient.GetAsync(fetchUrl);
                
                if (fetchResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    ShowRateLimitMessage();
                    return;
                }
                
                if (!fetchResponse.IsSuccessStatusCode)
                {
                    TxtPriceSummary.Text = "Erro";
                    TxtPriceDetail.Text = "Falha ao buscar detalhes";
                    return;
                }
                
                var fetchResult = await fetchResponse.Content.ReadAsStringAsync();
                await ParseTradeResults(fetchResult);
                
                // Salvar no cache
                if (_currentListings.Any())
                {
                    _tradeCache[cacheKey] = (_currentListings.ToList(), _marketItems.ToList(), DateTime.Now);
                    UpdatePriceDisplay();
                    DisplayTradeListings();
                }
            }
            catch (Exception ex)
            {
                TxtPriceSummary.Text = "Erro";
                TxtPriceDetail.Text = ex.Message;
                _log.Error($"Trade API error: {ex.Message}");
            }
        }
        
        private object BuildTradeQuery()
        {
            // Query básica por tipo de base
            return new
            {
                query = new
                {
                    status = new { option = "online" },
                    type = _currentItem?.BaseType,
                    stats = new[] { new { type = "and", filters = Array.Empty<object>() } }
                },
                sort = new { price = "asc" }
            };
        }
        
        private async Task ParseTradeResults(string jsonResult)
        {
            _currentListings.Clear();
            _marketItems.Clear();
            
            var fetchData = JsonSerializer.Deserialize<JsonElement>(jsonResult);
            
            if (!fetchData.TryGetProperty("result", out var items)) return;
            
            foreach (var item in items.EnumerateArray())
            {
                try
                {
                    if (!item.TryGetProperty("listing", out var listing)) continue;
                    if (!listing.TryGetProperty("price", out var price)) continue;
                    
                    var amount = price.GetProperty("amount").GetDecimal();
                    var currency = price.TryGetProperty("currency", out var curr) 
                        ? curr.GetString() ?? "chaos" : "chaos";
                    
                    string accountName = "Anônimo";
                    if (listing.TryGetProperty("account", out var account) && 
                        account.TryGetProperty("name", out var name))
                        accountName = name.GetString() ?? "Anônimo";
                    
                    string itemName = "";
                    var marketMods = new List<string>();
                    
                    if (item.TryGetProperty("item", out var itemData))
                    {
                        if (itemData.TryGetProperty("name", out var iName))
                            itemName = iName.GetString() ?? "";
                        if (string.IsNullOrEmpty(itemName) && itemData.TryGetProperty("typeLine", out var typeLine))
                            itemName = typeLine.GetString() ?? "";
                        
                        if (itemData.TryGetProperty("explicitMods", out var eMods))
                            foreach (var mod in eMods.EnumerateArray())
                                marketMods.Add(mod.GetString() ?? "");
                        
                        if (itemData.TryGetProperty("implicitMods", out var iMods))
                            foreach (var mod in iMods.EnumerateArray())
                                marketMods.Add(mod.GetString() ?? "");
                    }
                    
                    _currentListings.Add(new TradeListingInfo 
                    { 
                        Price = amount, 
                        Currency = currency, 
                        AccountName = accountName,
                        ItemName = itemName
                    });
                    
                    _marketItems.Add(new MarketItemInfo 
                    { 
                        Price = amount, 
                        Currency = currency, 
                        Mods = marketMods,
                        ItemName = itemName
                    });
                }
                catch { continue; }
            }
            
            await Task.CompletedTask;
        }
        
        private void ShowRateLimitMessage()
        {
            TxtPriceSummary.Text = "⏳ Aguarde";
            TxtPriceDetail.Text = "API limitada";
            TxtPriceRange.Text = "⚠️ RATE LIMIT\n\n" +
                "A API do PoE está limitando requisições.\n\n" +
                "💡 OPÇÕES:\n" +
                "• Aguarde 60 segundos e tente novamente\n" +
                "• Use o site oficial: pathofexile.com/trade2\n\n" +
                "O sistema usa cache de 3 minutos para evitar isso.";
        }
        
        private void DisplayTradeListings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("📋 ITENS À VENDA:");
            sb.AppendLine($"(Base: {_currentItem?.BaseType})\n");
            sb.AppendLine("═══════════════════════════════════════\n");
            
            int index = 1;
            foreach (var listing in _currentListings.Take(10))
            {
                decimal chaosValue = ConvertToChaos(listing.Price, listing.Currency);
                decimal displayPrice = ConvertFromChaos(chaosValue, _selectedCurrency);
                
                sb.AppendLine($"#{index}: {displayPrice:F1} {_selectedCurrency}");
                if (!string.IsNullOrEmpty(listing.ItemName))
                    sb.AppendLine($"    📦 {listing.ItemName}");
                sb.AppendLine($"    💰 {listing.Price} {listing.Currency}");
                sb.AppendLine($"    👤 {listing.AccountName}\n");
                index++;
            }
            
            TxtTradeListings.Text = sb.ToString();
        }
        
        private void DisplayPoeNinjaPrice(PoeNinjaItem item)
        {
            decimal chaosValue = (decimal)item.ChaosValue;
            decimal divineValue = (decimal)item.DivineValue;
            decimal displayPrice = ConvertFromChaos(chaosValue, _selectedCurrency);
            
            // Preço principal
            if (divineValue >= 1)
            {
                TxtPriceSummary.Text = $"{divineValue:F1} divine";
                TxtPriceDetail.Text = $"~{chaosValue:F0} chaos";
            }
            else
            {
                TxtPriceSummary.Text = $"{chaosValue:F0} chaos";
                TxtPriceDetail.Text = $"~{displayPrice:F1} {_selectedCurrency}";
            }
            
            // Criar listagem fake para compatibilidade
            _currentListings.Clear();
            _currentListings.Add(new TradeListingInfo 
            { 
                Price = chaosValue, 
                Currency = "chaos",
                ItemName = item.Name
            });
            
            // Market info
            TxtMarketCount.Text = $"({item.ListingCount} listagens)";
            
            // Análise detalhada
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("      💰 PREÇO POE.NINJA");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"📦 Item: {item.Name}");
            sb.AppendLine($"📋 Base: {item.BaseType}");
            sb.AppendLine($"🏷️ Tipo: {item.ItemType}\n");
            
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("      💵 VALORES");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"💰 Chaos: {chaosValue:F0}c");
            sb.AppendLine($"💎 Divine: {divineValue:F2} div");
            sb.AppendLine($"⚡ Exalted: {item.ExaltedValue:F2} ex\n");
            
            sb.AppendLine($"📊 Listagens: {item.ListingCount} vendedores\n");
            
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"  ✅ VENDA POR: {chaosValue:F0}c");
            if (divineValue >= 1)
                sb.AppendLine($"     ou ~{divineValue:F1} divine");
            sb.AppendLine("═══════════════════════════════════════");
            
            TxtPriceRange.Text = sb.ToString();
            
            // Trade listings
            var listSb = new StringBuilder();
            listSb.AppendLine("📋 INFORMAÇÕES DO POE.NINJA:");
            listSb.AppendLine($"(Item: {item.Name})\n");
            listSb.AppendLine("═══════════════════════════════════════\n");
            listSb.AppendLine($"💰 Preço médio: {chaosValue:F0} chaos");
            listSb.AppendLine($"📊 Vendedores: {item.ListingCount}\n");
            
            if (item.ExplicitModifiers.Any())
            {
                listSb.AppendLine("🔶 MODS DO ITEM:");
                foreach (var mod in item.ExplicitModifiers.Take(10))
                {
                    listSb.AppendLine($"  • {mod.Text}");
                }
            }
            
            TxtTradeListings.Text = listSb.ToString();
        }
        
        private async Task FetchSimilarItemsAsync(string baseType)
        {
            if (string.IsNullOrEmpty(baseType)) return;
            
            var similar = await _poeNinja.SearchSimilarItemsAsync(baseType, _currentLeague);
            
            _marketItems.Clear();
            foreach (var item in similar.Take(10))
            {
                _marketItems.Add(new MarketItemInfo
                {
                    Price = (decimal)item.ChaosValue,
                    Currency = "chaos",
                    ItemName = item.Name,
                    Mods = item.ExplicitModifiers.Select(m => m.Text).ToList()
                });
            }
        }
        
        private void DisplaySimilarItemsPrices(List<PoeNinjaItem> items)
        {
            var prices = items.Select(i => (decimal)i.ChaosValue).ToList();
            var min = prices.Min();
            var max = prices.Max();
            var avg = prices.Average();
            var median = GetMedian(prices);
            
            TxtPriceSummary.Text = $"~{median:F0}c";
            TxtPriceDetail.Text = $"Baseado em {items.Count} itens similares";
            TxtMarketCount.Text = $"({items.Sum(i => i.ListingCount)} total)";
            
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("      📊 ITENS SIMILARES NO POE.NINJA");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"📋 Base Type: {_currentItem?.BaseType}\n");
            sb.AppendLine($"📉 Mínimo: {min:F0}c");
            sb.AppendLine($"📈 Máximo: {max:F0}c");
            sb.AppendLine($"📊 Média: {avg:F0}c");
            sb.AppendLine($"📍 Mediana: {median:F0}c\n");
            
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("      ITENS ENCONTRADOS");
            sb.AppendLine("═══════════════════════════════════════\n");
            
            foreach (var item in items.OrderBy(i => i.ChaosValue).Take(10))
            {
                sb.AppendLine($"• {item.Name}: {item.ChaosValue:F0}c ({item.ListingCount} vendedores)");
            }
            
            TxtPriceRange.Text = sb.ToString();
            
            // Criar listagens
            _currentListings.Clear();
            _marketItems.Clear();
            foreach (var item in items)
            {
                _currentListings.Add(new TradeListingInfo
                {
                    Price = (decimal)item.ChaosValue,
                    Currency = "chaos",
                    ItemName = item.Name
                });
                _marketItems.Add(new MarketItemInfo
                {
                    Price = (decimal)item.ChaosValue,
                    Currency = "chaos",
                    ItemName = item.Name,
                    Mods = item.ExplicitModifiers.Select(m => m.Text).ToList()
                });
            }
            
            var listSb = new StringBuilder();
            listSb.AppendLine("📋 ITENS ÚNICOS COM MESMO BASE TYPE:\n");
            listSb.AppendLine("═══════════════════════════════════════\n");
            
            int idx = 1;
            foreach (var item in items.OrderBy(i => i.ChaosValue).Take(15))
            {
                listSb.AppendLine($"#{idx}: {item.Name}");
                listSb.AppendLine($"    💰 {item.ChaosValue:F0} chaos");
                listSb.AppendLine($"    📊 {item.ListingCount} vendedores\n");
                idx++;
            }
            
            TxtTradeListings.Text = listSb.ToString();
        }
        
        // ========== REMOVIDO: FetchTradeListingsAsync (usava API oficial com rate limit)
        
        private void UpdatePriceDisplay()
        {
            if (!_currentListings.Any()) return;
            
            var prices = _currentListings
                .Select(l => ConvertToChaos(l.Price, l.Currency))
                .ToList();
            
            var min = prices.Min();
            var max = prices.Max();
            var avg = prices.Average();
            var median = GetMedian(prices);
            
            var minD = ConvertFromChaos(min, _selectedCurrency);
            var maxD = ConvertFromChaos(max, _selectedCurrency);
            var avgD = ConvertFromChaos(avg, _selectedCurrency);
            var medianD = ConvertFromChaos(median, _selectedCurrency);
            
            // PREÇO SUGERIDO = Mediana
            TxtPriceSummary.Text = $"{medianD:F1} {_selectedCurrency}";
            TxtPriceDetail.Text = $"Min: {minD:F1} | Máx: {maxD:F1}";
            
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("      📊 ANÁLISE DE PREÇOS");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"📉 MÍNIMO: {minD:F2} {_selectedCurrency}");
            sb.AppendLine("   → Preço mais barato\n");
            sb.AppendLine($"📈 MÁXIMO: {maxD:F2} {_selectedCurrency}");
            sb.AppendLine("   → Preço mais caro\n");
            sb.AppendLine($"📊 MÉDIA: {avgD:F2} {_selectedCurrency}");
            sb.AppendLine("   → Soma ÷ quantidade\n");
            sb.AppendLine($"📍 MEDIANA: {medianD:F2} {_selectedCurrency}");
            sb.AppendLine("   → Valor do meio (recomendado!)\n");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"  ✅ VENDA POR: {medianD:F1} {_selectedCurrency}");
            sb.AppendLine("═══════════════════════════════════════");
            
            TxtPriceRange.Text = sb.ToString();
        }
        
        // ========== CONVERSÕES ==========
        
        private decimal ConvertToChaos(decimal amount, string currency)
        {
            return currency.ToLower() switch
            {
                "divine" => amount * _divineRate,
                "exalted" => amount * _exaltedRate,
                "alch" or "alchemy" => amount * 0.5m,
                "fusing" => amount * 0.3m,
                "vaal" => amount * 1.5m,
                _ => amount
            };
        }
        
        private decimal ConvertFromChaos(decimal chaosAmount, string targetCurrency)
        {
            return targetCurrency.ToLower() switch
            {
                "divine" => chaosAmount / _divineRate,
                "exalted" => chaosAmount / _exaltedRate,
                _ => chaosAmount
            };
        }
        
        private decimal GetMedian(List<decimal> values)
        {
            var sorted = values.OrderBy(x => x).ToList();
            int count = sorted.Count;
            if (count == 0) return 0;
            if (count % 2 == 0) return (sorted[count / 2 - 1] + sorted[count / 2]) / 2;
            return sorted[count / 2];
        }
        
        // ========== CALCULADORA ==========
        
        private void BtnCalculateSellChance_Click(object sender, RoutedEventArgs e)
        {
            if (!_currentListings.Any())
            {
                TxtSellChance.Text = "N/A";
                TxtPriceAdvice.Text = "⚠️ Avalie um item primeiro!";
                TxtCalcResult.Text = "Nenhum dado de mercado. Avalie um item na aba 'Avaliar Item'.";
                return;
            }
            
            if (!decimal.TryParse(TxtMyPrice.Text.Replace(",", "."), 
                System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, 
                out decimal myPrice) || myPrice <= 0)
            {
                TxtSellChance.Text = "??";
                TxtPriceAdvice.Text = "❌ Digite um preço válido!";
                return;
            }
            
            decimal myPriceInChaos = _selectedCurrency.ToLower() switch
            {
                "divine" => myPrice * _divineRate,
                "exalted" => myPrice * _exaltedRate,
                _ => myPrice
            };
            
            var marketPrices = _currentListings
                .Select(l => ConvertToChaos(l.Price, l.Currency))
                .OrderBy(p => p)
                .ToList();
            
            var result = CalculateSellProbability(myPriceInChaos, marketPrices);
            
            TxtSellChance.Text = $"{result.Probability:F0}%";
            ProgressSellChance.Value = result.Probability;
            TxtPriceAdvice.Text = result.Advice;
            
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("      📈 ANÁLISE DE PROBABILIDADE");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"💵 Seu Preço: {myPrice} {_selectedCurrency}");
            sb.AppendLine($"   (~{myPriceInChaos:F0} chaos)\n");
            sb.AppendLine($"📊 Mercado:");
            sb.AppendLine($"   Menor: {ConvertFromChaos(marketPrices.Min(), _selectedCurrency):F1} {_selectedCurrency}");
            sb.AppendLine($"   Média: {ConvertFromChaos(marketPrices.Average(), _selectedCurrency):F1} {_selectedCurrency}");
            sb.AppendLine($"   Mediana: {ConvertFromChaos(GetMedian(marketPrices), _selectedCurrency):F1} {_selectedCurrency}\n");
            sb.AppendLine($"🎯 Posição: #{result.Position} de {marketPrices.Count}");
            sb.AppendLine($"📈 Chance: {result.Probability:F0}%\n");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"  {result.Emoji} {result.Summary}");
            sb.AppendLine("═══════════════════════════════════════");
            
            TxtCalcResult.Text = sb.ToString();
        }
        
        private SellProbabilityResult CalculateSellProbability(decimal myPrice, List<decimal> marketPrices)
        {
            if (!marketPrices.Any())
                return new SellProbabilityResult { Probability = 50, Advice = "Sem dados.", Summary = "?", Emoji = "❓" };
            
            decimal minPrice = marketPrices.Min();
            decimal medianPrice = GetMedian(marketPrices);
            decimal avgPrice = marketPrices.Average();
            int position = marketPrices.Count(p => p < myPrice) + 1;
            
            double probability;
            string advice, summary, emoji;
            
            if (myPrice <= minPrice)
            {
                probability = 95;
                advice = "🚀 VENDA RÁPIDA! Considere aumentar um pouco.";
                summary = "Venda garantida!";
                emoji = "🔥";
            }
            else if (myPrice <= minPrice * 1.1m)
            {
                probability = 85;
                advice = "✅ Excelente preço!";
                summary = "Venda muito provável";
                emoji = "⭐";
            }
            else if (myPrice <= medianPrice)
            {
                probability = 70;
                advice = "👍 Bom preço, competitivo.";
                summary = "Boa chance";
                emoji = "👍";
            }
            else if (myPrice <= avgPrice * 1.2m)
            {
                probability = 45;
                advice = "⚠️ Acima da média. Pode demorar.";
                summary = "Venda possível";
                emoji = "⏳";
            }
            else
            {
                probability = 15;
                advice = "❌ Preço muito alto!";
                summary = "Difícil vender";
                emoji = "📉";
            }
            
            return new SellProbabilityResult 
            { 
                Probability = Math.Clamp(probability, 0, 100), 
                Position = position,
                Advice = advice, 
                Summary = summary, 
                Emoji = emoji 
            };
        }
        
        // ========== BUILDS ==========
        
        private async void BtnSearchBuild_Click(object sender, RoutedEventArgs e)
        {
            var searchTerm = TxtBuildSearch.Text.Trim();
            var selectedClass = (CmbBuildClass.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Witch";
            var phase = (CmbBuildPhase.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Leveling";
            
            if (string.IsNullOrEmpty(searchTerm))
            {
                MessageBox.Show("Digite o nome da build!", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            TxtBuildSkills.Text = "⏳ Buscando informações...";
            TxtBuildGear.Text = "⏳ Buscando informações...";
            TxtBuildPassives.Text = "⏳ Buscando informações...";
            TxtBuildAscendancy.Text = "⏳ Buscando informações...";
            
            await Task.Delay(500);
            ShowBuildInfo(searchTerm, selectedClass, phase);
        }
        
        private void ShowBuildInfo(string buildName, string className, string phase)
        {
            var skillsSb = new StringBuilder();
            skillsSb.AppendLine($"⚡ SKILLS: {buildName.ToUpper()}\n");
            skillsSb.AppendLine($"Fase: {phase}\n");
            skillsSb.AppendLine("═══════════════════════════════════════\n");
            
            if (buildName.ToLower().Contains("lich") || buildName.ToLower().Contains("storm"))
            {
                skillsSb.AppendLine("🔥 SKILLS PRINCIPAIS:");
                skillsSb.AppendLine("  • Skeletal Sniper - Dano principal");
                skillsSb.AppendLine("  • Flame Wall - Aplica ignite");
                skillsSb.AppendLine("  • Raging Spirits - DPS adicional");
                skillsSb.AppendLine("  • Unearth - Spawn de minions");
                skillsSb.AppendLine("  • Elemental Weakness - Curse\n");
            }
            else
            {
                skillsSb.AppendLine($"Busque guias em:\n• maxroll.gg/poe2/build-guides\n• poe.ninja/poe2/builds");
            }
            TxtBuildSkills.Text = skillsSb.ToString();
            
            var gearSb = new StringBuilder();
            gearSb.AppendLine($"🛡️ EQUIPAMENTOS: {buildName.ToUpper()}\n");
            gearSb.AppendLine("═══════════════════════════════════════\n");
            gearSb.AppendLine("📌 STATS PRIORITÁRIOS:");
            gearSb.AppendLine("  • +Level of Skills");
            gearSb.AppendLine("  • Resistências (cap 75%)");
            gearSb.AppendLine("  • Life/ES");
            TxtBuildGear.Text = gearSb.ToString();
            
            var passivesSb = new StringBuilder();
            passivesSb.AppendLine($"🌳 PASSIVAS: {buildName.ToUpper()}\n");
            passivesSb.AppendLine("═══════════════════════════════════════\n");
            passivesSb.AppendLine("💡 Use poe.ninja para ver árvores!");
            TxtBuildPassives.Text = passivesSb.ToString();
            
            var ascSb = new StringBuilder();
            ascSb.AppendLine($"⭐ ASCENDANCY: {className.ToUpper()}\n");
            ascSb.AppendLine("═══════════════════════════════════════\n");
            ascSb.AppendLine("Complete Trials of Ascendancy!");
            TxtBuildAscendancy.Text = ascSb.ToString();
        }
        
        // ========== COMPARATIVO - COM CORES ==========
        
        private void BtnCompareItems_Click(object sender, RoutedEventArgs e)
        {
            if (_currentItem == null)
            {
                TxtComparison.Text = "⚠️ Avalie um item primeiro na aba 'Avaliar Item'!";
                return;
            }
            
            if (!_marketItems.Any())
            {
                TxtComparison.Text = "⚠️ Nenhum item do mercado para comparar.\nAvalie um item primeiro para buscar dados do mercado.";
                return;
            }
            
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("  📊 COMPARAÇÃO COM O MERCADO");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"📦 Seu Item: {_currentItem.Name}");
            sb.AppendLine($"📋 Base: {_currentItem.BaseType}");
            sb.AppendLine($"📈 Comparando com {_marketItems.Count} itens\n");
            
            // Extrair valores dos mods do meu item
            var myModValues = new Dictionary<string, decimal>();
            foreach (var mod in _currentItem.ExplicitMods.Concat(_currentItem.ImplicitMods))
            {
                var value = ExtractModValue(mod.RawText);
                var modName = GetModBaseName(mod.RawText);
                if (value.HasValue && !string.IsNullOrEmpty(modName))
                    myModValues[modName] = value.Value;
            }
            
            // Coletar valores dos itens do mercado
            var marketModValues = new Dictionary<string, List<decimal>>();
            foreach (var marketItem in _marketItems)
            {
                foreach (var modText in marketItem.Mods)
                {
                    var value = ExtractModValue(modText);
                    var modName = GetModBaseName(modText);
                    if (!string.IsNullOrEmpty(modName) && value.HasValue)
                    {
                        if (!marketModValues.ContainsKey(modName))
                            marketModValues[modName] = new List<decimal>();
                        marketModValues[modName].Add(value.Value);
                    }
                }
            }
            
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("  SEUS STATS vs MERCADO");
            sb.AppendLine("═══════════════════════════════════════\n");
            
            int betterCount = 0;
            int worseCount = 0;
            
            foreach (var myMod in myModValues)
            {
                if (marketModValues.TryGetValue(myMod.Key, out var marketValues) && marketValues.Any())
                {
                    var marketAvg = marketValues.Average();
                    var marketMax = marketValues.Max();
                    
                    string status;
                    if (myMod.Value >= marketMax)
                    {
                        status = "🟢 MELHOR";
                        betterCount++;
                    }
                    else if (myMod.Value >= marketAvg)
                    {
                        status = "🟡 BOM";
                    }
                    else
                    {
                        status = "🔴 PIOR";
                        worseCount++;
                    }
                    
                    sb.AppendLine($"{status} {myMod.Key}");
                    sb.AppendLine($"   Seu: {myMod.Value:F0} | Média: {marketAvg:F0} | Máx: {marketMax:F0}\n");
                }
                else
                {
                    sb.AppendLine($"🟢 ÚNICO: {myMod.Key}");
                    sb.AppendLine($"   Valor: {myMod.Value:F0} (não encontrado em outros)\n");
                    betterCount++;
                }
            }
            
            // Mods que o mercado tem mas eu não
            var missingMods = marketModValues.Keys
                .Where(k => !myModValues.ContainsKey(k))
                .Take(5)
                .ToList();
            
            if (missingMods.Any())
            {
                sb.AppendLine("\n═══════════════════════════════════════");
                sb.AppendLine("  🔴 MODS QUE VOCÊ NÃO TEM");
                sb.AppendLine("═══════════════════════════════════════\n");
                foreach (var mod in missingMods)
                {
                    var avgVal = marketModValues[mod].Average();
                    sb.AppendLine($"  • {mod}");
                    sb.AppendLine($"    Média no mercado: {avgVal:F0}\n");
                }
            }
            
            sb.AppendLine("\n═══════════════════════════════════════");
            sb.AppendLine("  📊 RESUMO");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"  🟢 Stats melhores: {betterCount}");
            sb.AppendLine($"  🔴 Stats piores: {worseCount}");
            
            if (betterCount > worseCount)
                sb.AppendLine($"\n  ✅ Seu item é ACIMA DA MÉDIA!");
            else if (worseCount > betterCount)
                sb.AppendLine($"\n  ⚠️ Seu item é ABAIXO DA MÉDIA");
            else
                sb.AppendLine($"\n  📊 Seu item é MEDIANO");
            
            TxtComparison.Text = sb.ToString();
        }
        
        private decimal? ExtractModValue(string modText)
        {
            var matches = Regex.Matches(modText, @"[\+\-]?(\d+\.?\d*)");
            if (matches.Count > 0 && decimal.TryParse(matches[0].Groups[1].Value, 
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
                return value;
            return null;
        }
        
        private string GetModBaseName(string modText)
        {
            return Regex.Replace(modText, @"[\+\-]?\d+\.?\d*%?", "#").Trim();
        }
        
        // ========== HISTÓRICO ==========
        
        private async void BtnFetchHistory_Click(object sender, RoutedEventArgs e)
        {
            if (_currentItem == null)
            {
                TxtSalesHistory.Text = "⚠️ Avalie um item primeiro!";
                TxtSoldCount.Text = "--";
                return;
            }
            
            TxtSalesHistory.Text = "⏳ Analisando mercado...";
            TxtSoldCount.Text = "...";
            
            await Task.Delay(300);
            
            if (!_currentListings.Any())
            {
                TxtSalesHistory.Text = "⚠️ Nenhum dado de mercado.\nAvalie o item novamente.";
                TxtSoldCount.Text = "0";
                return;
            }
            
            var totalResults = _currentListings.Count;
            TxtSoldCount.Text = $"{totalResults}";
            
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("    📊 ANÁLISE DO MERCADO");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"📦 Item: {_currentItem.Name}");
            sb.AppendLine($"📋 Base: {_currentItem.BaseType}\n");
            sb.AppendLine($"📈 Listagens encontradas: {totalResults}\n");
            
            var prices = _currentListings.Select(l => ConvertToChaos(l.Price, l.Currency)).ToList();
            var avgPrice = ConvertFromChaos(prices.Average(), _selectedCurrency);
            var minPrice = ConvertFromChaos(prices.Min(), _selectedCurrency);
            var maxPrice = ConvertFromChaos(prices.Max(), _selectedCurrency);
            var medianPrice = ConvertFromChaos(GetMedian(prices), _selectedCurrency);
            
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("    💰 PREÇOS");
            sb.AppendLine("═══════════════════════════════════════\n");
            sb.AppendLine($"📉 Mínimo: {minPrice:F1} {_selectedCurrency}");
            sb.AppendLine($"📈 Máximo: {maxPrice:F1} {_selectedCurrency}");
            sb.AppendLine($"📊 Média: {avgPrice:F1} {_selectedCurrency}");
            sb.AppendLine($"📍 Mediana: {medianPrice:F1} {_selectedCurrency}\n");
            
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("    📊 DEMANDA");
            sb.AppendLine("═══════════════════════════════════════\n");
            
            if (totalResults > 50)
            {
                sb.AppendLine("🔴 DEMANDA: ALTA OFERTA");
                sb.AppendLine("💡 Muita competição no mercado.");
                sb.AppendLine("💡 Preço abaixo da mediana para vender rápido.");
            }
            else if (totalResults > 20)
            {
                sb.AppendLine("🟡 DEMANDA: MÉDIA");
                sb.AppendLine("💡 Mercado equilibrado.");
                sb.AppendLine("💡 Venda pela mediana.");
            }
            else if (totalResults > 5)
            {
                sb.AppendLine("🟢 DEMANDA: BAIXA OFERTA");
                sb.AppendLine("💡 Poucos vendedores.");
                sb.AppendLine("💡 Pode cobrar acima da mediana!");
            }
            else
            {
                sb.AppendLine("⭐ DEMANDA: MUITO RARO");
                sb.AppendLine("💡 Item escasso no mercado!");
                sb.AppendLine("💡 Preço premium possível.");
            }
            
            sb.AppendLine($"\n═══════════════════════════════════════");
            sb.AppendLine($"  ✅ SUGESTÃO: {medianPrice:F1} {_selectedCurrency}");
            sb.AppendLine($"═══════════════════════════════════════");
            
            TxtSalesHistory.Text = sb.ToString();
        }
        
        // ========== CONFIGURAÇÕES ==========
        
        private void ChkTopmost_Changed(object sender, RoutedEventArgs e)
        {
            this.Topmost = ChkTopmost.IsChecked ?? true;
        }
        
        private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTheme.SelectedItem is ComboBoxItem item && item.Tag != null)
                ApplyTheme(item.Tag.ToString() ?? "dark");
        }
        
        private void BtnAccentColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(btn.Tag.ToString()!));
                    Resources["AccentColor"] = brush;
                }
                catch { }
            }
        }
        
        private void ApplyTheme(string theme)
        {
            Color bg, card, primary;
            switch (theme)
            {
                case "blue": bg = Color.FromRgb(10, 25, 47); card = Color.FromRgb(23, 42, 69); primary = Color.FromRgb(74, 159, 255); break;
                case "purple": bg = Color.FromRgb(30, 20, 50); card = Color.FromRgb(50, 30, 80); primary = Color.FromRgb(168, 85, 247); break;
                case "green": bg = Color.FromRgb(15, 30, 20); card = Color.FromRgb(25, 50, 35); primary = Color.FromRgb(74, 222, 128); break;
                case "red": bg = Color.FromRgb(30, 15, 15); card = Color.FromRgb(50, 25, 25); primary = Color.FromRgb(239, 68, 68); break;
                case "orange": bg = Color.FromRgb(30, 20, 10); card = Color.FromRgb(50, 35, 20); primary = Color.FromRgb(251, 146, 60); break;
                default: bg = Color.FromRgb(26, 26, 46); card = Color.FromRgb(15, 52, 96); primary = Color.FromRgb(233, 69, 96); break;
            }
            
            Resources["BackgroundColor"] = new SolidColorBrush(bg);
            Resources["CardColor"] = new SolidColorBrush(card);
            Resources["PrimaryColor"] = new SolidColorBrush(primary);
            this.Background = new SolidColorBrush(bg);
        }
        
        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _config.HotkeyText = TxtHotkey.Text;
                _config.AutoEvaluate = ChkAutoEvaluate.IsChecked ?? false;
                _config.ShowPriceRange = ChkShowPriceRange.IsChecked ?? true;
                if (decimal.TryParse(TxtDivineRate.Text, out var dr)) { _config.DivineRate = dr; _divineRate = dr; }
                if (decimal.TryParse(TxtExaltedRate.Text, out var er)) { _config.ExaltedRate = er; _exaltedRate = er; }
                _configService.Save(_config);
                MessageBox.Show("✅ Configurações salvas!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Erro: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void BtnResetConfig_Click(object sender, RoutedEventArgs e)
        {
            TxtHotkey.Text = "Alt+E";
            TxtDivineRate.Text = "150";
            TxtExaltedRate.Text = "20";
            ChkAutoEvaluate.IsChecked = true;
            ChkShowPriceRange.IsChecked = true;
            ChkTopmost.IsChecked = true;
            this.Topmost = true;
            _divineRate = 150m;
            _exaltedRate = 20m;
        }
        
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
        
        public void ShowWindow()
        {
            this.Show();
            this.Activate();
            this.Topmost = true;
        }
    }
    
    // ========== HELPER CLASSES ==========
    
    public class TradeListingInfo
    {
        public decimal Price { get; set; }
        public string Currency { get; set; } = "chaos";
        public string AccountName { get; set; } = "";
        public string ItemName { get; set; } = "";
    }
    
    public class MarketItemInfo
    {
        public decimal Price { get; set; }
        public string Currency { get; set; } = "chaos";
        public List<string> Mods { get; set; } = new();
        public string ItemName { get; set; } = "";
    }
    
    public class SellProbabilityResult
    {
        public double Probability { get; set; }
        public int Position { get; set; }
        public string Advice { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Emoji { get; set; } = "";
    }
}

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using PoE2Inspector.Services.Logging;

namespace PoE2Inspector.Services.PoeNinja;

public class PoeNinjaItem
{
    public string Name { get; set; } = "";
    public string BaseType { get; set; } = "";
    public double ChaosValue { get; set; }
    public double DivineValue { get; set; }
    public double ExaltedValue { get; set; }
    public int ListingCount { get; set; }
    public string ItemType { get; set; } = "";
    public List<ModInfo> ExplicitModifiers { get; set; } = new();
    public List<ModInfo> ImplicitModifiers { get; set; } = new();
}

public class ModInfo
{
    public string Text { get; set; } = "";
    public bool Optional { get; set; }
}

public interface IPoeNinjaService
{
    Task<List<PoeNinjaItem>> GetUniqueWeaponsAsync(string league = "Standard");
    Task<List<PoeNinjaItem>> GetUniqueArmoursAsync(string league = "Standard");
    Task<List<PoeNinjaItem>> GetUniqueAccessoriesAsync(string league = "Standard");
    Task<List<PoeNinjaItem>> GetUniqueJewelsAsync(string league = "Standard");
    Task<List<PoeNinjaItem>> GetUniqueFlasksAsync(string league = "Standard");
    Task<PoeNinjaItem?> FindItemByNameAsync(string name, string league = "Standard");
    Task<List<PoeNinjaItem>> SearchSimilarItemsAsync(string baseType, string league = "Standard");
}

public class PoeNinjaService : IPoeNinjaService
{
    private readonly ILogService _log;
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string POE_NINJA_API = "https://poe.ninja/api/data/itemoverview";
    
    // Cache to avoid repeated API calls
    private readonly Dictionary<string, (List<PoeNinjaItem> Items, DateTime CachedAt)> _cache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(10);

    public PoeNinjaService(ILogService log)
    {
        _log = log;
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PoE2Inspector/2.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
    }

    private async Task<List<PoeNinjaItem>> FetchItemsAsync(string league, string type)
    {
        var cacheKey = $"{league}_{type}";
        
        // Check cache
        if (_cache.TryGetValue(cacheKey, out var cached) && 
            DateTime.Now - cached.CachedAt < _cacheExpiry)
        {
            _log.Info($"[PoeNinja] Using cached data for {type} ({cached.Items.Count} items)");
            return cached.Items;
        }

        try
        {
            var url = $"{POE_NINJA_API}?league={Uri.EscapeDataString(league)}&type={type}";
            _log.Info($"[PoeNinja] Fetching {type} from {url}");
            
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                _log.Error($"[PoeNinja] API Error: {response.StatusCode}");
                return new List<PoeNinjaItem>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var items = ParseItemsFromJson(json);
            
            // Cache the results
            _cache[cacheKey] = (items, DateTime.Now);
            _log.Info($"[PoeNinja] Loaded {items.Count} {type} items");
            
            return items;
        }
        catch (Exception ex)
        {
            _log.Error($"[PoeNinja] Error fetching {type}: {ex.Message}");
            return new List<PoeNinjaItem>();
        }
    }

    private List<PoeNinjaItem> ParseItemsFromJson(string json)
    {
        var items = new List<PoeNinjaItem>();
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("lines", out var lines))
                return items;

            foreach (var line in lines.EnumerateArray())
            {
                var item = new PoeNinjaItem
                {
                    Name = line.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    BaseType = line.TryGetProperty("baseType", out var bt) ? bt.GetString() ?? "" : "",
                    ChaosValue = line.TryGetProperty("chaosValue", out var cv) ? cv.GetDouble() : 0,
                    DivineValue = line.TryGetProperty("divineValue", out var dv) ? dv.GetDouble() : 0,
                    ExaltedValue = line.TryGetProperty("exaltedValue", out var ev) ? ev.GetDouble() : 0,
                    ListingCount = line.TryGetProperty("listingCount", out var lc) ? lc.GetInt32() : 0,
                    ItemType = line.TryGetProperty("itemType", out var it) ? it.GetString() ?? "" : ""
                };

                // Parse explicit modifiers
                if (line.TryGetProperty("explicitModifiers", out var explicitMods))
                {
                    foreach (var mod in explicitMods.EnumerateArray())
                    {
                        item.ExplicitModifiers.Add(new ModInfo
                        {
                            Text = mod.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                            Optional = mod.TryGetProperty("optional", out var o) && o.GetBoolean()
                        });
                    }
                }

                // Parse implicit modifiers
                if (line.TryGetProperty("implicitModifiers", out var implicitMods))
                {
                    foreach (var mod in implicitMods.EnumerateArray())
                    {
                        item.ImplicitModifiers.Add(new ModInfo
                        {
                            Text = mod.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                            Optional = mod.TryGetProperty("optional", out var o) && o.GetBoolean()
                        });
                    }
                }

                items.Add(item);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[PoeNinja] Parse error: {ex.Message}");
        }

        return items;
    }

    public async Task<List<PoeNinjaItem>> GetUniqueWeaponsAsync(string league = "Standard")
    {
        return await FetchItemsAsync(league, "UniqueWeapon");
    }

    public async Task<List<PoeNinjaItem>> GetUniqueArmoursAsync(string league = "Standard")
    {
        return await FetchItemsAsync(league, "UniqueArmour");
    }

    public async Task<List<PoeNinjaItem>> GetUniqueAccessoriesAsync(string league = "Standard")
    {
        return await FetchItemsAsync(league, "UniqueAccessory");
    }

    public async Task<List<PoeNinjaItem>> GetUniqueJewelsAsync(string league = "Standard")
    {
        return await FetchItemsAsync(league, "UniqueJewel");
    }

    public async Task<List<PoeNinjaItem>> GetUniqueFlasksAsync(string league = "Standard")
    {
        return await FetchItemsAsync(league, "UniqueFlask");
    }

    public async Task<PoeNinjaItem?> FindItemByNameAsync(string name, string league = "Standard")
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var normalizedName = name.Trim().ToLowerInvariant();
        _log.Info($"[PoeNinja] Searching for item: {name}");

        // Search in all categories
        var categories = new[]
        {
            ("UniqueWeapon", GetUniqueWeaponsAsync(league)),
            ("UniqueArmour", GetUniqueArmoursAsync(league)),
            ("UniqueAccessory", GetUniqueAccessoriesAsync(league)),
            ("UniqueJewel", GetUniqueJewelsAsync(league)),
            ("UniqueFlask", GetUniqueFlasksAsync(league))
        };

        foreach (var (categoryName, task) in categories)
        {
            var items = await task;
            
            // Exact match first
            var match = items.Find(i => 
                i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            
            if (match != null)
            {
                _log.Info($"[PoeNinja] Found exact match in {categoryName}: {match.Name} = {match.ChaosValue}c");
                return match;
            }

            // Partial match
            match = items.Find(i => 
                i.Name.ToLowerInvariant().Contains(normalizedName) ||
                normalizedName.Contains(i.Name.ToLowerInvariant()));

            if (match != null)
            {
                _log.Info($"[PoeNinja] Found partial match in {categoryName}: {match.Name} = {match.ChaosValue}c");
                return match;
            }
        }

        _log.Info($"[PoeNinja] No match found for: {name}");
        return null;
    }

    public async Task<List<PoeNinjaItem>> SearchSimilarItemsAsync(string baseType, string league = "Standard")
    {
        if (string.IsNullOrWhiteSpace(baseType))
            return new List<PoeNinjaItem>();

        var normalizedBase = baseType.Trim().ToLowerInvariant();
        var results = new List<PoeNinjaItem>();

        _log.Info($"[PoeNinja] Searching similar items for base type: {baseType}");

        // Search all categories
        var allItems = new List<PoeNinjaItem>();
        allItems.AddRange(await GetUniqueWeaponsAsync(league));
        allItems.AddRange(await GetUniqueArmoursAsync(league));
        allItems.AddRange(await GetUniqueAccessoriesAsync(league));

        // Find items with matching base type
        foreach (var item in allItems)
        {
            if (item.BaseType.ToLowerInvariant().Contains(normalizedBase) ||
                normalizedBase.Contains(item.BaseType.ToLowerInvariant()))
            {
                results.Add(item);
            }
        }

        _log.Info($"[PoeNinja] Found {results.Count} similar items for {baseType}");
        return results;
    }
}

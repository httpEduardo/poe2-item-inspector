using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PoE2Inspector.Domain;
using PoE2Inspector.Services.Logging;

namespace PoE2Inspector.Services.Parser;

public class ItemParser : IItemParser
{
    private readonly ILogService _log;

    public ItemParser(ILogService log)
    {
        _log = log;
    }

    public Item Parse(string rawText)
    {
        try
        {
            var item = new Item { RawText = rawText };
            
            // Limpar o texto e separar por linhas
            var cleanText = rawText.Trim();
            var lines = cleanText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            
            _log.Info($"Parsing item with {lines.Length} lines");
            
            // Primeiro, vamos encontrar as seções separadas por "--------"
            var sections = new List<List<string>>();
            var currentSection = new List<string>();
            
            foreach (var line in lines)
            {
                if (line.StartsWith("--------"))
                {
                    if (currentSection.Count > 0)
                    {
                        sections.Add(currentSection);
                        currentSection = new List<string>();
                    }
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    currentSection.Add(line.Trim());
                }
            }
            
            if (currentSection.Count > 0)
                sections.Add(currentSection);
            
            _log.Info($"Found {sections.Count} sections");
            
            // Processar seções
            if (sections.Count > 0)
            {
                ParseHeaderSection(sections[0], item);
            }
            
            // Procurar Item Level e processar outras seções
            bool foundImplicits = false;
            for (int i = 1; i < sections.Count; i++)
            {
                var section = sections[i];
                
                foreach (var line in section)
                {
                    // Item Level
                    if (line.StartsWith("Item Level:", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(line, @"Item Level:\s*(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var ilvl))
                        {
                            item.ItemLevel = ilvl;
                        }
                        continue;
                    }
                    
                    // Requirements
                    if (line.StartsWith("Requirements:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (line.StartsWith("Level:", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(line, @"Level:\s*(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var lvl))
                            item.RequiredLevel = lvl;
                        continue;
                    }
                    
                    // Properties com valores
                    if (line.Contains(":") && !IsModLine(line))
                    {
                        ParseProperty(line, item);
                        continue;
                    }
                    
                    // Mods - detectar se é implicit ou explicit
                    if (IsModLine(line))
                    {
                        var mod = new ModLine
                        {
                            RawText = line,
                            Type = foundImplicits ? ModType.Explicit : ModType.Implicit
                        };
                        
                        var numbers = Regex.Matches(line, @"[-+]?\d+(?:\.\d+)?")
                            .Cast<Match>()
                            .Select(m => double.Parse(m.Value))
                            .ToArray();
                        mod.Values = numbers;
                        
                        // Se encontrar "(implicit)" na linha ou seção anterior teve separador
                        if (line.Contains("(implicit)") || !foundImplicits)
                        {
                            item.ImplicitMods.Add(mod);
                        }
                        else
                        {
                            item.ExplicitMods.Add(mod);
                        }
                    }
                }
                
                // Após primeira seção de mods, próximas são explicits
                if (section.Any(l => IsModLine(l)))
                    foundImplicits = true;
            }
            
            _log.Info($"Parsed item: Name='{item.Name}', Base='{item.BaseType}', Rarity='{item.Rarity}'");
            _log.Info($"Implicits: {item.ImplicitMods.Count}, Explicits: {item.ExplicitMods.Count}");
            
            return item;
        }
        catch (Exception ex)
        {
            _log.Error($"Error parsing item text: {ex.Message}", ex);
            return new Item { RawText = rawText, Name = "Parse Error", BaseType = "Unknown" };
        }
    }
    
    private void ParseHeaderSection(List<string> headerLines, Item item)
    {
        foreach (var line in headerLines)
        {
            // Ignorar Item Class
            if (line.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase))
                continue;
            
            // Rarity
            if (line.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
            {
                item.Rarity = line.Substring(8).Trim();
                continue;
            }
            
            // Nome e Base Type
            if (string.IsNullOrEmpty(item.Name))
            {
                item.Name = line;
            }
            else if (string.IsNullOrEmpty(item.BaseType))
            {
                item.BaseType = line;
            }
        }
        
        // Se só tem nome, usar como base type também
        if (!string.IsNullOrEmpty(item.Name) && string.IsNullOrEmpty(item.BaseType))
        {
            item.BaseType = item.Name;
        }
    }
    
    private bool IsModLine(string line)
    {
        // Mods geralmente têm números com + ou % ou palavras específicas
        if (Regex.IsMatch(line, @"[+-]\d+%?"))
            return true;
        if (line.Contains("increased") || line.Contains("reduced") || 
            line.Contains("added") || line.Contains("to ") ||
            line.Contains("Adds ") || line.Contains("Grants "))
            return true;
        return false;
    }

    private void ParseProperty(string line, Item item)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex > 0)
        {
            var label = line.Substring(0, colonIndex).Trim();
            var value = line.Substring(colonIndex + 1).Trim();

            if (label.Equals("Sockets", StringComparison.OrdinalIgnoreCase))
            {
                var socketCount = value.Count(c => !char.IsWhiteSpace(c) && c != '-');
                value = socketCount.ToString();
            }

            item.Properties.Add(new StatLine { Label = label, Value = value });
        }
    }
}

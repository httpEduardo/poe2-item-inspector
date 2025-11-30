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
            var lines = rawText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var section = ParseSection.Header;
            var currentModType = ModType.Implicit;

            foreach (var line in lines)
            {

                if (line.StartsWith("--------"))
                {
                    section = NextSection(section, ref currentModType);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                switch (section)
                {
                    case ParseSection.Header:
                        ParseHeader(line, item);
                        break;
                    case ParseSection.Properties:
                        ParseProperty(line, item);
                        break;
                    case ParseSection.Requirements:
                        ParseRequirement(line, item);
                        break;
                    case ParseSection.Mods:
                        ParseMod(line, item, currentModType);
                        break;
                }
            }

            _log.Info($"Parsed item: {item.Name} ({item.BaseType})");
            return item;
        }
        catch (Exception ex)
        {
            _log.Error($"Error parsing item text", ex);
            return new Item { RawText = rawText, Name = "Parse Error" };
        }
    }

    private void ParseHeader(string line, Item item)
    {
        if (line.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
        {
            item.Rarity = line.Substring(8).Trim();
        }
        else if (string.IsNullOrEmpty(item.Name))
        {
            item.Name = line.Trim();
        }
        else if (string.IsNullOrEmpty(item.BaseType))
        {
            item.BaseType = line.Trim();
        }
    }

    private void ParseProperty(string line, Item item)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex > 0)
        {
            var label = line.Substring(0, colonIndex).Trim();
            var value = line.Substring(colonIndex + 1).Trim();

            if (label.Equals("Item Level", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out var ilvl))
                {
                    item.ItemLevel = ilvl;
                }
            }

            if (label.Equals("Sockets", StringComparison.OrdinalIgnoreCase))
            {

                var socketCount = value.Count(c => !char.IsWhiteSpace(c) && c != '-');
                _log.Info($"Sockets conversion: '{value}' -> {socketCount}");
                value = socketCount.ToString();
            }

            item.Properties.Add(new StatLine { Label = label, Value = value });
        }
    }

    private void ParseRequirement(string line, Item item)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex > 0)
        {
            var label = line.Substring(0, colonIndex).Trim();
            var value = line.Substring(colonIndex + 1).Trim();

            if (label.Equals("Level", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out var level))
                {
                    item.RequiredLevel = level;
                }
            }

            item.Requirements.Add(new StatLine { Label = label, Value = value });
        }
    }

    private void ParseMod(string line, Item item, ModType modType)
    {
        var mod = new ModLine
        {
            Type = modType,
            RawText = line.Trim()
        };

        var numbers = Regex.Matches(line, @"[-+]?\d+(?:\.\d+)?")
            .Cast<Match>()
            .Select(m => double.Parse(m.Value))
            .ToArray();

        mod.Values = numbers;

        var template = line;
        foreach (var match in Regex.Matches(line, @"[-+]?\d+(?:\.\d+)?").Cast<Match>().Reverse())
        {
            template = template.Remove(match.Index, match.Length).Insert(match.Index, "#");
        }
        mod.Template = template;

        switch (modType)
        {
            case ModType.Implicit:
                item.ImplicitMods.Add(mod);
                break;
            case ModType.Explicit:
                item.ExplicitMods.Add(mod);
                break;
            case ModType.Enchant:
                item.EnchantMods.Add(mod);
                break;
            default:
                item.OtherMods.Add(mod);
                break;
        }
    }

    private ParseSection NextSection(ParseSection current, ref ModType currentModType)
    {
        switch (current)
        {
            case ParseSection.Header:
                return ParseSection.Properties;
            case ParseSection.Properties:
                return ParseSection.Requirements;
            case ParseSection.Requirements:
                currentModType = ModType.Implicit;
                return ParseSection.Mods;
            case ParseSection.Mods:

                if (currentModType == ModType.Implicit)
                {
                    currentModType = ModType.Explicit;
                }
                return ParseSection.Mods;
            default:
                return current;
        }
    }

    private enum ParseSection
    {
        Header,
        Properties,
        Requirements,
        Mods
    }
}

using System.Collections.Generic;

namespace PoE2Inspector.Domain;

public class Item
{
    public string RawText { get; set; } = string.Empty;

    public string Rarity { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseType { get; set; } = string.Empty;

    public int? ItemLevel { get; set; }
    public int? RequiredLevel { get; set; }

    public List<StatLine> Properties { get; set; } = new();
    public List<StatLine> Requirements { get; set; } = new();

    public List<ModLine> ImplicitMods { get; set; } = new();
    public List<ModLine> ExplicitMods { get; set; } = new();
    public List<ModLine> EnchantMods { get; set; } = new();
    public List<ModLine> OtherMods { get; set; } = new();
}

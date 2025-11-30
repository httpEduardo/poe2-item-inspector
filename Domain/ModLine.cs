using System;

namespace PoE2Inspector.Domain;

public enum ModType
{
    Implicit,
    Explicit,
    Enchant,
    Other
}

public class ModLine
{
    public ModType Type { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public double[] Values { get; set; } = Array.Empty<double>();
}

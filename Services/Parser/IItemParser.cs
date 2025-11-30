using PoE2Inspector.Domain;

namespace PoE2Inspector.Services.Parser;

public interface IItemParser
{
    Item Parse(string rawText);
}

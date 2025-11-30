using PoE2Inspector.Domain;

namespace PoE2Inspector.Services.Trade;

public interface ITradeService
{
    string BuildTradeUrl(Item item);
    void OpenTradeSearch(Item item);
}

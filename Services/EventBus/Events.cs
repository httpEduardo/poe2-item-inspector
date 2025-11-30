using PoE2Inspector.Domain;

namespace PoE2Inspector.Services.EventBus;

public record HotkeyEvent(HotkeyAction Action);

public record ItemTextCapturedEvent(string RawText);

public record ItemParsedEvent(Item Item);

public enum HotkeyAction
{
    InspectItem
}

using System;

namespace PoE2Inspector.Services.EventBus;

public interface IEventBus
{
    void Publish<T>(T evt);
    void Subscribe<T>(Action<T> handler);
    void Unsubscribe<T>(Action<T> handler);
}

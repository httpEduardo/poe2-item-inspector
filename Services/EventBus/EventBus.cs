using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace PoE2Inspector.Services.EventBus;

public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<T>(T evt)
    {
        if (evt == null) return;

        var eventType = typeof(T);
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            var handlersCopy = handlers.ToList();
            foreach (var handler in handlersCopy)
            {
                try
                {
                    ((Action<T>)handler)(evt);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in event handler: {ex.Message}");
                }
            }
        }
    }

    public void Subscribe<T>(Action<T> handler)
    {
        var eventType = typeof(T);
        _handlers.AddOrUpdate(
            eventType,
            _ => new List<Delegate> { handler },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(handler);
                }
                return list;
            });
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        var eventType = typeof(T);
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        }
    }
}

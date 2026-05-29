using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectOne.Utils;

namespace ProjectOne.Event
{
  public class EventManager : Singleton<EventManager>
  {
    private readonly Dictionary<Type, object> channels = new Dictionary<Type, object>();

    public void Subscribe<T>(Action<T> handler)
    {
      GetChannel<T>().Subscribe(handler);
    }

    public void Publish<T>(T eventData)
    {
      GetChannel<T>().Publish(eventData);
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
      GetChannel<T>().Unsubscribe(handler);
    }

    private IEventChannel<T> GetChannel<T>()
    {
        var type = typeof(T);

        if (!channels.TryGetValue(type, out var channel))
        {
            channel = new EventChannel<T>();
            channels[type] = channel;
        }

        return (IEventChannel<T>)channel;
    }
  }
}
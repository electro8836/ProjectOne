using System;

namespace ProjectOne.Event
{
    public interface IEventChannel<T>
    {
        void Publish(T eventData);
        void Subscribe(Action<T> handler);
        void Unsubscribe(Action<T> handler);
    }

    public class EventChannel<T> : IEventChannel<T>
    {
        private event Action<T> onEvent;
        
        public void Publish(T eventData)
        {
            if (onEvent != null)
            {
                onEvent.Invoke(eventData);
            }
        }

        public void Subscribe(Action<T> handler)
        {
            onEvent += handler;
        }

        public void Unsubscribe(Action<T> handler)
        {
            onEvent -= handler;
        }
    }
}

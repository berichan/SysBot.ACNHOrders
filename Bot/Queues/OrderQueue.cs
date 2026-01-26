using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public class OrderQueue<T> : IEnumerable<T> where T : IACNHOrderNotifier<Item>
    {
        private readonly List<T> _orders = new();
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) { return _orders.Count; } }
        }

        public void Enqueue(T order)
        {
            lock (_lock)
            {
                _orders.Add(order);
            }
        }

        public bool TryDequeue(out T? result)
        {
            lock (_lock)
            {
                if (_orders.Count == 0)
                {
                    result = default;
                    return false;
                }
                result = _orders[0];
                _orders.RemoveAt(0);
                return true;
            }
        }

        public bool TryPeek(out T? result)
        {
            lock (_lock)
            {
                if (_orders.Count == 0)
                {
                    result = default;
                    return false;
                }
                result = _orders[0];
                return true;
            }
        }

        public bool RemoveByUserId(ulong userId)
        {
            lock (_lock)
            {
                int removed = _orders.RemoveAll(o => o.UserGuid == userId);
                return removed > 0;
            }
        }

        public int GetPosition(ulong userId)
        {
            lock (_lock)
            {
                for (int i = 0; i < _orders.Count; i++)
                {
                    if (_orders[i].UserGuid == userId)
                        return i + 1;
                }
                return -1;
            }
        }

        public T? GetByUserId(ulong userId)
        {
            lock (_lock)
            {
                return _orders.FirstOrDefault(o => o.UserGuid == userId);
            }
        }

        public T[] ToArray()
        {
            lock (_lock)
            {
                return _orders.ToArray();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
            {
                return _orders.ToList().GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

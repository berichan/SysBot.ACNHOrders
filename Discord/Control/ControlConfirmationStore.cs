using System;
using System.Collections.Generic;

namespace SysBot.ACNHOrders
{
    public sealed class ControlConfirmationStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, (ulong User, string Action, DateTimeOffset Expires)> _pending = new();

        public string Create(ulong user, string action, DateTimeOffset now)
        {
            lock (_sync)
            {
                foreach (var key in new List<string>(_pending.Keys))
                    if (_pending[key].Expires <= now || _pending[key].User == user) _pending.Remove(key);
                var token = Guid.NewGuid().ToString("N");
                _pending[token] = (user, action, now.AddMinutes(5));
                return token;
            }
        }

        public bool Consume(ulong user, string action, string token, DateTimeOffset now)
        {
            lock (_sync)
            {
                if (!_pending.TryGetValue(token, out var pending) || pending.User != user || pending.Action != action || pending.Expires <= now) return false;
                _pending.Remove(token);
                return true;
            }
        }
    }
}

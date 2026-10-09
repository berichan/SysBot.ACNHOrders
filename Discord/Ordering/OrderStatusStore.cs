using System;
using System.Collections.Generic;

namespace SysBot.ACNHOrders
{
    public enum OrderStage { Queued, Preparing, Ready, Visiting, Completed, Cancelled, Failed }
    public sealed record OrderStatus(ulong UserId, ulong OrderId, OrderStage Stage, string Message, DateTimeOffset UpdatedAt,
        string? Dodo = null, DateTimeOffset? Deadline = null, string ControlToken = "");

    public sealed class OrderStatusStore
    {
        public static OrderStatusStore Shared { get; } = new();
        private readonly object _sync = new();
        private readonly Dictionary<ulong, OrderStatus> _statuses = new();
        private ulong? _activeUser;

        public bool IsActive(ulong user) { lock (_sync) return _activeUser == user; }
        public bool HasActiveOrder { get { lock (_sync) return _activeUser.HasValue; } }

        public void Set(ulong user, ulong order, OrderStage stage, string message = "", string? dodo = null, DateTimeOffset? deadline = null)
        {
            lock (_sync)
            {
                _statuses.TryGetValue(user, out var previous);
                if (previous != null && previous.OrderId != order && stage is not OrderStage.Queued and not OrderStage.Preparing) return;
                if (stage is OrderStage.Preparing or OrderStage.Ready or OrderStage.Visiting) _activeUser = user;
                var controlToken = previous?.OrderId == order ? previous.ControlToken : Guid.NewGuid().ToString("N");
                _statuses[user] = new(user, order, stage, message, DateTimeOffset.UtcNow, dodo, deadline, controlToken);
            }
        }

        public OrderStatus? Get(ulong user)
        {
            lock (_sync)
            {
                if (!_statuses.TryGetValue(user, out var status)) return null;
                if (!IsActive(user) && status.Stage != OrderStage.Queued && DateTimeOffset.UtcNow - status.UpdatedAt > TimeSpan.FromDays(1))
                { _statuses.Remove(user); return null; }
                return status;
            }
        }

        public void Release(ulong user)
        {
            lock (_sync) if (_activeUser == user) _activeUser = null;
        }
    }
}

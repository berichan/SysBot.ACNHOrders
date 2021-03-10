using System;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public interface IACNHOrderNotifier<T> where T : Item, new()
    {
        public T[] Order { get; }
        public VillagerRequest? VillagerOrder { get; }
        public ulong UserGuid { get; }
        public ulong OrderID { get; }
        public string VillagerName { get; }
        public bool SkipRequested { get; }
        void OrderInitializing(CrossBot routine, string msg);
        void OrderReady(CrossBot routine, string msg, string dodo);
        void OrderCancelled(CrossBot routine, string msg, bool faulted);
        void OrderFinished(CrossBot routine, string msg);
        void SendNotification(CrossBot routine, string msg);
        Action<CrossBot>? OnFinish {set;}

    }
}

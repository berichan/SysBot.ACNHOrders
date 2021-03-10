using NHSE.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class DummyOrder<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        public T[] Order => new T[1];

        public ulong UserGuid => ulong.MaxValue;

        public string VillagerName => string.Empty;

        public Action<CrossBot>? OnFinish { private get; set; }

        public ulong OrderID => ulong.MaxValue;

        public VillagerRequest? VillagerOrder => null;

        public bool SkipRequested => false;

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
        }

        public void SendNotification(CrossBot routine, string msg)
        {
        }
    }
}

using NHSE.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class QueueHub
    {
        public readonly ConcurrentQueue<IACNHOrderNotifier<Item>> Orders = new();

        public static readonly QueueHub CurrentInstance = new();

        public QueueHub() { }
    }
}

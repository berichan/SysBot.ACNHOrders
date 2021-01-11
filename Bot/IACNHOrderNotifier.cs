using System;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public interface IACNHOrderNotifier<T> where T : MultiItem, new()
    {
        void OrderInitializing(CrossBot routine, string msg);
        void OrderReady(CrossBot routine, string msg);
        void OrderCancelled(CrossBot routine, string msg);
        void OrderFinished(CrossBot routine, string msg);
        void SendNotification(CrossBot routine, string msg);
        Action<CrossBot>? OnFinish {set;}

    }
}

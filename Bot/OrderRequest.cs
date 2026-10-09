using Discord;
using Discord.WebSocket;
using NHSE.Core;
using SysBot.Base;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class OrderRequest<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        public MultiItem ItemOrderData { get; }
        public ulong UserGuid { get; }
        public ulong OrderID { get; }
        public string VillagerName { get; }
        private SocketUser Trader { get; }
        private ISocketMessageChannel CommandSentChannel { get; }
        public Action<CrossBot>? OnFinish { private get; set; }
        public T[] Order { get; } // stupid but I cba to work on this part anymore
        public VillagerRequest? VillagerOrder { get; }

        public OrderRequest(MultiItem data, T[] order, ulong user, ulong orderId, SocketUser trader, ISocketMessageChannel commandSentChannel, VillagerRequest? vil)
        {
            ItemOrderData = data;
            UserGuid = user;
            OrderID = orderId;
            Trader = trader;
            CommandSentChannel = commandSentChannel;
            Order = order;
            VillagerName = trader.Username;
            VillagerOrder = vil;
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OrderStatusStore.Shared.Set(UserGuid, OrderID, faulted ? OrderStage.Failed : OrderStage.Cancelled, msg);
            OnFinish?.Invoke(routine);
            _ = NotifyAsync($"Your order could not be completed: {msg}");
            if (!faulted)
                CommandSentChannel.SendMessageAsync($"{Trader.Mention}: your order was cancelled. {msg}");
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            OrderStatusStore.Shared.Set(UserGuid, OrderID, OrderStage.Preparing, msg);
            _ = NotifyAsync($"Your order is starting. **Empty your inventory**, then talk to Orville and wait at the Dodo code entry screen. I will send your code shortly. {msg}");
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            OrderStatusStore.Shared.Set(UserGuid, OrderID, OrderStage.Ready, msg, dodo,
                DateTimeOffset.UtcNow.AddSeconds(routine.Config.OrderConfig.WaitForArriverTime * 0.9));
            _ = NotifyAsync($"The island is ready, {Trader.Mention}. Your Dodo code is **{dodo}**. {msg}");
        }
        public void OrderFinished(CrossBot routine, string msg)
        {
            OrderStatusStore.Shared.Set(UserGuid, OrderID, OrderStage.Completed, msg);
            OnFinish?.Invoke(routine);
            _ = NotifyAsync($"Your order is complete. Thanks for visiting! {msg}");
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            _ = NotifyAsync(msg);
        }
        private async Task NotifyAsync(string message)
        {
            try { await Trader.SendMessageAsync(message).ConfigureAwait(false); }
            catch (Exception ex)
            {
                LogUtil.LogError($"Could not deliver order notification for {UserGuid}: {ex.Message}", nameof(OrderRequest<T>));
            }
        }
    }
}

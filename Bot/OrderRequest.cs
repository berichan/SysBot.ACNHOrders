using Discord;
using Discord.WebSocket;
using NHSE.Core;
using System;
using System.Diagnostics;
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
        public bool SkipRequested { get; set; } = false;

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
            OnFinish?.Invoke(routine);
            Trader.SendMessageAsync($"Oops! Something has happened with your order: {msg}");
            if (!faulted)
                CommandSentChannel.SendMessageAsync($"{Trader.Mention} - Your order has been cancelled: {msg}");
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            Trader.SendMessageAsync($"Your order is starting, please **ensure your inventory is __empty__**, then go talk to Orville and stay on the Dodo code entry screen. I will send you the Dodo code shortly. {msg}");
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            Trader.SendMessageAsync($"I'm waiting for you {Trader.Mention}! {msg}. Your Dodo code is **{dodo}**");
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OnFinish?.Invoke(routine);
            Trader.SendMessageAsync($"Your order is complete, Thanks for your order! {msg}");
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            Trader.SendMessageAsync(msg);
        }
    }
}

using Discord;
using Discord.WebSocket;
using NHSE.Core;
using System;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class OrderRequest<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        public MultiItem ItemOrderData { get; }
        public ulong UserGuid { get; }
        public string VillagerName { get; }
        private SocketUser Trader { get; }
        private ISocketMessageChannel CommandSentChannel { get; }
        public Action<CrossBot>? OnFinish { private get; set; }
        public T[] Order { get; } // stupid but I cba to work on this part anymore
        public bool SkipRequested { get; set; } = false;

        public OrderRequest(MultiItem data, T[] order, ulong user, SocketUser trader, ISocketMessageChannel commandSentChannel)
        {
            ItemOrderData = data;
            UserGuid = user;
            Trader = trader;
            CommandSentChannel = commandSentChannel;
            Order = order;
            VillagerName = trader.Username;
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

        public void OrderReady(CrossBot routine, string msg)
        {
            Trader.SendMessageAsync($"I'm waiting for you {Trader.Username}! {msg}");
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

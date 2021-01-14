using Discord;
using Discord.WebSocket;
using NHSE.Core;
using System;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class OrderRequest<T> : IACNHOrderNotifier<T> where T : MultiItem, new()
    {
        public MultiItem ItemOrderData { get; }
        public ulong UserGuid { get; }
        private SocketUser Trader { get; }
        private ISocketMessageChannel CommandSentChannel { get; }
        public Action<CrossBot>? OnFinish { private get; set; }
        public Item[] Order { get => ItemOrderData.ItemArray.Items.ToArray(); } // stupid but I cba to work on this part anymore

        public OrderRequest(T data, ulong user, SocketUser trader, ISocketMessageChannel commandSentChannel)
        {
            ItemOrderData = data;
            UserGuid = user;
            Trader = trader;
            CommandSentChannel = commandSentChannel;
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
            Trader.SendMessageAsync($"Your order is starting, please go to your airport. I will send you the Dodo code shortly. {msg}");
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

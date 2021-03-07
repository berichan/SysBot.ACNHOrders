using SysBot.ACNHOrders;
using NHSE.Core;
using System;
using TwitchLib.Client;

namespace SysBot.ACNHOrders.Twitch
{
    public class TwitchOrderRequest<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        public T[] Order { get; }
        public VillagerRequest? VillagerOrder { get; }
        public ulong UserGuid { get; }
        public ulong OrderID { get; }
        public string VillagerName { get; }
        public bool SkipRequested { get; set; } = false;
        public Action<CrossBot>? OnFinish { private get; set; }
        public string Trader { get; }
        private TwitchClient Client { get; }
        private string Channel { get; }
        private TwitchConfig Settings { get; }

        public TwitchOrderRequest(T[] order, ulong user, ulong orderId, string trader, string villagerName, TwitchClient client, string channel, TwitchConfig settings, VillagerRequest? vil)
        {
            UserGuid = user;
            OrderID = orderId;
            Trader = trader;
            Order = order;
            VillagerName = villagerName;
            VillagerOrder = vil;
            Client = client;
            Channel = channel;
            Settings = settings;
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OnFinish?.Invoke(routine);
            SendMessage($"@{Trader} - Oops! Something has happened with your order: {msg}", Settings.OrderCanceledDestination);
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            SendMessage($"@{Trader} - Your order is starting, please **ensure your inventory is __empty__**, then go talk to Orville and stay on the Dodo code entry screen. I will send you the Dodo code shortly. {msg}", Settings.OrderStartDestination);
        }

        public void OrderReady(CrossBot routine, string msg)
        {
            if (Settings.OrderWaitDestination != TwitchMessageDestination.Disabled)
                SendMessage($"I'm waiting for you @{Trader}! {msg}", TwitchMessageDestination.Whisper);
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OnFinish?.Invoke(routine);
            SendMessage($"@{Trader} - Your order is complete, Thanks for your order! {msg}", Settings.OrderFinishDestination);
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            SendMessage($"@{Trader} - {msg}", Settings.NotifyDestination);
        }

        private void SendMessage(string message, TwitchMessageDestination dest)
        {
            switch (dest)
            {
                case TwitchMessageDestination.Channel:
                    Client.SendMessage(Channel, message);
                    break;
                case TwitchMessageDestination.Whisper:
                    Client.SendWhisper(Trader, message);
                    break;
            }
        }
    }
}

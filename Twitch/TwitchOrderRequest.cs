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
        private int Password { get; }

        public TwitchOrderRequest(T[] order, ulong user, ulong orderId, string trader, string villagerName, TwitchClient client, string channel, TwitchConfig settings, int pass, VillagerRequest? vil)
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
            Password = pass;
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OnFinish?.Invoke(routine);
            SendMessage($"@{Trader} - {msg}", Settings.OrderCanceledDestination);
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            msg = SanitizeForTwitch(msg);
            SendMessage($"@{Trader} - Your order is starting, please ensure your inventory is empty, then go talk to Orville and stay on the Dodo code entry screen. I will send you the Dodo link shortly. {msg}", Settings.OrderStartDestination);
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            msg = SanitizeForTwitch(msg);
            if (Settings.OrderWaitDestination == TwitchMessageDestination.Channel)
                SendMessage($"I'm waiting for you @{Trader}! {msg}. Enter the number you whispered to me on https://berichan.github.io/GetDodoCode/?hash={SimpleEncrypt.SimpleEncryptToBase64(dodo, Password).MakeWebSafe()} to get your dodo code. Click this link, not an old one or someone else's.", Settings.OrderWaitDestination);
            else if (Settings.OrderWaitDestination == TwitchMessageDestination.Whisper)
                SendMessage($"I'm waiting for you @{Trader}! {msg}. Your Dodo code is {dodo}", Settings.OrderWaitDestination);
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OnFinish?.Invoke(routine);
            SendMessage($"@{Trader} - Your order is complete, Thanks for your order! {msg}", Settings.OrderFinishDestination);
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            if (msg.StartsWith("Visitor arriving"))
                return;
            msg = SanitizeForTwitch(msg);
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

        public static string SanitizeForTwitch(string msg)
        {
            return msg.Replace("**", string.Empty);
        }
    }
}

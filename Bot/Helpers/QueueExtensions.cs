using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using NHSE.Core;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Concurrent;

namespace SysBot.ACNHOrders
{
    public static class QueueExtensions
    {
        const int ArriveTime = 90;
        const int SetupTime = 95;

        public static async Task AddToQueueAsync(this SocketCommandContext Context, OrderRequest<Item> itemReq, string player, SocketUser trader)
        {
            IUserMessage test;
            try
            {
                const string helper = "I've added you to the queue! I'll message you here when your order is ready";
                test = await trader.SendMessageAsync(helper).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                await Context.Channel.SendMessageAsync($"{ex.HttpCode}: {ex.Reason}!").ConfigureAwait(false);
                var noAccessMsg = Context.User == trader ? "You must enable private messages in order to be queued!" : $"{player} must enable private messages in order for them to be queued!";
                await Context.Channel.SendMessageAsync(noAccessMsg).ConfigureAwait(false);
                return;
            }

            // Try adding
            var result = AttemptAddToQueue(itemReq, trader.Mention, trader.Username, out var msg);

            // Notify in channel
            await Context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            // Notify in PM to mirror what is said in the channel.
            await trader.SendMessageAsync(msg).ConfigureAwait(false);

            // Clean Up
            if (result)
            {
                // Delete the user's join message for privacy
                if (!Context.IsPrivate)
                    await Context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
            else
            {
                // Delete our "I'm adding you!", and send the same message that we sent to the general channel.
                await test.DeleteAsync().ConfigureAwait(false);
            }
        }

        public static bool AddToQueueSync(IACNHOrderNotifier<Item> itemReq, string playerMention, string playerNameId, out string msg)
        {
            var result = AttemptAddToQueue(itemReq, playerMention, playerNameId, out var msge);
            msg = msge;

            return result;
        }

        // this sucks
        private static bool AttemptAddToQueue(IACNHOrderNotifier<Item> itemReq, string traderMention, string traderDispName, out string msg)
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray();
            var order = Array.Find(orderArray, x => x.UserGuid == itemReq.UserGuid);
            if (order != null)
            {
                if (!order.SkipRequested)
                    msg = $"{traderMention} - Sorry, you are already in the queue.";
                else
                    msg = $"{traderMention} - You have been recently removed from the queue. Please wait a while before attempting to enter the queue again.";
                return false;
            }

            if(Globals.Bot.CurrentUserName == traderDispName)
            {
                msg = $"{traderMention} - Failed to queue your order as it is the current processing order. Please wait a few seconds for the queue to clear if you've already completed it.";
                return false;
            }

            var position = orderArray.Length + 1;
            var idToken = Globals.Bot.Config.OrderConfig.ShowIDs ? $" (ID {itemReq.OrderID})" : string.Empty;
            msg = $"{traderMention} - Added you to the order queue{idToken}. Your position is: **{position}**";

            if (position > 1)
                msg += $". Your predicted ETA is {GetETA(position)}";
            else
                msg += ". Your order will start after the current order is complete!";

            if (itemReq.VillagerOrder != null)
                msg += $". {GameInfo.Strings.GetVillager(itemReq.VillagerOrder.GameName)} will be waiting for you on the island. Ensure you can collect them within the order timeframe.";

            Globals.Hub.Orders.Enqueue(itemReq);

            return true;
        }

        public static int GetPosition(ulong id, out OrderRequest<Item>? order)
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray().Where(x => !x.SkipRequested).ToArray();
            var orderFound = Array.Find(orderArray, x => x.UserGuid == id);
            if (orderFound != null && !orderFound.SkipRequested)
            {
                if (orderFound is OrderRequest<Item> oreq)
                {
                    order = oreq;
                    return Array.IndexOf(orderArray, orderFound) + 1;
                }
            }

            order = null;
            return -1;
        }

        public static string GetETA(int pos)
        {
            int minSeconds = ArriveTime + SetupTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            int addSeconds = ArriveTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            var timeSpan = TimeSpan.FromSeconds(minSeconds + (addSeconds * (pos-1)));
            if (timeSpan.Hours > 0)
                return string.Format("{0:D2}h:{1:D2}m:{2:D2}s", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
            else
                return string.Format("{0:D2}m:{1:D2}s", timeSpan.Minutes, timeSpan.Seconds);
        }

        private static ulong ID = 0;
        private static object IDAccessor = new();
        public static ulong GetNextID()
        {
            lock(IDAccessor)
            {
                return ID++;
            }
        }

        public static void ClearQueue<T>(this ConcurrentQueue<T> queue)
        {
            T item; // weird runtime error
#pragma warning disable CS8600
            while (queue.TryDequeue(out item)) { } // do nothing
#pragma warning restore CS8600
        }

        public static string GetQueueString()
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray().Where(x => !x.SkipRequested).ToArray();
            string orderString = string.Empty;
            foreach (var ord in orderArray)
                orderString += $"{ord.VillagerName} \r\n";

            return orderString;
        }
    }
}

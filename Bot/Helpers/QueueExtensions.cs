using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using NHSE.Core;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Concurrent;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public static class QueueExtensions
    {
        const int ArriveTime = 90;
        const int SetupTime = 95;

        public static async Task<bool> AddToQueueAsync(this SocketCommandContext Context, OrderRequest<Item> itemReq, string player, SocketUser trader)
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
                return false;
            }

            var result = AddToQueueSync(itemReq, trader.Mention, trader.Username, out var msg,
                Globals.Bot.Config.OrderConfig.MaxQueueCount);

            try
            {
                await Context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Could not send queue response in channel {Context.Channel.Id}: {ex.Message}", nameof(QueueExtensions));
            }
            try
            {
                await trader.SendMessageAsync(msg).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Could not send queue notification to {trader.Id}: {ex.Message}", nameof(QueueExtensions));
            }

            if (result)
            {
                if (!Context.IsPrivate)
                {
                    try
                    {
                        await Context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"Could not delete the accepted order message: {ex.Message}", nameof(QueueExtensions));
                    }
                }
            }
            else
            {
                try
                {
                    await test.DeleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Could not clean up the queue test DM for {trader.Id}: {ex.Message}", nameof(QueueExtensions));
                }
            }

            return result;
        }

        private static readonly object QueueSync = new();

        public static bool AddToQueueSync(IACNHOrderNotifier<Item> itemReq, string playerMention, string playerNameId, out string msg, int? maxQueueCount = null)
        {
            lock (QueueSync)
            {
                if (!Globals.Bot.Config.AcceptingCommands || Globals.ConsoleControl?.CanAcceptOrders == false)
                { msg = "The host has paused orders. Try again when orders reopen."; return false; }
                if (maxQueueCount.HasValue && Globals.Hub.Orders.Count >= maxQueueCount.Value)
                {
                    msg = $"The queue is full, with {Globals.Hub.Orders.Count} players waiting. Please try again later.";
                    return false;
                }

                var result = AttemptAddToQueueCore(itemReq, playerMention, playerNameId, out var msge);
                msg = msge;
                return result;
            }
        }

        private static bool AttemptAddToQueueCore(IACNHOrderNotifier<Item> itemReq, string traderMention, string traderDispName, out string msg)
        {
            var orders = Globals.Hub.Orders;

            var existingOrder = orders.GetByUserId(itemReq.UserGuid);
            if (existingOrder != null)
            {
                msg = $"{traderMention}: you already have a waiting order. Use My order to check it.";
                return false;
            }

            if (OrderStatusStore.Shared.IsActive(itemReq.UserGuid))
            {
                msg = $"{traderMention}: your order is still in progress. Use My order to check it. If you have finished, wait for the island to finish cleanup.";
                return false;
            }

            var position = orders.Count + 1;
            var idToken = Globals.Bot.Config.OrderConfig.ShowIDs ? $" (ID {itemReq.OrderID})" : string.Empty;
            msg = $"{traderMention}: your order was accepted{idToken}. Queue position: **{position}**.";

            if (position > 1)
                msg += $" Approximate wait: {GetETA(position)}.";
            else
                msg += " Your order starts when the island is ready.";

            if (itemReq.VillagerOrder != null)
                msg += $" {GameInfo.Strings.GetVillager(itemReq.VillagerOrder.GameName)} will be waiting on the island. Have an empty housing plot ready and adopt them before your visit ends.";

            OrderStatusStore.Shared.Set(itemReq.UserGuid, itemReq.OrderID, OrderStage.Queued);
            Globals.Hub.Orders.Enqueue(itemReq);

            return true;
        }

        public static int GetPosition(ulong id, out OrderRequest<Item>? order)
        {
            var orders = Globals.Hub.Orders;
            var position = orders.GetPosition(id);
            
            if (position > 0)
            {
                var found = orders.GetByUserId(id);
                if (found is OrderRequest<Item> oreq)
                {
                    order = oreq;
                    return position;
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
            T item;
#pragma warning disable CS8600
            while (queue.TryDequeue(out item)) { }
#pragma warning restore CS8600
        }

        public static string GetQueueString()
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray();
            string orderString = string.Empty;
            foreach (var ord in orderArray)
                orderString += $"{ord.VillagerName} \r\n";

            return orderString;
        }
    }
}

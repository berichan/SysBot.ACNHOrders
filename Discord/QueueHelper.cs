using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using NHSE.Core;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    internal sealed record QueueAttemptResult(string Message, bool Accepted);

    internal static class QueueHelper
    {
        internal static async Task<QueueAttemptResult> AttemptToQueueRequestDetailedAsync(
            IReadOnlyCollection<Item> items,
            SocketUser orderer,
            ISocketMessageChannel msgChannel,
            VillagerRequest? vr,
            bool catalogue,
            int maxOrderCount,
            MultiItem? preparedDelivery = null)
        {
            if (!Globals.Bot.Config.AllowKnownAbusers && LegacyAntiAbuse.CurrentInstance.IsGlobalBanned(orderer.Id))
                return new("You are not permitted to use this bot.", false);

            if (!Globals.Bot.Config.AcceptingCommands || Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode || Globals.Bot.Config.SkipConsoleBotCreation || Globals.ConsoleControl?.CanAcceptOrders == false)
                return new("Orders are not currently accepted.", false);

            if (GlobalBan.IsBanned(orderer.Id.ToString()))
                return new("You are banned from using this bot. Your order was not accepted.", false);

            if (Globals.Hub.Orders.GetByUserId(orderer.Id) != null || OrderStatusStore.Shared.IsActive(orderer.Id))
                return new("You already have an order. Choose My order to check its progress.", false);

            if (Globals.Hub.Orders.Count >= maxOrderCount)
                return new($"The queue is full, with {Globals.Hub.Orders.Count} players waiting. Please try again later.", false);

            if (!InternalItemTool.CurrentInstance.IsSaneAfterCorrection(items, Globals.Bot.Config.DropConfig))
            {
                var unsafeItems = InternalItemTool.CurrentInstance.GetUnsafeItemNames(items);
                var unsafeList = string.Join(", ", unsafeItems);
                return new($"These items can damage your save and cannot be ordered: {unsafeList}", false);
            }

            string? notice = null;
            if (items.Count > MultiItem.MaxOrder)
            {
                notice = $"Users are limited to {MultiItem.MaxOrder} items per command. You requested {items.Count}; the excess items were removed.";
                items = items.Take(MultiItem.MaxOrder).ToArray();
            }

            var multiOrder = preparedDelivery ?? new MultiItem(items.ToArray(), catalogue, true, true);
            if (!InternalItemTool.CurrentInstance.IsSaneAfterCorrection(multiOrder.ItemArray.Items.ToArray(), Globals.Bot.Config.DropConfig))
                return new("This order contains unsafe items. Edit your item list before confirming.", false);
            var requestInfo = new OrderRequest<Item>(multiOrder, multiOrder.ItemArray.Items.ToArray(), orderer.Id, QueueExtensions.GetNextID(), orderer, msgChannel, vr);

            IUserMessage test;
            try
            {
                const string helper = "Checking that I can send your private order instructions. Your order has not been accepted yet.";
                test = await orderer.SendMessageAsync(helper).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                return new(JoinMessages(notice, $"I could not send you a DM. Allow direct messages from members of this server, then try again. Discord error: {ex.HttpCode}: {ex.Reason}."), false);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Could not validate direct messages for {orderer.Id}: {ex.Message}", nameof(QueueHelper));
                return new(JoinMessages(notice, "I could not send you a private message, so the order was not accepted."), false);
            }

            var accepted = QueueExtensions.AddToQueueSync(requestInfo, orderer.Mention, orderer.Username, out var msg, maxOrderCount);
            var notificationFailed = false;
            try
            {
                await orderer.SendMessageAsync(msg).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                notificationFailed = true;
                LogUtil.LogError($"Could not send queue notification to {orderer.Id}: {ex.Message}", nameof(QueueHelper));
            }

            // Remove the delivery test on success as well; the final message tells the member whether their order was accepted.
            {
                try
                {
                    await test.DeleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Could not clean up the queue test DM for {orderer.Id}: {ex.Message}", nameof(QueueHelper));
                }
            }

            var response = JoinMessages(notice, msg);
            if (accepted && notificationFailed)
                response += "\nYour order was accepted, but I could not send the queue confirmation DM.";
            return new(response, accepted);
        }

        internal static async Task<string> AttemptToQueueRequestAsync(
            IReadOnlyCollection<Item> items,
            SocketUser orderer,
            ISocketMessageChannel msgChannel,
            VillagerRequest? vr,
            bool catalogue,
            int maxOrderCount)
            => (await AttemptToQueueRequestDetailedAsync(items, orderer, msgChannel, vr, catalogue, maxOrderCount).ConfigureAwait(false)).Message;

        private static string JoinMessages(string? notice, string message) =>
            string.IsNullOrWhiteSpace(notice) ? message : $"{notice}\n{message}";
    }
}

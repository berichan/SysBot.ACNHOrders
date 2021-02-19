using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    // ReSharper disable once UnusedType.Global
    public class OrderModule : ModuleBase<SocketCommandContext>
    {
        private static int MaxOrderCount => Globals.Bot.Config.OrderConfig.MaxQueueCount;

        private const string OrderItemSummary =
            "Requests the bot add the item order to the queue with the user's provided input. " +
            "Hex Mode: Item IDs (in hex); request multiple by putting spaces between items. " +
            "Text Mode: Item names; request multiple by putting commas between items. To parse for another language, include the language code first and a comma, followed by the items.";

        [Command("order")]
        [Summary(OrderItemSummary)]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            var cfg = Globals.Bot.Config;
            var items = DropUtil.GetItemsFromUserInput(request, cfg.DropConfig, true);
            await AttemptToQueueRequest(items, Context.User, Context.Channel).ConfigureAwait(false);
        }

        [Command("ordercat")]
        [Summary("Orders a catalogue of items created by an order tool such as ACNHMobileSpawner, does not duplicate any items.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestCatalogueOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            var cfg = Globals.Bot.Config;
            var items = DropUtil.GetItemsFromUserInput(request, cfg.DropConfig, true);
            await AttemptToQueueRequest(items, Context.User, Context.Channel, true).ConfigureAwait(false);
        }

        [Command("order")]
        [Summary("Requests the bot an order of items in the NHI format.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestNHIOrderAsync()
        {
            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment == default)
            {
                await ReplyAsync("No attachment provided!").ConfigureAwait(false);
                return;
            }

            var att = await NetUtil.DownloadNHIAsync(attachment).ConfigureAwait(false);
            if (!att.Success || !(att.Data is Item[] items))
            {
                await ReplyAsync("No NHI attachment provided!").ConfigureAwait(false);
                return;
            }

            await AttemptToQueueRequest(items, Context.User, Context.Channel, true).ConfigureAwait(false);
        }

        [Command("queue")]
        [Alias("qs", "qp", "position")]
        [Summary("View your position in the queue.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task ViewQueuePositionAsync()
        {
            var position = QueueExtensions.GetPosition(Context.User.Id, out _);
            if (position < 0)
            {
                await ReplyAsync("Sorry, you are not in the queue, or your order is happening now.").ConfigureAwait(false);
                return;
            }

            var message = $"{Context.User.Mention} - You are in the order queue. Position: {position}.";
            if (position > 1)
                message += $" Your predicted ETA is {QueueExtensions.GetETA(position)}.";

            await ReplyAsync(message).ConfigureAwait(false);
        }

        [Command("remove")]
        [Alias("qc", "delete", "removeMe")]
        [Summary("Remove yourself from the queue.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RemoveFromQueueAsync()
        {
            QueueExtensions.GetPosition(Context.User.Id, out var order);
            if (order == null)
            {
                await ReplyAsync("Sorry, you are not in the queue, or your order is happening now.").ConfigureAwait(false);
                return;
            }

            order.SkipRequested = true;
            await ReplyAsync($"{Context.User.Mention} - Your order has been removed. Please note that you will not be able to rejoin the queue again for a while.").ConfigureAwait(false);
        }

        [Command("visitorList")]
        [Alias("visitors")]
        [Summary("Print the list of visitors on the island (dodo restore mode only).")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task ShowVisitorList()
        {
            await ReplyAsync(Globals.Bot.VisitorList.VisitorFormattedString);
        }

        [Command("checkState")]
        [Alias("checkDirtyState")]
        [Summary("Prints whether or not the bot will restart the game for the next order.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task ShowDirtyState()
        {
            await ReplyAsync($"State: {(Globals.Bot.GameIsDirty? "Bad" : "Good")}");
        }

        private async Task AttemptToQueueRequest(IReadOnlyCollection<Item> items, SocketUser orderer, ISocketMessageChannel msgChannel, bool catalogue = false)
        {
            var currentOrderCount = Globals.Bot.Orders.Count;
            if (currentOrderCount >= Globals.Bot.Config.OrderConfig.MaxQueueCount)
            {
                var requestLimit = $"The queue limit has been reached, there are currently {currentOrderCount} players in the queue. Please try again later.";
                await ReplyAsync(requestLimit).ConfigureAwait(false);
                return;
            }

            if (items.Count > MultiItem.MaxOrder)
            {
                var clamped = $"Users are limited to {MultiItem.MaxOrder} items per command, You've asked for {items.Count}. All items above the limit have been removed.";
                await ReplyAsync(clamped).ConfigureAwait(false);
                items = items.Take(40).ToArray();
            }

            var multiOrder = new MultiItem(items.ToArray(), catalogue, true, true);
            var requestInfo = new OrderRequest<Item>(multiOrder, multiOrder.ItemArray.Items.ToArray(), orderer.Id, orderer, msgChannel);
            await Context.AddToQueueAsync(requestInfo, orderer.Username, orderer);
        }
    }
}

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
        [Summary("Order an inventory of items")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestDropAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            var cfg = Globals.Bot.Config;
            var items = DropUtil.GetItemsFromUserInput(request, cfg.DropConfig, true);
            await AttemptToQueueRequest(items, Context.User, Context.Channel).ConfigureAwait(false);
        }

        private async Task AttemptToQueueRequest(IReadOnlyCollection<Item> items, SocketUser orderer, ISocketMessageChannel msgChannel)
        {
            if (items.Count > MultiItem.MaxOrder)
            {
                var clamped = $"Users are limited to {MultiItem.MaxOrder} items per command, You've asked for {items.Count}. All items above the limit have been removed.";
                await ReplyAsync(clamped).ConfigureAwait(false);
                items = items.Take(40).ToArray();
            }

            var requestInfo = new OrderRequest<MultiItem>(new MultiItem(items.ToArray(), true), orderer.Id, orderer, msgChannel);
            await Context.AddToQueueAsync(requestInfo, orderer.Username, orderer);
        }
    }
}

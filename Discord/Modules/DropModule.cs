using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    // ReSharper disable once UnusedType.Global
    public class DropModule : ModuleBase<SocketCommandContext>
    {
        private static int MaxRequestCount => Globals.Bot.Config.DropConfig.MaxDropCount;

        [Command("clean")]
        [Summary("Picks up items around the bot.")]
        public async Task RequestCleanAsync()
        {
            if (Globals.Bot.CurrentUserId != Context.User.Id)
                return;
            if (!Globals.Bot.Config.AllowClean)
            {
                await ReplyAsync("Clean functionality is currently disabled.").ConfigureAwait(false);
                return;
            }
            Globals.Bot.CleanRequested = true;
            await ReplyAsync("A clean request will be executed momentarily.").ConfigureAwait(false);
        }

        [Command("code")]
        [Alias("dodo")]
        [Summary("Prints the Dodo Code for the island.")]
        [RequireSudo]
        public async Task RequestDodoCodeAsync() => await ReplyAsync($"Dodo Code for {Globals.Bot.TownName}: {Globals.Bot.DodoCode}.").ConfigureAwait(false);

        [Command("sendDodo")]
        [Summary("Prints the Dodo Code for the island. Only works in dodo restore mode.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestRestoreLoopDodoAsync()
        {
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
                return;
            await RequestDodoCodeAsync().ConfigureAwait(false);
        }

        private const string DropItemSummary =
            "Requests the bot drop an item with the user's provided input. " +
            "Hex Mode: Item IDs (in hex); request multiple by putting spaces between items. " +
            "Text Mode: Item names; request multiple by putting commas between items. To parse for another language, include the language code first and a comma, followed by the items.";

        [Command("drop")]
        [Alias("dropItem")]
        [Summary("Drops a custom item (or items).")]
        public async Task RequestDropAsync([Summary(DropItemSummary)][Remainder]string request)
        {
            if (Globals.Bot.CurrentUserId != Context.User.Id)
                return;
            var cfg = Globals.Bot.Config;
            var items = ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.PlayerDropped);
            MultiItem.StackToMax(items);
            await DropItems(items).ConfigureAwait(false);
        }

        private const string DropDIYSummary =
            "Requests the bot drop a DIY recipe with the user's provided input. " +
            "Hex Mode: DIY Recipe IDs (in hex); request multiple by putting spaces between items. " +
            "Text Mode: DIY Recipe Item names; request multiple by putting commas between items. To parse for another language, include the language code first and a comma, followed by the items.";

        [Command("dropDIY")]
        [Alias("diy")]
        [Summary("Drops a DIY recipe with the requested recipe ID(s).")]
        public async Task RequestDropDIYAsync([Summary(DropDIYSummary)][Remainder]string recipeIDs)
        {
            if (Globals.Bot.CurrentUserId != Context.User.Id)
                return;
            var items = ItemParser.GetDIYsFromUserInput(recipeIDs);
            await DropItems(items).ConfigureAwait(false);
        }

        private async Task DropItems(IReadOnlyCollection<Item> items)
        {
            if (items.Count > MaxRequestCount)
            {
                var clamped = $"Users are limited to {MaxRequestCount} items per command. Please use this bot responsibly.";
                await ReplyAsync(clamped).ConfigureAwait(false);
                items = items.Take(MaxRequestCount).ToArray();
            }

            var requestInfo = new ItemRequest(Context.User.Username, items);
            Globals.Bot.Injections.Enqueue(requestInfo);

            var msg = $"Item drop request{(requestInfo.Items.Count > 1 ? "s" : string.Empty)} will be executed momentarily.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }
    }
}

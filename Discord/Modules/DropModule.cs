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
            if (!await GetDropAvailability().ConfigureAwait(false))
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
            try
            {
                await Context.User.SendMessageAsync($"Dodo Code: {Globals.Bot.DodoCode}.").ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                await ReplyAsync($"{ex.Message}: Private messages must be open to use this command. I won't leak the Dodo code in this channel!");
            }
        }

        private const string DropItemSummary =
            "Requests the bot drop an item with the user's provided input. " +
            "Hex Mode: Item IDs (in hex); request multiple by putting spaces between items. " +
            "Text Mode: Item names; request multiple by putting commas between items. To parse for another language, include the language code first and a comma, followed by the items.";

        [Command("drop")]
        [Alias("dropItem")]
        [Summary("Drops a custom item (or items).")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestDropAsync([Summary(DropItemSummary)][Remainder]string request)
        {
            var cfg = Globals.Bot.Config;
            var items = ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, cfg.DropConfig.UseLegacyDrop ? ItemDestination.PlayerDropped : ItemDestination.HeldItem);

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
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestDropDIYAsync([Summary(DropDIYSummary)][Remainder]string recipeIDs)
        {
            var items = ItemParser.GetDIYsFromUserInput(recipeIDs);
            await DropItems(items).ConfigureAwait(false);
        }

        private async Task DropItems(IReadOnlyCollection<Item> items)
        {
            if (!await GetDropAvailability().ConfigureAwait(false))
                return;

            if (!InternalItemTool.CurrentInstance.IsSane(items))
            {
                await ReplyAsync($"{Context.User.Mention} - You are attempting to drop items that will damage your save. Drop request not accepted.");
                return;
            }

            if (items.Count > MaxRequestCount)
            {
                var clamped = $"Users are limited to {MaxRequestCount} items per command. Please use this bot responsibly.";
                await ReplyAsync(clamped).ConfigureAwait(false);
                items = items.Take(MaxRequestCount).ToArray();
            }

            var requestInfo = new ItemRequest(Context.User.Username, items);
            Globals.Bot.Injections.Enqueue(requestInfo);

            var msg = $"Item drop request{(requestInfo.Item.Count > 1 ? "s" : string.Empty)} will be executed momentarily.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        private async Task<bool> GetDropAvailability()
        {
            var cfg = Globals.Bot.Config;

            if (cfg.CanUseSudo(Context.User.Id) || Globals.Self.Owner == Context.User.Id)
                return true;

            if (Globals.Bot.CurrentUserId == Context.User.Id)
                return true;

            if (!cfg.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync($"{Context.User.Mention} - You are only permitted to use this command while on the island during your order, and only if you have forgotten something in your order.");
                return false;
            }
            else if (!cfg.DodoModeConfig.AllowDrop)
            {
                await ReplyAsync($"AllowDrop is currently set to false.");
                return false;
            }

            return true;
        }
    }
}

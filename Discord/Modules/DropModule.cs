using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using NHSE.Core;
using SysBot.Base;

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
        public async Task RequestDodoCodeAsync()
        {
            var draw = Globals.Bot.DodoImageDrawer;
            var txt = $"Dodo Code for {Globals.Bot.TownName}: {Globals.Bot.DodoCode}.";
            if (draw != null )
            {
                var path = draw.GetProcessedDodoImagePath();
                if (path != null)
                {
                    await Context.Channel.SendFileAsync(path, txt);
                    return;
                }
            }
            
            await ReplyAsync(txt).ConfigureAwait(false);
        }

        [Command("sendDodo")]
        [Alias("sd", "send")]
        [Summary("Prints the Dodo Code for the island. Only works in dodo restore mode.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestRestoreLoopDodoAsync()
        {
            var cfg = Globals.Bot.Config;
            var MesUser = Context.User.Username;
            Globals.Bot.DisUserID = ($"{Context.User.Id}");
            if (!Globals.Bot.Config.DodoModeConfig.AllowSendDodo && !Globals.Bot.Config.CanUseSudo(Context.User.Id) && Globals.Self.Owner != Context.User.Id)
                return;
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
                return;
            //Check ban list to send dodo code.
            string[] Checklist = File.ReadAllLines("banlist.txt", Encoding.UTF8);
            int indexS = Array.FindIndex(Checklist, row => row.Contains(Context.User.Id.ToString()));
            if (indexS != -1)
            {
                await ReplyAsync($"{Context.User.Mention} - You are currently not allowed to use the bot. Dodo code will not be sent.");
                return;
            }
            try
            {
                if (cfg.FieldLayerName != "name")
                {
                    var MapFile = ($"{Globals.Bot.Config.FieldLayerNHLDirectory}/{Globals.Bot.CLayer}.png");
                    await Context.User.SendMessageAsync($"Dodo Code for {Globals.Bot.TownName}: {Globals.Bot.DodoCode}.\n{Globals.Bot.TownName} is currently set to the following layer: {Globals.Bot.CLayer}.").ConfigureAwait(false);
                    if (File.Exists($"{MapFile}"))
                    {
                        await Context.User.SendFileAsync($"{MapFile}");
                    }
                    await ReplyAsync($"`{MesUser}`: Sent you the dodo code via DM");
                    await Globals.Self.TrySpeakMessage(Globals.Bot.Config.DodoModeConfig.SentDodoChannels, $"[{DateTime.Now:MM-dd hh:mm:ss tt}] The Dodo code was sent to <@{Context.User.Id}> - {Context.User.Id}  from `{Context.Guild.Name}` server.").ConfigureAwait(false);
                }
                else
                {
                    await Context.User.SendMessageAsync($"Dodo Code for {Globals.Bot.TownName}: {Globals.Bot.DodoCode}.").ConfigureAwait(false);
                }
            }
            catch (HttpException ex)
            {
                await ReplyAsync($"{ex.Message}: Private messages must be open to use this command. I won't leak the Dodo code in this channel!");
                return;
            }

            var reaction = Globals.Bot.Config.DodoModeConfig.SuccessfulDodoCodeSendReaction;
            if (!string.IsNullOrWhiteSpace(reaction))
            {
                try
                {
                    IEmote emote = reaction.StartsWith("<") ? Emote.Parse(reaction) : new Emoji(reaction);
                    await Context.Message.AddReactionAsync(emote);
                }
                catch 
                {
                    LogUtil.LogError($"Could not parse {reaction} as an emote, or the necessary permissions are not given to this bot.", "Config");
                }
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

        [Command("setTurnips")]
        [Alias("turnips")]
        [Summary("Sets all the week's turnips (minus Sunday) to a certain value.")]
        [RequireSudo]
        public async Task RequestTurnipSetAsync(int value)
        {
            var bot = Globals.Bot;
            bot.StonkRequests.Enqueue(new TurnipRequest(Context.User.Username, value)
            {
                OnFinish = success =>
                {
                    var reply = success
                        ? $"All turnip values successfully set to {value}!"
                        : "Catastrophic failure.";
                    Task.Run(async () => await ReplyAsync($"{Context.User.Mention}: {reply}").ConfigureAwait(false));
                }
            });
            await ReplyAsync($"Queued all turnip values to be set to {value}.");
        }

        [Command("setTurnipsMax")]
        [Alias("turnipsMax", "stonks")]
        [Summary("Sets all the week's turnips (minus Sunday) to 999,999,999")]
        [RequireSudo]
        public async Task RequestTurnipMaxSetAsync() => await RequestTurnipSetAsync(999999999);

        private async Task DropItems(IReadOnlyCollection<Item> items)
        {
            if (!await GetDropAvailability().ConfigureAwait(false))
                return;

            if (!InternalItemTool.CurrentInstance.IsSane(items, Globals.Bot.Config.DropConfig))
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

            if (!cfg.AllowDrop)
            {
                await ReplyAsync($"AllowDrop is currently set to false.");
                return false;
            }
            else if (!cfg.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync($"{Context.User.Mention} - You are only permitted to use this command while on the island during your order, and only if you have forgotten something in your order.");
                return false;
            }

            return true;
        }
    }
}

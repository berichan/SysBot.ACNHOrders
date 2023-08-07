using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    // ReSharper disable once UnusedType.Global
    public class OrderModule : ModuleBase<SocketCommandContext>
    {
        private static int MaxOrderCount => Globals.Bot.Config.OrderConfig.MaxQueueCount;
        private static Dictionary<ulong, DateTime> UserLastCommand = new();
        private static object commandSync = new();

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
            VillagerRequest? vr = null;

            LogUtil.LogInfo($"order received by {Context.User.Username} - {request}", nameof(OrderModule));

            // try get villager
            var result = VillagerOrderParser.ExtractVillagerName(request, out var res, out var san);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {res} Order has not been accepted.");
                return;
            }

            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - Villager injection is currently disabled.");
                    return;
                }

                request = san;
                var replace = VillagerResources.GetVillager(res);
                vr = new VillagerRequest(Context.User.Username, replace, 0, GameInfo.Strings.GetVillager(res));
            }

            Item[]? items = null;

            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment != default)
            {
                var att = await NetUtil.DownloadNHIAsync(attachment).ConfigureAwait(false);
                if (!att.Success || !(att.Data is Item[] itemData))
                {
                    await ReplyAsync("No NHI attachment provided!").ConfigureAwait(false);
                    return;
                }
                else
                {
                    items = itemData;
                }
            }

            if (items == null)
                items = string.IsNullOrWhiteSpace(request) ? new Item[1] { new Item(Item.NONE) } : ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped).ToArray();

            await AttemptToQueueRequest(items, Context.User, Context.Channel, vr).ConfigureAwait(false);
        }

        [Command("ordercat")]
        [Summary("Orders a catalogue of items created by an order tool such as ACNHMobileSpawner, does not duplicate any items.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestCatalogueOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            var cfg = Globals.Bot.Config;
            VillagerRequest? vr = null;

            LogUtil.LogInfo($"ordercat received by {Context.User.Username} - {request}", nameof(OrderModule));

            // try get villager
            var result = VillagerOrderParser.ExtractVillagerName(request, out var res, out var san);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {res} Order has not been accepted.");
                return;
            }

            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - Villager injection is currently disabled.");
                    return;
                }

                request = san;
                var replace = VillagerResources.GetVillager(res);
                vr = new VillagerRequest(Context.User.Username, replace, 0, GameInfo.Strings.GetVillager(res));
            }

            var items = string.IsNullOrWhiteSpace(request) ? new Item[1] { new Item(Item.NONE) } : ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped);
            await AttemptToQueueRequest(items, Context.User, Context.Channel, vr, true).ConfigureAwait(false);
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

            await AttemptToQueueRequest(items, Context.User, Context.Channel, null, true).ConfigureAwait(false);
        }


        [Command("lastorder")]
        [Alias("lo", "lasto", "lorder")]
        [Summary("LastOrderItemSummary")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestLastOrderAsync()
        {
            var cfg = Globals.Bot.Config;
            string path = ("UserOrder\\" + $"{Context.User.Id}.txt");
            if (File.Exists(path))
            {
                string request = File.ReadAllText(path);
                ;
                VillagerRequest? vr = null;

                // try get villager
                var result = VillagerOrderParser.ExtractVillagerName(request, out var res, out var san);
                if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
                {
                    await ReplyAsync($"{Context.User.Mention} - {res} Order has not been accepted.");
                    return;
                }

                if (result == VillagerOrderParser.VillagerRequestResult.Success)
                {
                    if (!cfg.AllowVillagerInjection)
                    {
                        await ReplyAsync($"{Context.User.Mention} - Villager injection is currently disabled.");
                        return;
                    }

                    request = san;
                    var replace = VillagerResources.GetVillager(res);
                    vr = new VillagerRequest(Context.User.Username, replace, 0, GameInfo.Strings.GetVillager(res));
                }

                Item[]? items = null;

                var attachment = Context.Message.Attachments.FirstOrDefault();
                if (attachment != default)
                {
                    var att = await NetUtil.DownloadNHIAsync(attachment).ConfigureAwait(false);
                    if (!att.Success || !(att.Data is Item[] itemData))
                    {
                        await ReplyAsync("No NHI attachment provided!").ConfigureAwait(false);
                        return;
                    }
                    else
                    {
                        items = itemData;
                    }
                }

                if (items == null)
                    items = string.IsNullOrWhiteSpace(request) ? new Item[1] { new Item(Item.NONE) } : ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped).ToArray();

                await AttemptToQueueRequest(items, Context.User, Context.Channel, vr).ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"<@{Context.User.Id}>, We do not have your last order logged, place an order and then you can use this command.").ConfigureAwait(false);
                return;
            }
        }

        [Command("checkitems")]
        [Alias("checkitem")]
        [Summary("Check the item ids to find item id's that will not let order happen.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task CheckItemAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            var cfg = Globals.Bot.Config;
            var BadItemsList = "";
            var CheckItemN = "";
            Item[]? items = null;
            items = string.IsNullOrWhiteSpace(request) ? new Item[1] { new Item(Item.NONE) } : ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped).ToArray();
            {
                var Bitems = FileUtil.GetEmbeddedResource("SysBot.ACNHOrders.Resources", "InternalHexList.txt");
                string[] CheckItems = request.Split(' ');

                foreach (var CheckItem in CheckItems)
                    if (Bitems.Contains(CheckItem))
                    {
                        ushort itemID = ItemParser.GetID(CheckItem);
                        if (itemID == Item.NONE)
                        {

                        }
                        else
                        {
                            var name = GameInfo.Strings.GetItemName(itemID);
                            CheckItemN = name + ": " + CheckItem;
                        }
                        BadItemsList = BadItemsList + CheckItemN + "\n";
                    }

                if (BadItemsList == "")
                {
                    await ReplyAsync($"All items are safe to order.");
                }
                else
                {
                    await ReplyAsync($"The following items are not safe to order:\n`{BadItemsList}`");
                }
            }
        }

        [Command("preset")]
        [Summary("Requests the bot an order of a preset created by the bot host.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestPresetOrderAsync([Remainder] string presetName)
        {
            var cfg = Globals.Bot.Config;
            VillagerRequest? vr = null;

            // try get villager
            var result = VillagerOrderParser.ExtractVillagerName(presetName, out var res, out var san);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {res} Order has not been accepted.");
                return;
            }

            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - Villager injection is currently disabled.");
                    return;
                }

                presetName = san;
                var replace = VillagerResources.GetVillager(res);
                vr = new VillagerRequest(Context.User.Username, replace, 0, GameInfo.Strings.GetVillager(res));
            }

            presetName = presetName.Trim();
            var preset = PresetLoader.GetPreset(cfg.OrderConfig, presetName);
            if (preset == null)
            {
                await ReplyAsync($"{Context.User.Mention} - {presetName} is not a valid preset.");
                return;
            }

            await AttemptToQueueRequest(preset, Context.User, Context.Channel, vr, true).ConfigureAwait(false);
        }

        [Command("ListPresets")]
        [Alias("LP")]
        [Summary("Lists all the presets.")]
        public async Task RequestListPresetsAsync()
        {
            var bot = Globals.Bot;

            DirectoryInfo dir = new DirectoryInfo(bot.Config.OrderConfig.NHIPresetsDirectory);
            FileInfo[] files = dir.GetFiles("*.nhi");
            string listnhi = "";
            foreach (FileInfo file in files)
            {
                listnhi = listnhi + "\n " + Path.GetFileNameWithoutExtension(file.Name);
            }
            await ReplyAsync($"**Presets available are the following:** {listnhi}.").ConfigureAwait(false);
        }

        [Command("uploadpreset")]
        [Alias("UpPre", "UP")]
        [Summary("Uploads file to add to preset folder.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        [RequireSudo]
        public async Task RequestUploadPresetAsync()
        {
            var cfg = Globals.Bot.Config;
            var attachments = Context.Message.Attachments;

            string file = attachments.ElementAt(0).Filename;
            string url = attachments.ElementAt(0).Url;

            var file1 = cfg.OrderConfig.NHIPresetsDirectory + "/" + file;
            await NetUtil.DownloadFileAsync(url, file1).ConfigureAwait(false);

            await ReplyAsync("Received attachment!\n\n" + "The following file has been added to presets folder: " + file);
        }

        [Command("queue")]
        [Alias("qs", "qp", "position")]
        [Summary("View your position in the queue.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task ViewQueuePositionAsync()
        {
            var cooldown = Globals.Bot.Config.OrderConfig.PositionCommandCooldown;
            if (!CanCommand(Context.User.Id, cooldown, true))
            {
                await ReplyAsync($"{Context.User.Mention} - This command has a {cooldown} second cooldown. Use this bot responsibly.").ConfigureAwait(false);
                return;
            }

            var position = QueueExtensions.GetPosition(Context.User.Id, out _);
            if (position < 0)
            {
                await ReplyAsync("Sorry, you are not in the queue, or your order is happening now.").ConfigureAwait(false);
                return;
            }

            var message = $"{Context.User.Mention} - You are in the order queue. Position: {position}.";
            if (position > 1)
                message += $" Your predicted ETA is {QueueExtensions.GetETA(position)}.";
            else
                message += " Your order will start after the current order is complete!";

            await ReplyAsync(message).ConfigureAwait(false);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
        }

        [Command("remove")]
        [Alias("qc", "delete", "removeMe", "cancel")]
        [Summary("Remove yourself from the queue.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RemoveFromQueueAsync()
        {
            QueueExtensions.GetPosition(Context.User.Id, out var order);
            if (order == null)
            {
                await ReplyAsync($"{Context.User.Mention} - Sorry, you are not in the queue, or your order is happening now.").ConfigureAwait(false);
                return;
            }

            order.SkipRequested = true;
            await ReplyAsync($"{Context.User.Mention} - Your order has been removed. Please note that you will not be able to rejoin the queue again for a while.").ConfigureAwait(false);
        }

        [Command("removeUser")]
        [Alias("rmu", "removeOther", "rmo")]
        [Summary("Remove someone from the queue.")]
        [RequireSudo]
        public async Task RemoveOtherFromQueueAsync(string identity)
        {
            if (ulong.TryParse(identity, out var res))
            {
                QueueExtensions.GetPosition(res, out var order);
                if (order == null)
                {
                    await ReplyAsync($"{identity} is not a valid ulong in the queue.").ConfigureAwait(false);
                    return;
                }

                order.SkipRequested = true;
                await ReplyAsync($"{identity} ({order.VillagerName}) has been removed from the queue.").ConfigureAwait(false);
            }
            else
                await ReplyAsync($"{identity} is not a valid u64.").ConfigureAwait(false);
        }

        [Command("removeAlt")]
        [Alias("removeLog", "rmAlt")]
        [Summary("Removes an identity (name-id) from the local user-to-villager AntiAbuse database")]
        [RequireSudo]
        public async Task RemoveAltAsync([Remainder]string identity)
        {
            if (NewAntiAbuse.Instance.Remove(identity))
                await ReplyAsync($"{identity} has been removed from the database.").ConfigureAwait(false);
            else
                await ReplyAsync($"{identity} is not a valid identity.").ConfigureAwait(false);
        }

        [Command("removeAltLegacy")]
        [Alias("removeLogLegacy", "rmAltLegacy")]
        [Summary("(Uses legacy database) Removes an identity (name-id) from the local user-to-villager AntiAbuse database")]
        [RequireSudo]
        public async Task RemoveLegacyAltAsync([Remainder] string identity)
        {
            if (LegacyAntiAbuse.CurrentInstance.Remove(identity))
                await ReplyAsync($"{identity} has been removed from the database.").ConfigureAwait(false);
            else
                await ReplyAsync($"{identity} is not a valid identity.").ConfigureAwait(false);
        }

        [Command("visitorList")]
        [Alias("visitors")]
        [Summary("Print the list of visitors on the island (dodo restore mode only).")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task ShowVisitorList()
        {
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Globals.Self.Owner != Context.User.Id)
            {
                await ReplyAsync($"{Context.User.Mention} - You may only view visitors in dodo restore mode. Please respect the privacy of other orderers.");
                return;
            }

            await ReplyAsync(Globals.Bot.VisitorList.VisitorFormattedString);
        }

        [Command("checkState")]
        [Alias("checkDirtyState")]
        [Summary("Prints whether or not the bot will restart the game for the next order.")]
        [RequireSudo]
        public async Task ShowDirtyStateAsync()
        {
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync("There is no order state in dodo restore mode.");
                return;
            }

            await ReplyAsync($"State: {(Globals.Bot.GameIsDirty? "Bad" : "Good")}").ConfigureAwait(false);
        }

        [Command("queueList")]
        [Alias("ql")]
        [Summary("DMs the user the current list of names in the queue.")]
        [RequireSudo]
        public async Task ShowQueueListAsync()
        {
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync("There is no queue in dodo restore mode.").ConfigureAwait(false);
                return;
            }

            try
            {
                await Context.User.SendMessageAsync($"The following users are in the queue for {Globals.Bot.TownName}: \r\n{QueueExtensions.GetQueueString()}").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await ReplyAsync($"{e.Message}: Are your DMs open?").ConfigureAwait(false);
            }
        }

        [Command("gameTime")]
        [Alias("gt")]
        [Summary("Prints the last checked (current) in-game time.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetGameTime()
        {
            var bot = Globals.Bot;
            var cooldown = bot.Config.OrderConfig.PositionCommandCooldown;
            if (!CanCommand(Context.User.Id, cooldown, true))
            {
                await ReplyAsync($"{Context.User.Mention} - This command has a {cooldown} second cooldown. Use this bot responsibly.").ConfigureAwait(false);
                return;
            }

            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                var nooksMessage = (bot.LastTimeState.Hour >= 22 || bot.LastTimeState.Hour < 8) ? "Nook's Cranny is closed" : "Nook's Cranny is expected to be open.";
                await ReplyAsync($"The current in-game time is: {bot.LastTimeState} \r\n{nooksMessage}").ConfigureAwait(false);
                return;
            }

            await ReplyAsync($"Last order started at: {bot.LastTimeState}").ConfigureAwait(false);
            return;
        }

        private async Task AttemptToQueueRequest(IReadOnlyCollection<Item> items, SocketUser orderer, ISocketMessageChannel msgChannel, VillagerRequest? vr, bool catalogue = false)
        {
            if (!Globals.Bot.Config.AllowKnownAbusers && LegacyAntiAbuse.CurrentInstance.IsGlobalBanned(orderer.Id))
            {
                await ReplyAsync($"{Context.User.Mention} - You are not permitted to use this bot.");
                return;
            }

            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode || Globals.Bot.Config.SkipConsoleBotCreation)
            {
                await ReplyAsync($"{Context.User.Mention} - Orders are not currently accepted.");
                return;
            }

            if (GlobalBan.IsBanned(orderer.Id.ToString()))
            {
                await ReplyAsync($"{Context.User.Mention} - You have been banned for abuse. Order has not been accepted.");
                return;
            }

            var currentOrderCount = Globals.Hub.Orders.Count;
            if (currentOrderCount >= MaxOrderCount)
            {
                var requestLimit = $"The queue limit has been reached, there are currently {currentOrderCount} players in the queue. Please try again later.";
                await ReplyAsync(requestLimit).ConfigureAwait(false);
                return;
            }

            if (!InternalItemTool.CurrentInstance.IsSane(items, Globals.Bot.Config.DropConfig))
            {
                await ReplyAsync($"{Context.User.Mention} - You are attempting to order items that will damage your save. Order not accepted.");
                return;
            }

            if (items.Count > MultiItem.MaxOrder)
            {
                var clamped = $"Users are limited to {MultiItem.MaxOrder} items per command, You've asked for {items.Count}. All items above the limit have been removed.";
                await ReplyAsync(clamped).ConfigureAwait(false);
                items = items.Take(40).ToArray();
            }

            var multiOrder = new MultiItem(items.ToArray(), catalogue, true, true);
            var requestInfo = new OrderRequest<Item>(multiOrder, multiOrder.ItemArray.Items.ToArray(), orderer.Id, QueueExtensions.GetNextID(), orderer, msgChannel, vr);
            await Context.AddToQueueAsync(requestInfo, orderer.Username, orderer);
        }

        public static bool CanCommand(ulong id, int secondsCooldown, bool addIfNotAdded)
        {
            if (secondsCooldown < 0)
                return true;
            lock (commandSync)
            {
                if (UserLastCommand.ContainsKey(id))
                {
                    bool inCooldownPeriod = Math.Abs((DateTime.Now - UserLastCommand[id]).TotalSeconds) < secondsCooldown;
                    if (addIfNotAdded && !inCooldownPeriod)
                    {
                        UserLastCommand.Remove(id);
                        UserLastCommand.Add(id, DateTime.Now);
                    }
                    return !inCooldownPeriod;
                }
                else if (addIfNotAdded)
                {
                    UserLastCommand.Add(id, DateTime.Now);
                }
                return true;
            }
        }
    }

    public static class VillagerOrderParser
    {
        public enum VillagerRequestResult
        {
            NoVillagerRequested,
            InvalidVillagerRequested,
            Success
        }

        public static VillagerRequestResult ExtractVillagerName(string order, out string result, out string sanitizedOrder, string villagerFormat = "Villager:")
        {
            result = string.Empty;
            sanitizedOrder = string.Empty;
            var index = order.IndexOf(villagerFormat, StringComparison.InvariantCultureIgnoreCase);
            if (index < 0)
                return VillagerRequestResult.NoVillagerRequested;

            var internalName = order.Substring(index + villagerFormat.Length);
            var nameSearched = internalName;
            internalName = internalName.Trim();

            if (!VillagerResources.IsVillagerDataKnown(internalName))
                internalName = GameInfo.Strings.VillagerMap.FirstOrDefault(z => string.Equals(z.Value, internalName, StringComparison.InvariantCultureIgnoreCase)).Key;

            if (IsUnadoptable(nameSearched) || IsUnadoptable(internalName))
            {
                result = $"{nameSearched} is not adoptable. Order setup required for this villager is unnecessary.";
                return VillagerRequestResult.InvalidVillagerRequested;
            }

            if (internalName == default)
            {
                result = $"{nameSearched} is not a valid internal villager name.";
                return VillagerRequestResult.InvalidVillagerRequested;
            }

            sanitizedOrder = order.Substring(0, index);
            result = internalName;
            return VillagerRequestResult.Success;
        }

        private static readonly List<string> UnadoptableVillagers = new()
        {
            "cbr18",
            "der10",
            "elp11",
            "gor11",
            "rbt20",
            "shp14",
            "alp",
            "alw",
            "bev",
            "bey",
            "boa",
            "boc",
            "bpt",
            "chm",
            "chy",
            "cml",
            "cmlb",
            "dga",
            "dgb",
            "doc",
            "dod",
            "fox",
            "fsl",
            "grf",
            "gsta",
            "gstb",
            "gul",
            "gul",
            "hgc",
            "hgh",
            "hgs",
            "kpg",
            "kpm",
            "kpp",
            "kps",
            "lom",
            "man",
            "mka",
            "mnc",
            "mnk",
            "mob",
            "mol",
            "otg",
            "otgb",
            "ott",
            "owl",
            "ows",
            "pck",
            "pge",
            "pgeb",
            "pkn",
            "plk",
            "plm",
            "plo",
            "poo",
            "poob",
            "pyn",
            "rcm",
            "rco",
            "rct",
            "rei",
            "seo",
            "skk",
            "slo",
            "spn",
            "sza",
            "szo",
            "tap",
            "tkka",
            "tkkb",
            "ttla",
            "ttlb",
            "tuk",
            "upa",
            "wrl",
            "xct"
        };

        public static bool IsUnadoptable(string? internalName) => UnadoptableVillagers.Contains(internalName == null ? string.Empty : internalName.Trim().ToLower());
    }
}

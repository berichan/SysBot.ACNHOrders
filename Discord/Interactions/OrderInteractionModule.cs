using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public sealed class OrderModal : IModal
    {
        public string Title => "Place an Order";

        [ModalTextInput("modal-items", TextInputStyle.Paragraph)]
        public string Items { get; set; } = string.Empty;

        [ModalTextInput("modal-villager")]
        [RequiredInput(false)]
        public string? Villager { get; set; }

        [ModalTextInput("modal-language")]
        [RequiredInput(false)]
        public string? Language { get; set; }
    }

    public class OrderInteractionModule : InteractionModuleBase<SocketInteractionContext>
    {
        public const string LastOrderDirectory = "UserOrder";
        public const string OrderMarker = "ORDER";
        public const string OrderCatMarker = "ORDERCAT";

        private static Dictionary<ulong, DateTime> UserLastCommand = new();
        private static object commandSync = new();

        [SlashCommand("order", "Build and review an order before joining the queue.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task RequestOrderAsync(string? items = null, string? villager = null, string? language = null) =>
            new OrderExperience(Context).StartOrder(items, villager, language);

        [ComponentInteraction("open-order-modal")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task OpenOrderModal() => new OrderExperience(Context).StartOrder();
        [ComponentInteraction("open-order-modal:*")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task OpenSuffixedOrderModal(string _) => OpenOrderModal();
        [ModalInteraction("order-modal")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task HandleOrderModal(OrderModal modal) => new OrderExperience(Context).StartOrder(modal.Items, modal.Villager, modal.Language);
        [ModalInteraction("order-modal:*")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task HandleSuffixedOrderModal(string _, OrderModal modal) => HandleOrderModal(modal);

        [SlashCommand("ordercat", "Review a catalogue order before joining the queue.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task RequestCatalogueOrderAsync(string? items = null, string? villager = null) =>
            new OrderExperience(Context).StartOrder(items, villager, mode: OrderFillMode.Catalogue);

        [SlashCommand("order-nhi", "Import an .nhi file into an order preview.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task RequestNHIOrderAsync(IAttachment file) => new OrderExperience(Context).ImportFile(file);

        [SlashCommand("lastorder", "Review and optionally repeat your last accepted order.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task RequestLastOrderAsync() => new OrderExperience(Context).Handle("again", "home", "0");
        [SlashCommand("checkitems", "Check the item ids to find item id's that will not let order happen.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task CheckItemAsync(string items)
        {
            var BadItemsList = "";
            var CheckItemN = "";

            var Bitems = FileUtil.GetEmbeddedResource("SysBot.ACNHOrders.Resources", "InternalHexList.txt");
            string[] CheckItems = items.Split(' ');

            foreach (var CheckItem in CheckItems)
                if (Bitems.Contains(CheckItem))
                {
                    ushort itemID = ItemParser.GetID(CheckItem);
                    if (itemID != Item.NONE)
                    {
                        var name = GameInfo.Strings.GetItemName(itemID);
                        CheckItemN = name + ": " + CheckItem;
                    }
                    BadItemsList = BadItemsList + CheckItemN + "\n";
                }

            if (BadItemsList == "")
                await RespondAsync("All items are safe to order.", ephemeral: true);
            else
                await RespondAsync($"The following items are not safe to order:\n`{BadItemsList}`", ephemeral: true);
        }

        [SlashCommand("preset", "Import a host preset into an order preview.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task RequestPresetOrderAsync(string name, string? villager = null) =>
            new OrderExperience(Context).ImportPreset(name, villager);
        [SlashCommand("listpresets", "Lists all the presets.")]
        public async Task RequestListPresetsAsync()
        {
            var bot = Globals.Bot;
            Directory.CreateDirectory(bot.Config.OrderConfig.NHIPresetsDirectory);
            DirectoryInfo dir = new DirectoryInfo(bot.Config.OrderConfig.NHIPresetsDirectory);
            FileInfo[] files = dir.GetFiles("*.nhi");
            string listnhi = "";
            foreach (FileInfo file in files)
                listnhi = listnhi + "\n " + Path.GetFileNameWithoutExtension(file.Name);

            await RespondAsync($"**Presets available are the following:** {listnhi}.");
        }

        [SlashCommand("uploadpreset", "Uploads file to add to preset folder.")]
        [RequireSudoInteraction]
        public async Task RequestUploadPresetAsync(IAttachment file)
        {
            await DeferAsync(ephemeral: true).ConfigureAwait(false);
            var cfg = Globals.Bot.Config;
            var fileName = Path.GetFileName(file.Filename);
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(Path.GetExtension(fileName), ".nhi", StringComparison.OrdinalIgnoreCase))
            {
                await SetDeferredResponseAsync("Only .nhi preset files can be uploaded.").ConfigureAwait(false);
                return;
            }

            var url = file.Url;
            Directory.CreateDirectory(cfg.OrderConfig.NHIPresetsDirectory);
            var dest = Path.Combine(cfg.OrderConfig.NHIPresetsDirectory, fileName);
            await NetUtil.DownloadFileAsync(url, dest).ConfigureAwait(false);
            await SetDeferredResponseAsync("Received attachment!\n\nThe following file has been added to presets folder: " + fileName).ConfigureAwait(false);
        }

        [SlashCommand("queue", "View your order position and live progress.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task ViewQueuePositionAsync() => new OrderExperience(Context).MyOrder();

        [SlashCommand("remove", "Cancel your waiting order after confirmation.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task RemoveFromQueueAsync() => new OrderExperience(Context).RequestCancel();
        [SlashCommand("removeuser", "Remove someone from the queue.")]
        [RequireSudoInteraction]
        public async Task RemoveOtherFromQueueAsync(string id)
        {
            if (ulong.TryParse(id, out var res))
            {
                QueueExtensions.GetPosition(res, out var order);
                if (order == null)
                {
                    await RespondAsync($"{id} is not a valid ulong in the queue.", ephemeral: true);
                    return;
                }

                if (!Globals.Hub.Orders.RemoveByUserId(res, order.OrderID))
                { await RespondAsync("That order has started and is no longer in the waiting queue.", ephemeral: true); return; }
                OrderStatusStore.Shared.Set(res, order.OrderID, OrderStage.Cancelled, "The host removed your waiting order.");
                await RespondAsync($"{id} ({order.VillagerName}) has been removed from the queue.");
            }
            else
                await RespondAsync($"{id} is not a valid u64.", ephemeral: true);
        }

        [SlashCommand("removealt", "Removes an identity (name-id) from the local user-to-villager AntiAbuse database.")]
        [RequireSudoInteraction]
        public async Task RemoveAltAsync(string identity)
        {
            if (NewAntiAbuse.Instance.Remove(identity))
                await RespondAsync($"{identity} has been removed from the database.");
            else
                await RespondAsync($"{identity} is not a valid identity.", ephemeral: true);
        }

        [SlashCommand("removealt-legacy", "Uses legacy database to remove an identity (name-id).")]
        [RequireSudoInteraction]
        public async Task RemoveLegacyAltAsync(string identity)
        {
            if (LegacyAntiAbuse.CurrentInstance.Remove(identity))
                await RespondAsync($"{identity} has been removed from the database.");
            else
                await RespondAsync($"{identity} is not a valid identity.", ephemeral: true);
        }

        [SlashCommand("visitors", "Print the list of visitors on the island (dodo restore mode only).")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task ShowVisitorList()
        {
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Globals.Self.Owner != Context.User.Id)
            {
                await RespondAsync("You may only view visitors in dodo restore mode. Please respect the privacy of other orderers.", ephemeral: true);
                return;
            }

            await RespondAsync(Globals.Bot.VisitorList.VisitorFormattedString);
        }

        [SlashCommand("checkstate", "Prints whether or not the bot will restart the game for the next order.")]
        [RequireSudoInteraction]
        public async Task ShowDirtyStateAsync()
        {
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await RespondAsync("There is no order state in dodo restore mode.");
                return;
            }

            await RespondAsync($"State: {(Globals.Bot.GameIsDirty ? "Bad" : "Good")}");
        }

        [SlashCommand("queuelist", "DMs the user the current list of names in the queue.")]
        [RequireSudoInteraction]
        public async Task ShowQueueListAsync()
        {
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await RespondAsync("There is no queue in dodo restore mode.");
                return;
            }

            await DeferAsync(ephemeral: true).ConfigureAwait(false);
            try
            {
                await Context.User.SendMessageAsync($"The following users are in the queue for {Globals.Bot.TownName}: \r\n{QueueExtensions.GetQueueString()}").ConfigureAwait(false);
                await SetDeferredResponseAsync("Sent you the queue list via DM.").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await SetDeferredResponseAsync($"{e.Message}: Are your DMs open?").ConfigureAwait(false);
            }
        }

        [SlashCommand("gametime", "Prints the last checked (current) in-game time.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetGameTime()
        {
            var bot = Globals.Bot;
            var cooldown = bot.Config.OrderConfig.PositionCommandCooldown;
            if (!CanCommand(Context.User.Id, cooldown, true))
            {
                await RespondAsync($"This command has a {cooldown} second cooldown. Use this bot responsibly.", ephemeral: true);
                return;
            }

            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                var nooksMessage = (bot.LastTimeState.Hour >= 22 || bot.LastTimeState.Hour < 8) ? "Nook's Cranny is closed" : "Nook's Cranny is expected to be open.";
                await RespondAsync($"The current in-game time is: {bot.LastTimeState} \r\n{nooksMessage}");
                return;
            }

            await RespondAsync($"Last order started at: {bot.LastTimeState}");
        }

        private Task SetDeferredResponseAsync(string message) =>
            Context.Interaction.ModifyOriginalResponseAsync(properties => properties.Content = message);

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
}

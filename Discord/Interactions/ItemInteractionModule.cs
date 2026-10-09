using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public class ItemInteractionModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("lookup", "Find items and add them directly to your order.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task SearchItemsAsync([Autocomplete(typeof(ItemLookupAutocomplete))] string name) =>
            new OrderExperience(Context).Search(name);

        [SlashCommand("lookup-lang", "Find items in a language and add them to your order.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public Task SearchItemsLangAsync(string language, [Autocomplete(typeof(ItemLookupAutocomplete))] string name) =>
            new OrderExperience(Context).Search(name, language);
        [SlashCommand("item", "Gets the info for an item.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetItemInfoAsync(string hex)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await RespondAsync("Lookup commands are not accepted.", ephemeral: true);
                return;
            }

            ushort itemID = ItemParser.GetID(hex);
            if (itemID == Item.NONE)
            {
                await RespondAsync("Invalid item requested.", ephemeral: true);
                return;
            }

            var name = GameInfo.Strings.GetItemName(itemID);
            var result = ItemInfo.GetItemInfo(itemID);
            if (result.Length == 0)
                await RespondAsync($"No customization data available for the requested item ({name}).", ephemeral: true);
            else
                await RespondAsync($"{name}:\r\n{result}", ephemeral: true);
        }

        [SlashCommand("stack", "Stacks an item and prints the hex code.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task StackAsync(string hex, int count)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await RespondAsync("Lookup commands are not accepted.", ephemeral: true);
                return;
            }

            ushort itemID = ItemParser.GetID(hex);
            if (itemID == Item.NONE || count < 1 || count > 99)
            {
                await RespondAsync("Invalid item requested.", ephemeral: true);
                return;
            }

            var ct = count - 1;
            var item = new Item(itemID) { Count = (ushort)ct };
            var msg = ItemParser.GetItemText(item);
            await RespondAsync(msg, ephemeral: true);
        }

        [SlashCommand("customize", "Customizes an item and prints the hex code.")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task CustomizeAsync(string hex, int cust1, int cust2)
            => await CustomizeImpl(hex, cust1 + cust2);

        private async Task CustomizeImpl(string hex, int sum)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await RespondAsync("Lookup commands are not accepted.", ephemeral: true);
                return;
            }

            ushort itemID = ItemParser.GetID(hex);
            if (itemID == Item.NONE)
            {
                await RespondAsync("Invalid item requested.", ephemeral: true);
                return;
            }
            if (sum <= 0)
            {
                await RespondAsync("No customization data specified.", ephemeral: true);
                return;
            }

            var remake = ItemRemakeUtil.GetRemakeIndex(itemID);
            if (remake < 0)
            {
                await RespondAsync("No customization data available for the requested item.", ephemeral: true);
                return;
            }

            int body = sum & 7;
            int fabric = sum >> 5;
            if (fabric > 7 || ((fabric << 5) | body) != sum)
            {
                await RespondAsync("Invalid customization data specified.", ephemeral: true);
                return;
            }

            var info = ItemRemakeInfoData.List[remake];
            bool hasBody = body == 0 || body <= info.ReBodyPatternNum;
            bool hasFabric = fabric == 0 || info.GetFabricDescription(fabric) != "Invalid";

            if (!hasBody || !hasFabric)
            {
                await RespondAsync("Requested customization for item appears to be invalid.", ephemeral: true);
                return;
            }

            var item = new Item(itemID) { BodyType = body, PatternChoice = fabric };
            var msg = ItemParser.GetItemText(item);
            await RespondAsync(msg, ephemeral: true);
        }
    }
}

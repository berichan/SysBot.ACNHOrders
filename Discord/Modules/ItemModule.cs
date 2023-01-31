using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    // ReSharper disable once UnusedType.Global
    public class ItemModule : ModuleBase<SocketCommandContext>
    {
        [Command("lookupLang")]
        [Alias("ll")]
        [Summary("Gets a list of items that contain the request string.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task SearchItemsAsync([Summary("Language code to search with")] string language, [Summary("Item name / item substring")][Remainder] string itemName)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await ReplyAsync($"{Context.User.Mention} - Lookup commands are not accepted.");
                return;
            }
            var strings = GameInfo.GetStrings(language).ItemDataSource;
            await PrintItemsAsync(itemName, strings).ConfigureAwait(false);
        }

        [Command("lookup")]
        [Alias("li", "search")]
        [Summary("Gets a list of items that contain the request string.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task SearchItemsAsync([Summary("Item name / item substring")][Remainder] string itemName)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await ReplyAsync($"{Context.User.Mention} - Lookup commands are not accepted.");
                return;
            }
            var strings = GameInfo.Strings.ItemDataSource;
            await PrintItemsAsync(itemName, strings).ConfigureAwait(false);
        }

        private async Task PrintItemsAsync(string itemName, IReadOnlyList<ComboItem> strings)
        {
            const int minLength = 2;
            if (itemName.Length <= minLength)
            {
                await ReplyAsync($"Please enter a search term longer than {minLength} characters.").ConfigureAwait(false);
                return;
            }

            var exact = ItemParser.GetItem(itemName, strings);
            if (!exact.IsNone)
            {
                var msg = $"{exact.ItemId:X4} {itemName}";
                if (msg == "02F8 vine")
                {
                    msg = "3107 vine";
                }
                if (msg == "02F7 glowing moss")
                {
                    msg = "3106 glowing moss";
                }
                await ReplyAsync(Format.Code(msg)).ConfigureAwait(false);
                return;
            }

            var matches = ItemParser.GetItemsMatching(itemName, strings).ToArray();
            var result = string.Join(Environment.NewLine, matches.Select(z => $"{z.Value:X4} {z.Text}"));

            if (result.Length == 0)
            {
                await ReplyAsync("No matches found.").ConfigureAwait(false);
                return;
            }

            const int maxLength = 500;
            if (result.Length > maxLength)
            {
                var ordered = matches.OrderBy(z => LevenshteinDistance.Compute(z.Text, itemName));
                result = string.Join(Environment.NewLine, ordered.Select(z => $"{z.Value:X4} {z.Text}"));
                result = result.Substring(0, maxLength) + "...[truncated]";
            }

            await ReplyAsync(Format.Code(result)).ConfigureAwait(false);
        }

        [Command("item")]
        [Summary("Gets the info for an item.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetItemInfoAsync([Summary("Item ID (in hex)")] string itemHex)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await ReplyAsync($"{Context.User.Mention} - Lookup commands are not accepted.");
                return;
            }

            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == Item.NONE)
            {
                await ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }

            var name = GameInfo.Strings.GetItemName(itemID);
            var result = ItemInfo.GetItemInfo(itemID);
            if (result.Length == 0)
                await ReplyAsync($"No customization data available for the requested item ({name}).").ConfigureAwait(false);
            else
                await ReplyAsync($"{name}:\r\n{result}").ConfigureAwait(false);
        }

        [Command("stack")]
        [Summary("Stacks an item and prints the hex code.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task StackAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("Count of items in the stack")] int count)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await ReplyAsync($"{Context.User.Mention} - Lookup commands are not accepted.");
                return;
            }

            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == Item.NONE || count < 1 || count > 99)
            {
                await ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }

            var ct = count - 1; // value 0 => count of 1
            var item = new Item(itemID) { Count = (ushort)ct };
            var msg = ItemParser.GetItemText(item);
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("customize")]
        [Summary("Customizes an item and prints the hex code.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task CustomizeAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("First customization value")] int cust1, [Summary("Second customization value")] int cust2)
            => await CustomizeAsync(itemHex, cust1 + cust2).ConfigureAwait(false);

        [Command("customize")]
        [Summary("Customizes an item and prints the hex code.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task CustomizeAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("Customization value sum")] int sum)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await ReplyAsync($"{Context.User.Mention} - Lookup commands are not accepted.");
                return;
            }

            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == Item.NONE)
            {
                await ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }
            if (sum <= 0)
            {
                await ReplyAsync("No customization data specified.").ConfigureAwait(false);
                return;
            }

            var remake = ItemRemakeUtil.GetRemakeIndex(itemID);
            if (remake < 0)
            {
                await ReplyAsync("No customization data available for the requested item.").ConfigureAwait(false);
                return;
            }

            int body = sum & 7;
            int fabric = sum >> 5;
            if (fabric > 7 || ((fabric << 5) | body) != sum)
            {
                await ReplyAsync("Invalid customization data specified.").ConfigureAwait(false);
                return;
            }

            var info = ItemRemakeInfoData.List[remake];
            // already checked out-of-range body/fabric values above
            bool hasBody = body == 0 || body <= info.ReBodyPatternNum;
            bool hasFabric = fabric == 0 || info.GetFabricDescription(fabric) != "Invalid";

            if (!hasBody || !hasFabric)
                await ReplyAsync("Requested customization for item appears to be invalid.").ConfigureAwait(false);

            var item = new Item(itemID) { BodyType = body, PatternChoice = fabric };
            var msg = ItemParser.GetItemText(item);
            await ReplyAsync(msg).ConfigureAwait(false);
        }
    }
}

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
    public class RecipeModule : ModuleBase<SocketCommandContext>
    {
        [Command("recipeLang")]
        [Alias("rl")]
        [Summary("Gets a list of DIY recipe IDs that contain the requested Item Name string.")]
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

        [Command("recipe")]
        [Alias("ri", "searchDIY")]
        [Summary("Gets a list of DIY recipe IDs that contain the requested Item Name string.")]
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

            foreach (var item in strings)
            {
                if (!string.Equals(item.Text, itemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ItemParser.InvertedRecipeDictionary.TryGetValue((ushort)item.Value, out var recipeID))
                {
                    await ReplyAsync("Requested item is not a DIY recipe.").ConfigureAwait(false);
                    return;
                }

                var msg = $"{item.Value:X4} {item.Text}: Recipe order code: {recipeID:X3}000016A2";
                await ReplyAsync(Format.Code(msg)).ConfigureAwait(false);
                return;
            }

            var items = ItemParser.GetItemsMatching(itemName, strings).ToArray();
            var matches = new List<string>();
            foreach (var item in items)
            {
                if (!ItemParser.InvertedRecipeDictionary.TryGetValue((ushort)item.Value, out var recipeID))
                    continue;

                var msg = $"{item.Value:X4} {item.Text}: Recipe order code: {recipeID:X3}000016A2";
                matches.Add(msg);
            }

            var result = string.Join(Environment.NewLine, matches);
            if (result.Length == 0)
            {
                await ReplyAsync("No matches found.").ConfigureAwait(false);
                return;
            }

            const int maxLength = 500;
            if (result.Length > maxLength)
                result = result.Substring(0, maxLength) + "...[truncated]";

            await ReplyAsync(Format.Code(result)).ConfigureAwait(false);
        }
    }
}

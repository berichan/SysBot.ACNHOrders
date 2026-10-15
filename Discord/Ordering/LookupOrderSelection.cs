using System.Globalization;
using System.Linq;
using Discord;

namespace SysBot.ACNHOrders
{
    internal static class LookupOrderSelection
    {
        internal static bool TryPickItem(OrderDraft draft, string value, int page, CrossBotConfig config)
        {
            if (!config.AllowLookup || draft.SearchRecipes || page < 0 || page != draft.SearchPage
                || !ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var itemId)) return false;
            var matches = ItemSearchService.Search(draft.Search, draft.Language);
            if (page > System.Math.Max(0, (matches.Length - 1) / ItemSearchService.PageSize)
                || !matches.Skip(page * ItemSearchService.PageSize).Take(ItemSearchService.PageSize).Any(item => item.Value == itemId)) return false;
            draft.PendingItem = OrderPreparation.FullStacks(new[] { new NHSE.Core.Item(itemId) })[0];
            draft.PendingQuantity = 1;
            draft.SearchPage = page;
            return true;
        }

        internal static bool TrySetBody(OrderDraft draft, int body)
        {
            if (draft.PendingItem == null || draft.PendingItem.ItemId == NHSE.Core.Item.DIYRecipe
                || !ItemSearchService.BodyVariants(draft.PendingItem).Any(variant => variant.Value == body)) return false;
            draft.PendingItem.BodyType = body;
            draft.Mode = OrderFillMode.Exact;
            return true;
        }

        internal static bool TrySetFabric(OrderDraft draft, int fabric)
        {
            if (draft.PendingItem == null || draft.PendingItem.ItemId == NHSE.Core.Item.DIYRecipe
                || !ItemSearchService.FabricVariants(draft.PendingItem).Any(variant => variant.Value == fabric)) return false;
            draft.PendingItem.PatternChoice = fabric;
            draft.Mode = OrderFillMode.Exact;
            return true;
        }

        internal static Modal BuildQuantityModal(string customId, OrderDraft draft) => new ModalBuilder()
            .WithTitle("Choose quantity").WithCustomId(customId)
            .AddTextInput($"Number of slots (1 to {MultiItem.MaxOrder - draft.Items.Length})", "quantity",
                minLength: 1, maxLength: 2, required: true, value: draft.PendingQuantity.ToString(CultureInfo.InvariantCulture)).Build();

        internal static bool TrySetQuantity(OrderDraft draft, string text, out string error)
        {
            error = string.Empty;
            if (draft.PendingItem == null) { error = "Choose an item from the search results first."; return false; }
            if (!RecipeSearchService.IsKnown(draft.PendingItem)) { error = "That recipe card is invalid. Use Find recipes to choose it again."; return false; }
            var remaining = MultiItem.MaxOrder - draft.Items.Length;
            if (remaining <= 0) { error = "Your order already has 40 items. Remove an item first."; return false; }
            if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 1 || quantity > remaining)
            { error = $"Enter a whole number from 1 to {remaining}. Each copy uses one order slot."; return false; }
            draft.PendingQuantity = quantity;
            draft.Mode = OrderFillMode.Exact;
            return true;
        }

        internal static bool TryAdd(OrderDraft draft, out int added, out string error)
        {
            added = 0;
            error = string.Empty;
            if (draft.Errors.Length > 0) { error = "Fix the pasted list before adding items. Open Paste / edit list to correct it."; return false; }
            if (draft.PendingItem == null) { error = "Choose an item from the search results first."; return false; }
            if (!RecipeSearchService.IsKnown(draft.PendingItem)) { error = "That recipe card is invalid. Use Find recipes to choose it again."; return false; }
            var remaining = MultiItem.MaxOrder - draft.Items.Length;
            if (remaining <= 0) { error = "Your order already has 40 items. Remove an item first."; return false; }
            if (draft.PendingQuantity < 1 || draft.PendingQuantity > remaining)
            { error = $"Only {remaining} order slots remain. Choose a quantity from 1 to {remaining}."; return false; }
            added = draft.PendingQuantity;
            draft.Items = OrderPreparation.FullStacks(draft.Items.Concat(Enumerable.Repeat(draft.PendingItem, added)));
            draft.PendingItem = null;
            draft.PendingQuantity = 1;
            draft.Input = OrderPreparation.FormatInput(draft.Items);
            return true;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public sealed record RecipeSearchResult(ushort Id, string Name);

    public static class RecipeSearchService
    {
        private static readonly ConcurrentDictionary<string, RecipeSearchResult[]> Catalogues = new();

        public static bool IsKnown(Item item) => item.ItemId != Item.DIYRecipe || RecipeList.Recipes.ContainsKey(item.Count);

        public static Item Create(ushort recipeId)
        {
            if (!RecipeList.Recipes.ContainsKey(recipeId)) throw new ArgumentOutOfRangeException(nameof(recipeId));
            return new Item(Item.DIYRecipe) { Count = recipeId };
        }

        public static string Name(ushort recipeId, string language = "en") => RecipeList.Recipes.TryGetValue(recipeId, out var itemId)
            ? OrderPreparation.GetStrings(OrderPreparation.Languages.Contains(language) ? language : "en").GetItemName(itemId)
            : "Unknown recipe";

        public static RecipeSearchResult[] Search(string query, string language = "en")
        {
            if (!OrderPreparation.Languages.Contains(language)) return Array.Empty<RecipeSearchResult>();
            var catalogue = Catalogues.GetOrAdd(language, lang => RecipeList.Recipes
                .Select(recipe => new RecipeSearchResult(recipe.Key, Name(recipe.Key, lang)))
                .Where(recipe => !string.IsNullOrWhiteSpace(recipe.Name)).ToArray());
            var term = VillagerSearchService.Normalize(query);
            return catalogue.Select(recipe => (Recipe: recipe, Text: VillagerSearchService.Normalize(recipe.Name)))
                .Where(recipe => term.Length == 0 || recipe.Text.Contains(term, StringComparison.Ordinal))
                .OrderBy(recipe => recipe.Text == term ? 0 : recipe.Text.StartsWith(term, StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(recipe => recipe.Text, StringComparer.Ordinal).ThenBy(recipe => recipe.Recipe.Id)
                .Select(recipe => recipe.Recipe).ToArray();
        }

        internal static bool TryPick(OrderDraft draft, string value, int page, CrossBotConfig config, out string error)
        {
            error = "That recipe selection is no longer available. Search again to choose a recipe.";
            if (!config.AllowLookup) { error = "Recipe search is disabled by the host."; return false; }
            if (!draft.SearchRecipes || page < 0 || page != draft.SearchPage
                || !ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var recipeId)) return false;
            var matches = Search(draft.Search, draft.Language);
            if (page > Math.Max(0, (matches.Length - 1) / ItemSearchService.PageSize)
                || !matches.Skip(page * ItemSearchService.PageSize).Take(ItemSearchService.PageSize).Any(recipe => recipe.Id == recipeId)) return false;
            var card = Create(recipeId);
            if (!InternalItemTool.CurrentInstance.IsSaneAfterCorrection(new[] { card }, config.DropConfig))
            { error = "That recipe cannot be ordered."; return false; }
            draft.PendingItem = card;
            draft.PendingQuantity = 1;
            draft.SearchPage = page;
            error = string.Empty;
            return true;
        }
    }
}

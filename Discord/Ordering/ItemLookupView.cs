using System;
using System.Linq;
using Discord;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    internal sealed record ItemLookupPage(Embed Embed, MessageComponent Components, int Page);

    internal static class ItemLookupView
    {
        private static string Short(string text, int max = 100) => text.Length <= max ? text : text[..(max - 1)] + "…";

        internal static Modal RecipeModal(string customId, string query) => new ModalBuilder().WithTitle("Find recipes")
            .WithCustomId(customId).AddTextInput("Recipe name (leave blank to browse)", "query", required: false, maxLength: 100,
                value: query.Length == 0 ? null : Short(query)).Build();

        internal static ItemLookupPage Results(OrderDraft draft, int page, Func<string, int, string> id, Func<string, int, string> selectId)
        {
            var recipes = draft.SearchRecipes;
            var matches = recipes ? RecipeSearchService.Search(draft.Search, draft.Language)
                .Select(recipe => (Name: recipe.Name, Value: recipe.Id.ToString("X4"))).ToArray()
                : ItemSearchService.Search(draft.Search, draft.Language).Select(item => (Name: item.Text, Value: item.Value.ToString("X4"))).ToArray();
            page = Math.Clamp(page, 0, Math.Max(0, (matches.Length - 1) / ItemSearchService.PageSize));
            var description = matches.Length == 0
                ? recipes ? "No matching recipes. Try a shorter name, change the language, or leave the search blank to browse."
                    : "No matching items. Try a shorter name (at least two characters). For recipe cards, choose Recipes."
                : $"{matches.Length} matches, page {page + 1}/{(matches.Length + 9) / 10}.\n"
                    + (recipes ? "Choose a recipe card to add to your order. This search includes DIY and cooking recipes." : "Choose a finished item to add to your order. For recipe cards, choose Recipes.");
            var embed = new EmbedBuilder().WithTitle((recipes ? "Find recipes" : "Find items") + (draft.Search.Length == 0 ? "" : ": " + Short(draft.Search, 80)))
                .WithDescription(description).WithColor(Color.Blue).Build();
            var components = new ComponentBuilder();
            if (matches.Length > 0)
            {
                var menu = new SelectMenuBuilder().WithCustomId(selectId(recipes ? "recipe-pick" : "pick", page))
                    .WithPlaceholder(recipes ? "Choose a recipe card" : "Choose a finished item");
                foreach (var match in matches.Skip(page * ItemSearchService.PageSize).Take(ItemSearchService.PageSize))
                    menu.AddOption(Short(match.Name), match.Value, description: recipes ? "Recipe card" : "Finished item");
                components.WithSelectMenu(menu, 0);
            }
            components.WithSelectMenu(VillagerLookupView.LanguageMenu(draft, selectId("lookup-language", page)), 1)
                .WithButton("Items", id("filter-items", page), recipes ? ButtonStyle.Secondary : ButtonStyle.Primary, row: 2)
                .WithButton("Recipes", id("filter-recipes", page), recipes ? ButtonStyle.Primary : ButtonStyle.Secondary, row: 2)
                .WithButton("Search again", id(recipes ? "recipe-search" : "search", page), row: 2)
                .WithButton("Your order", id("order", 0), row: 2);
            if (page > 0) components.WithButton("Previous", id("results", page - 1), row: 3);
            if ((page + 1) * ItemSearchService.PageSize < matches.Length) components.WithButton("Next", id("results", page + 1), row: 3);
            return new(embed, components.Build(), page);
        }

        internal static ItemLookupPage Selection(OrderDraft draft, Func<string, int, string> id, Func<string, int, string> selectId, string? notice = null)
        {
            var item = OrderPreparation.FullStacks(new[] { draft.PendingItem! })[0];
            var recipe = item.ItemId == Item.DIYRecipe;
            var components = new ComponentBuilder();
            var variants = ItemSearchService.BodyVariants(item);
            if (variants.Count > 1)
            {
                var menu = new SelectMenuBuilder().WithCustomId(selectId("variant", 0)).WithPlaceholder("Choose a variant");
                foreach (var variant in variants) menu.AddOption(Short(variant.Name), variant.Value.ToString(), isDefault: item.BodyType == variant.Value);
                components.WithSelectMenu(menu, 0);
            }
            var fabrics = ItemSearchService.FabricVariants(item);
            if (fabrics.Count > 1)
            {
                var menu = new SelectMenuBuilder().WithCustomId(selectId("fabric", 0)).WithPlaceholder("Choose a fabric variant");
                foreach (var fabric in fabrics) menu.AddOption(Short(fabric.Name), fabric.Value.ToString(), isDefault: item.PatternChoice == fabric.Value);
                components.WithSelectMenu(menu, 1);
            }
            var full = draft.Items.Length >= MultiItem.MaxOrder;
            components.WithButton($"Quantity: {draft.PendingQuantity}", id("quantity", 0), row: 2, disabled: full)
                .WithButton("Add to order", id("add", 0), ButtonStyle.Success, row: 2, disabled: full)
                .WithButton("Back to results", id("results", draft.SearchPage), row: 2);
            var description = $"Quantity: **{draft.PendingQuantity}**. **{Math.Max(0, MultiItem.MaxOrder - draft.Items.Length)} slots available.**\n"
                + (recipe ? "Each copy is one recipe card and uses one order slot. Quantity chooses how many cards to add. Recipe cards have no stack or variant options."
                    : "Each copy uses one order slot. Choose a quantity and any variants. All stackable items use full stacks.")
                + "\nPress **Add to order**, then review your order before confirming. Choosing a quantity selects **Exact** to keep the requested number of copies. **Standard** fills 40 slots and can add extra copies.";
            if (notice != null) description = notice + "\n\n" + description;
            if (full) description = "Your order already has 40 items. Remove an item before adding more.\n\n" + description;
            var embed = new EmbedBuilder().WithTitle(Short(OrderPreparation.Describe(item, draft.Language), 256)).WithDescription(description).WithColor(Color.Blue).Build();
            return new(embed, components.Build(), draft.SearchPage);
        }
    }
}

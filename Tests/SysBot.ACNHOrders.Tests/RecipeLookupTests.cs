using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using NHSE.Core;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public class RecipeBindingProbeModule : InteractionModuleBase<IInteractionContext>
    {
        public BindingCapture Capture { get; set; }
        [ModalInteraction("shop-recipe:*:*")]
        public Task Search(string token, int revision, OrderRecipeSearchModal modal)
        { Capture.Route = new[] { token, revision.ToString() }; Capture.Values = new[] { modal.Query ?? "" }; return Task.CompletedTask; }
        [ModalInteraction("shop-recipe:*:*:*")]
        public Task SuffixedSearch(string token, int revision, string suffix, OrderRecipeSearchModal modal)
        { Capture.Route = new[] { token, revision.ToString(), suffix }; Capture.Values = new[] { modal.Query ?? "" }; return Task.CompletedTask; }
    }

    public sealed class RecipeLookupTests
    {
        private static CrossBotConfig Enabled => new() { AllowLookup = true };
        private static string Id(string action, int page) => $"shop:{action}:private-token:3-{page}:island";
        private static string SelectId(string action, int page) => $"shop-select:{action}:private-token:3-{page}:island";
        public static IEnumerable<object[]> Languages => OrderPreparation.Languages.Select(language => new object[] { language });

        [Theory]
        [MemberData(nameof(Languages))]
        public void EachLanguageFindsNativeNamesAndBrowsesTheCompleteRecipeCatalogue(string language)
        {
            var catalogue = RecipeSearchService.Search("", language);
            Assert.Equal(RecipeList.Recipes.Count, catalogue.Length);
            Assert.Equal(RecipeList.Recipes.Keys.OrderBy(id => id), catalogue.Select(recipe => recipe.Id).OrderBy(id => id));
            foreach (var recipe in catalogue)
            {
                Assert.Equal(OrderPreparation.GetStrings(language).GetItemName(RecipeList.Recipes[recipe.Id]), recipe.Name);
                var card = RecipeSearchService.Create(recipe.Id);
                Assert.Equal(recipe.Id, card.Count);
                Assert.Equal(Item.DIYRecipe, card.ItemId);
                Assert.True(OrderPreparation.IsKnown(card));
            }
            var name = RecipeSearchService.Name(15, language);
            Assert.Contains(RecipeSearchService.Search(name, language), recipe => recipe.Id == 15);
            var draft = new OrderDraft { SearchRecipes = true, Search = name, Language = language };
            Assert.True(RecipeSearchService.TryPick(draft, "000F", 0, Enabled, out var error), error);
            Assert.Equal((ushort)15, draft.PendingItem.Count);
            Assert.Equal(name + " (recipe card)", OrderPreparation.Describe(draft.PendingItem, language));
        }

        [Fact]
        public void RecipeSearchIncludesCookingAndHandlesBlankShortAndAccentInsensitiveQueries()
        {
            Assert.NotEmpty(RecipeSearchService.Search("pizza"));
            var jp = RecipeSearchService.Name(15, "jp");
            Assert.Contains(RecipeSearchService.Search(jp[..1], "jp"), recipe => recipe.Id == 15);
            var accented = RecipeSearchService.Search("", "fr").First(recipe => recipe.Name.Any(ch => ch > 127));
            Assert.Contains(RecipeSearchService.Search(VillagerSearchService.Normalize(accented.Name), "fr"), recipe => recipe.Id == accented.Id);
            Assert.Empty(RecipeSearchService.Search("does-not-exist"));
            Assert.Empty(RecipeSearchService.Search("", "invalid"));
        }

        [Fact]
        public void FinishedItemAndRecipeSelectionsUseDifferentPayloadsAndCannotCrossRoutes()
        {
            var draft = new OrderDraft { Search = "wooden chair" };
            Assert.True(LookupOrderSelection.TryPickItem(draft, "0779", 0, Enabled));
            Assert.Equal((ushort)0x0779, draft.PendingItem.ItemId);
            Assert.False(LookupOrderSelection.TryPickItem(draft, "16A2", 0, Enabled));
            Assert.False(RecipeSearchService.TryPick(draft, "000F", 0, Enabled, out _));
            draft.SearchRecipes = true;
            Assert.False(LookupOrderSelection.TryPickItem(draft, "0779", 0, Enabled));
            Assert.True(RecipeSearchService.TryPick(draft, "000F", 0, Enabled, out _));
            Assert.Equal(0x0000000F000016A2UL, draft.PendingItem.RawValue);
            Assert.DoesNotContain(ItemSearchService.Search("recipe"), item => item.Value == Item.DIYRecipe);
        }

        [Theory]
        [InlineData("FFFF", 0)]
        [InlineData("000F000016A2", 0)]
        [InlineData("000F", -1)]
        [InlineData("000F", int.MaxValue)]
        [InlineData("bad-id", 0)]
        public void InvalidRecipeSelectionsLeaveTheCurrentSelectionUntouched(string value, int page)
        {
            var previous = RecipeSearchService.Create(123);
            var draft = new OrderDraft { SearchRecipes = true, Search = "wooden chair", PendingItem = previous, PendingQuantity = 2 };
            Assert.False(RecipeSearchService.TryPick(draft, value, page, Enabled, out var error));
            Assert.NotEmpty(error);
            Assert.Same(previous, draft.PendingItem);
            Assert.Equal(2, draft.PendingQuantity);
        }

        [Fact]
        public void RecipeSelectionMustBeOnTheDisplayedPageAndHostLookupMustStillBeEnabled()
        {
            var draft = new OrderDraft { SearchRecipes = true };
            var result = RecipeSearchService.Search("")[20];
            Assert.False(RecipeSearchService.TryPick(draft, result.Id.ToString("X4"), 0, Enabled, out _));
            Assert.False(RecipeSearchService.TryPick(draft, result.Id.ToString("X4"), 2, Enabled, out _));
            draft.SearchPage = 2;
            Assert.True(RecipeSearchService.TryPick(draft, result.Id.ToString("X4"), 2, Enabled, out _));
            var previous = draft.PendingItem;
            Assert.False(RecipeSearchService.TryPick(draft, result.Id.ToString("X4"), 2, new CrossBotConfig { AllowLookup = false }, out var error));
            Assert.Contains("disabled", error);
            Assert.Same(previous, draft.PendingItem);
            var store = new OrderDraftStore();
            var revision = draft.Revision;
            store.Changed(1, 2, draft);
            Assert.False(store.Matches(draft, draft.Token, revision));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(40)]
        public void CardQuantityControlsCopiesAndNeverTouchesTheRecipeId(int quantity)
        {
            var card = RecipeSearchService.Create(15);
            var draft = new OrderDraft { PendingItem = card, SearchRecipes = true };
            Assert.True(LookupOrderSelection.TrySetQuantity(draft, quantity.ToString(), out _));
            Assert.Equal((ushort)15, card.Count);
            Assert.True(LookupOrderSelection.TryAdd(draft, out var added, out _));
            Assert.Equal(quantity, added);
            Assert.Equal(OrderFillMode.Exact, draft.Mode);
            Assert.All(draft.Items, item => Assert.Equal(card.RawValue, item.RawValue));
            Assert.NotSame(card, draft.Items[0]);
            if (quantity > 1) Assert.NotSame(draft.Items[0], draft.Items[1]);
            Assert.True(OrderPreparation.TryPrepare(draft.Items, draft.Mode, Enabled, "tester", null, out var prepared, out var error), error);
            Assert.Equal(quantity, prepared.VisibleItems.Length);
            Assert.All(prepared.VisibleItems, item => Assert.Equal(card.RawValue, item.RawValue));
        }

        [Theory]
        [InlineData(OrderFillMode.Standard)]
        [InlineData(OrderFillMode.Exact)]
        [InlineData(OrderFillMode.Catalogue)]
        public void EveryRecipeIdSurvivesFullStacksVariantsAndAllDeliveryModes(OrderFillMode mode)
        {
            foreach (var recipeId in RecipeList.Recipes.Keys)
            {
                var card = RecipeSearchService.Create(recipeId);
                var draft = new OrderDraft { PendingItem = card };
                Assert.Empty(ItemSearchService.BodyVariants(card));
                Assert.Empty(ItemSearchService.FabricVariants(card));
                for (var value = 0; value <= 7; value++)
                {
                    Assert.False(LookupOrderSelection.TrySetBody(draft, value));
                    Assert.False(LookupOrderSelection.TrySetFabric(draft, value));
                    Assert.Equal(recipeId, card.Count);
                }
                var stacked = OrderPreparation.FullStacks(new[] { card });
                Assert.Equal(card.RawValue, stacked[0].RawValue);
                var delivery = mode == OrderFillMode.Exact
                    ? new MultiItem(stacked.Concat(Enumerable.Range(1, 39).Select(_ => new Item(Item.NONE))).ToArray(), true, false, true)
                    : new MultiItem(stacked, mode == OrderFillMode.Catalogue, true, true);
                Assert.All(delivery.ItemArray.Items.Where(item => !item.IsNone), item => Assert.Equal(card.RawValue, item.RawValue));
                Assert.Equal(recipeId, card.Count);
            }
        }

        [Theory]
        [InlineData(OrderFillMode.Standard)]
        [InlineData(OrderFillMode.Exact)]
        [InlineData(OrderFillMode.Catalogue)]
        public void MixedRecipeCardsAndItemsKeepEverySelectedIdAndDistinctReviewGroups(OrderFillMode mode)
        {
            var cards = new[] { RecipeSearchService.Create(15), RecipeSearchService.Create(123) };
            var items = cards.Append(new Item(131)).ToArray();
            Assert.True(OrderPreparation.TryPrepare(items, mode, Enabled, "tester", null, out var prepared, out var error), error);
            Assert.Equal(cards.Select(card => card.RawValue), prepared.VisibleItems.Where(item => item.ItemId == Item.DIYRecipe).Select(item => item.RawValue).Distinct());
            Assert.Equal(2, prepared.VisibleItems.Where(item => item.ItemId == Item.DIYRecipe).GroupBy(ItemParser.GetItemText).Count());
            Assert.All(prepared.Delivery.ItemArray.Items.Where(item => item.ItemId == Item.DIYRecipe), item => Assert.Contains(item.RawValue, cards.Select(card => card.RawValue)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(65535)]
        public async Task UnknownRecipeIdsCannotBeAddedPreparedOrQueued(int id)
        {
            var invalid = new Item(Item.DIYRecipe) { Count = (ushort)id };
            var draft = new OrderDraft { PendingItem = invalid };
            Assert.False(LookupOrderSelection.TrySetQuantity(draft, "1", out _));
            Assert.False(LookupOrderSelection.TryAdd(draft, out _, out _));
            Assert.False(OrderPreparation.TryPrepare(new[] { invalid }, OrderFillMode.Exact, Enabled, "tester", null, out _, out var error));
            Assert.Contains("recipe", error);
            QuickOrderSubmission.ApplyInput(draft, "$order " + OrderPreparation.FormatInput(new[] { invalid }), Enabled);
            var result = await QuickOrderSubmission.SubmitAsync(draft, Enabled, "tester", _ => throw new Exception("Invalid card reached the queue"));
            Assert.False(result.Accepted);
        }

        [Fact]
        public void CardQuantityCannotOverflowOrderCapacityOrChangeTheSelectedCard()
        {
            var card = RecipeSearchService.Create(15);
            var draft = new OrderDraft { PendingItem = card, Items = Enumerable.Repeat(new Item(131), 39).ToArray() };
            Assert.False(LookupOrderSelection.TrySetQuantity(draft, "2", out _));
            Assert.Equal((ushort)15, card.Count);
            draft.PendingQuantity = 2;
            Assert.False(LookupOrderSelection.TryAdd(draft, out _, out _));
            Assert.Equal(39, draft.Items.Length);
            Assert.Equal((ushort)15, card.Count);
        }

        [Fact]
        public void NormalItemCustomizationStillWorksAndRecipeCardsOfferOnlyCardQuantity()
        {
            var draft = new OrderDraft { PendingItem = new Item(131) };
            Assert.True(LookupOrderSelection.TrySetBody(draft, 1));
            Assert.Equal(1, draft.PendingItem.BodyType);
            Assert.Equal(OrderFillMode.Exact, draft.Mode);
            var view = ItemLookupView.Selection(draft, Id, SelectId);
            Assert.Contains(view.Components.Components.SelectMany(row => row.Components).OfType<SelectMenuComponent>(), menu => menu.CustomId == SelectId("variant", 0));
            var card = RecipeSearchService.Create(15);
            draft.PendingItem = card;
            var modal = LookupOrderSelection.BuildQuantityModal("shop-quantity:token:3", draft);
            var input = Assert.IsType<TextInputComponent>(Assert.Single(Assert.Single(modal.Component.Components).Components));
            Assert.StartsWith("Number of slots", input.Label);
            Assert.Equal("1", input.Value);
            Assert.Equal((ushort)15, card.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RecipeOrderCodesKeepTheirIdsWithBothWrappingPolicies(bool wrap)
        {
            var config = Enabled;
            config.DropConfig.UseLegacyDrop = wrap;
            config.DropConfig.WrapAllItems = wrap;
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, "$ordercat 00F000016A2", config);
            Assert.Empty(draft.Errors);
            Assert.Equal((ushort)15, Assert.Single(draft.Items).Count);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", prepared =>
            {
                Assert.All(prepared.Delivery.ItemArray.Items.Where(item => !item.IsNone), item => Assert.Equal((ushort)15, item.Count));
                Assert.Single(prepared.VisibleItems);
                return Task.FromResult(new QueueAttemptResult("Accepted", true));
            });
            Assert.True(result.Accepted);
        }

        [Theory]
        [InlineData("")]
        [InlineData("木")]
        public void RecipeModalAllowsBlankBrowseAndOneCharacterNativeSearch(string query)
        {
            var modal = ItemLookupView.RecipeModal("shop-recipe:token:3:island", query);
            var input = Assert.IsType<TextInputComponent>(Assert.Single(Assert.Single(modal.Component.Components).Components));
            Assert.False(input.Required);
            Assert.Equal(query.Length == 0 ? null : query, input.Value);
            Assert.Equal(100, input.MaxLength);
        }

        [Fact]
        public void RecipeViewsShowCardsLanguagesAndSafeControlsWithCorrectPagination()
        {
            var draft = new OrderDraft { SearchRecipes = true, Language = "jp" };
            var first = ItemLookupView.Results(draft, 0, Id, SelectId);
            var menus = first.Components.Components.SelectMany(row => row.Components).OfType<SelectMenuComponent>().ToArray();
            Assert.Equal(10, menus[0].Options.Count);
            Assert.All(menus[0].Options, option => Assert.Equal("Recipe card", option.Description));
            Assert.Equal(9, menus[1].Options.Count);
            Assert.Single(menus[1].Options.Where(option => option.IsDefault == true));
            var last = ItemLookupView.Results(draft, int.MaxValue, Id, SelectId);
            Assert.DoesNotContain(last.Components.Components.SelectMany(row => row.Components).OfType<ButtonComponent>(), button => button.Label == "Next");
            draft.PendingItem = RecipeSearchService.Create(15); draft.SearchPage = 2;
            var selection = ItemLookupView.Selection(draft, Id, SelectId);
            Assert.Equal("もくせいチェア (recipe card)", selection.Embed.Title);
            Assert.DoesNotContain(selection.Components.Components.SelectMany(row => row.Components), component => component is SelectMenuComponent);
            Assert.Contains("no stack or variant options", selection.Embed.Description);
            Assert.Contains(selection.Components.Components.SelectMany(row => row.Components).OfType<ButtonComponent>(), button => button.Label == "Back to results" && button.CustomId == Id("results", 2));
            Assert.All(first.Components.Components.Concat(selection.Components.Components), row => Assert.InRange(row.Components.Count, 1, 5));
            draft.Search = "no-matching-name";
            Assert.Contains("No matching recipes", ItemLookupView.Results(draft, 0, Id, SelectId).Embed.Description);
        }

        [Fact]
        public void RecipeCardsSurviveEditableInputDraftHistoryAndRestart()
        {
            var directory = Path.Combine(Path.GetTempPath(), "acnh-recipe-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                draft.PendingItem = RecipeSearchService.Create(15); draft.Language = "jp";
                Assert.True(LookupOrderSelection.TrySetQuantity(draft, "2", out _));
                Assert.True(LookupOrderSelection.TryAdd(draft, out _, out _));
                store.Changed(10, 20, draft);
                var loaded = new OrderDraftStore(directory).Get(10, 20);
                Assert.Equal("jp", loaded.Language);
                Assert.Equal(draft.Items.Select(item => item.RawValue), loaded.Items.Select(item => item.RawValue));
                var parsed = OrderPreparation.Parse(loaded.EditableInput, Enabled.DropConfig);
                Assert.Empty(parsed.Errors);
                Assert.All(parsed.Items, item => { Assert.Equal((ushort)15, item.Count); Assert.True(item.IsDropped); });
                var dropped = OrderPreparation.Clone(draft.Items);
                foreach (var item in dropped) item.IsDropped = true;
                Assert.Equal(dropped.Select(item => item.RawValue), parsed.Items.Select(item => item.RawValue));
                Assert.True(OrderPreparation.TryPrepare(loaded.Items, loaded.Mode, Enabled, "tester", null, out var prepared, out var error), error);
                Assert.Equal(2, prepared.VisibleItems.Length);
                OrderHistory.Save(10, 20, draft, directory);
                Assert.True(OrderHistory.TryLoad(10, 20, out var saved, out _, out _, out _, directory, directory));
                Assert.Equal(draft.Items.Select(item => item.RawValue), Item.GetArray(saved.Items).Select(item => item.RawValue));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    foreach (var path in Directory.GetFiles(directory)) File.Delete(path);
                    Directory.Delete(directory);
                }
            }
        }

        [Theory]
        [InlineData(null, "もくせいチェア")]
        [InlineData("island", "")]
        public async Task RecipeModalBindsNativeNamesBlankBrowseAndIslandSuffix(string suffix, string query)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<RecipeBindingProbeModule>(services);
            var id = "shop-recipe:private-token:12" + (suffix == null ? "" : ":" + suffix);
            var fields = query.Length == 0 ? Array.Empty<IComponentInteractionData>()
                : new[] { InterfaceStub.Create<IComponentInteractionData>(("CustomId", "query"), ("Value", query), ("Type", ComponentType.TextInput)) };
            var data = InterfaceStub.Create<IModalInteractionData>(("CustomId", id), ("Components", fields));
            var user = InterfaceStub.Create<IUser>(("Id", 123UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", user));
            var context = new InteractionContext(client, interaction);
            var pattern = InteractionRouting.FindPattern(id, suffix, service.ModalCommands.Select(command => command.Name));
            Assert.NotNull(pattern);
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(new[] { query }, capture.Values);
            Assert.Equal(suffix == null ? new[] { "private-token", "12" } : new[] { "private-token", "12", suffix }, capture.Route);
        }
    }
}

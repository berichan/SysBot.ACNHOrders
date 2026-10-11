using System;
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
    public class VillagerBindingProbeModule : InteractionModuleBase<IInteractionContext>
    {
        public BindingCapture Capture { get; set; }
        [ModalInteraction("shop-villager:*:*")]
        public Task Search(string token, int revision, OrderVillagerSearchModal modal)
        { Capture.Route = new[] { token, revision.ToString() }; Capture.Values = new[] { modal.Query ?? "" }; return Task.CompletedTask; }
        [ModalInteraction("shop-villager:*:*:*")]
        public Task SuffixedSearch(string token, int revision, string suffix, OrderVillagerSearchModal modal)
        { Capture.Route = new[] { token, revision.ToString(), suffix }; Capture.Values = new[] { modal.Query ?? "" }; return Task.CompletedTask; }
    }

    public sealed class VillagerLookupTests
    {
        public static TheoryData<string, string> Names => new()
        {
            { "en", "Raymond" }, { "jp", "ジャック" }, { "fr", "Raymond" }, { "de", "Gunnar" },
            { "es", "Narciso" }, { "it", "Raimondo" }, { "ko", "잭슨" }, { "chs", "杰克" }, { "cht", "傑克" }
        };
        private static CrossBotConfig Enabled => new() { AllowLookup = true, AllowVillagerInjection = true };
        private static string Id(string action, int page) => $"shop:{action}:token:2-{page}:island";
        private static string SelectId(string action, int page) => $"shop-select:{action}:token:2-{page}:island";

        [Theory]
        [MemberData(nameof(Names))]
        public void LocalizedNamesSearchResolvePrepareAndDisplayInEveryLanguage(string language, string name)
        {
            var result = VillagerSearchService.Search(name, language)[0];
            Assert.Equal("cat23", result.Id);
            Assert.Equal(name, result.Name);
            Assert.True(VillagerSearchService.TryResolve(name, language, out var id, out var error), error);
            Assert.Equal("cat23", id);
            Assert.True(OrderPreparation.TryPrepare(new[] { new Item(131) }, OrderFillMode.Exact, Enabled, "tester", name,
                out var prepared, out error, language), error);
            Assert.Equal(name, prepared.Villager.GameName);
            Assert.Equal(name, VillagerSearchService.DisplaySelection(new OrderDraft { Language = language, Villager = id }));
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void EveryLanguageOffersTheSameOrderableIdsAndNamesResolveWithoutChoosingAmbiguousMatches(string language, string knownName)
        {
            var results = VillagerSearchService.Search("", language);
            Assert.True(results.Length > 300);
            Assert.Contains(results, entry => entry.Id == "cat23" && entry.Name == knownName);
            Assert.Equal(VillagerSearchService.Search("").Select(entry => entry.Id).OrderBy(id => id), results.Select(entry => entry.Id).OrderBy(id => id));
            Assert.All(results, entry =>
            {
                Assert.False(VillagerOrderParser.IsUnadoptable(entry.Id));
                Assert.False(string.IsNullOrWhiteSpace(entry.Name));
                Assert.True(VillagerSearchService.TryResolve(entry.Id, language, out var resolved, out _));
                Assert.Equal(entry.Id, resolved);
            });
            foreach (var group in results.GroupBy(entry => VillagerSearchService.Normalize(entry.Name)))
            {
                var entry = group.First();
                var resolved = VillagerSearchService.TryResolve(entry.Name, language, out var id, out var error);
                if (group.Count() == 1) { Assert.True(resolved, $"{language}: {entry.Id} = {entry.Name}: {error}"); Assert.Equal(entry.Id, id); }
                else { Assert.False(resolved); Assert.Contains("More than one", error); }
            }
        }

        [Theory]
        [InlineData("cbr18")]
        [InlineData("der10")]
        [InlineData("elp11")]
        [InlineData("gor11")]
        [InlineData("rbt20")]
        [InlineData("shp14")]
        [InlineData("owl")]
        [InlineData("sza")]
        [InlineData("brd20")]
        [InlineData("der12")]
        [InlineData("does-not-exist")]
        public void RestrictedVillagersAndNpcsNeverAppearOrPassSelectionAndPreparation(string id)
        {
            Assert.False(VillagerSearchService.IsOrderable(id));
            foreach (var language in OrderPreparation.Languages)
            {
                Assert.DoesNotContain(VillagerSearchService.Search("", language), entry => entry.Id == id);
                Assert.Empty(VillagerSearchService.Search(id, language));
                Assert.False(VillagerSearchService.TryResolve(id, language, out _, out _));
            }
            var draft = new OrderDraft { Items = new[] { new Item(131) }, PendingVillager = id };
            Assert.False(VillagerOrderSelection.TryPick(draft, id, Enabled, out _));
            Assert.False(VillagerOrderSelection.TryAdd(draft, Enabled, out _));
            Assert.Null(draft.Villager);
            Assert.False(OrderPreparation.TryPrepare(draft.Items, draft.Mode, Enabled, "tester", id, out _, out _));
        }

        [Theory]
        [InlineData("jp", "ジ")]
        [InlineData("ko", "잭")]
        [InlineData("chs", "杰")]
        [InlineData("cht", "傑")]
        public void SingleCharacterSearchesFindLocalizedNames(string language, string query)
        {
            Assert.Contains(VillagerSearchService.Search(query, language), entry => entry.Id == "cat23");
        }

        [Theory]
        [InlineData("fr", "Cleo", "ant05")]
        [InlineData("de", "jorg", "flg07")]
        [InlineData("it", "giosue", "dog15")]
        [InlineData("de", "Ｇｕｎｎａｒ", "cat23")]
        [InlineData("jp", "シ\u3099ャック", "cat23")]
        public void SearchAndManualInputHandleAccentsCaseAndUnicodeForms(string language, string text, string expected)
        {
            Assert.Equal(expected, VillagerSearchService.Search(text, language)[0].Id);
            Assert.True(VillagerSearchService.TryResolve(text, language, out var id, out var error), error);
            Assert.Equal(expected, id);
            Assert.NotEqual(VillagerSearchService.Normalize("ジ"), VillagerSearchService.Normalize("シ"));
        }

        [Fact]
        public void VillagerNameDocResolvesToTheRabbitWhileTheSpecialNpcIdCannotBeSelected()
        {
            Assert.False(VillagerSearchService.IsOrderable("doc"));
            Assert.True(VillagerSearchService.TryResolve("Doc", "en", out var id, out _));
            Assert.Equal("rbt10", id);
            Assert.False(VillagerOrderSelection.TryPick(new OrderDraft(), "doc", Enabled, out _));
        }

        [Fact]
        public void EveryOrderableVillagerHasUsableBundledDeliveryData()
        {
            foreach (var villager in VillagerSearchService.Search(""))
            {
                Assert.True(OrderPreparation.TryPrepare(new[] { new Item(131) }, OrderFillMode.Exact, Enabled, "tester", villager.Id,
                    out var prepared, out var error), $"{villager.Id} = {villager.Name}: {error}");
                Assert.NotNull(prepared.Villager);
                Assert.Equal(villager.Name, prepared.Villager.GameName);
            }
        }

        [Fact]
        public void ChangingAVillagerInvalidatesPreviewAndConfirmationRechecksHostPolicy()
        {
            var config = Enabled;
            var store = new OrderDraftStore();
            var draft = store.Get(10, 20);
            draft.Items = new[] { new Item(131) };
            draft.Villager = "cat00";
            Assert.True(OrderPreparation.TryPrepare(draft.Items, draft.Mode, config, "tester", draft.Villager, out var preview, out _));
            draft.Preview = preview; draft.PreviewRevision = draft.Revision;
            var revision = draft.Revision;
            Assert.True(VillagerOrderSelection.TryPick(draft, "cat23", config, out _));
            Assert.True(VillagerOrderSelection.TryAdd(draft, config, out _));
            store.Changed(10, 20, draft);
            Assert.Null(draft.Preview);
            Assert.Equal(-1, draft.PreviewRevision);
            Assert.False(store.Matches(draft, draft.Token, revision));
            config.AllowVillagerInjection = false;
            Assert.False(OrderPreparation.TryPrepare(draft.Items, draft.Mode, config, "tester", draft.Villager, out _, out var error));
            Assert.Contains("disabled", error);
        }

        [Fact]
        public void VillagerOnlyOrdersAndTypedNamesWorkWhenOnlySearchIsDisabled()
        {
            Assert.True(OrderPreparation.TryPrepare(Array.Empty<Item>(), OrderFillMode.Exact, Enabled, "tester", "cat23", out _, out var error), error);
            var config = Enabled;
            config.AllowLookup = false;
            Assert.False(VillagerOrderSelection.CanSearch(config, out _));
            Assert.True(OrderPreparation.TryPrepare(new[] { new Item(131) }, OrderFillMode.Exact, config, "tester", "ジャック", out _, out error, "jp"), error);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void HostPoliciesAreRecheckedWhenSelectingAndAdding(bool lookup, bool villagers)
        {
            var config = new CrossBotConfig { AllowLookup = lookup, AllowVillagerInjection = villagers };
            var draft = new OrderDraft { PendingVillager = "cat23", Villager = "cat00" };
            Assert.False(VillagerOrderSelection.CanSearch(config, out var error));
            Assert.Contains("disabled", error);
            Assert.False(VillagerOrderSelection.TryPick(draft, "cat23", config, out _));
            Assert.False(VillagerOrderSelection.TryAdd(draft, config, out _));
            Assert.Equal("cat00", draft.Villager);
            VillagerOrderSelection.Set(draft, null);
            Assert.Null(draft.Villager);
        }

        [Fact]
        public void AddReplaceAndRemovePreserveItemsQuantitiesModesAndUnrelatedErrors()
        {
            var item = new Item(131) { BodyType = 1 };
            var items = new[] { item, OrderPreparation.Clone(new[] { item })[0] };
            var draft = new OrderDraft { Items = items, Mode = OrderFillMode.Catalogue, PendingItem = item, PendingQuantity = 12,
                Errors = new[] { "Unknown item", OrderPreparation.VillagerLengthError }, Input = "invalid list" };
            Assert.True(VillagerOrderSelection.TryPick(draft, "cat23", Enabled, out _));
            Assert.Null(draft.Villager);
            Assert.Null(draft.Preview);
            Assert.True(VillagerOrderSelection.TryAdd(draft, Enabled, out _));
            Assert.Equal("cat23", draft.Villager);
            Assert.Null(draft.PendingVillager);
            Assert.Equal(new[] { "Unknown item" }, draft.Errors);
            Assert.Equal("invalid list", draft.Input);
            Assert.True(VillagerOrderSelection.TryPick(draft, "cat00", Enabled, out _));
            Assert.True(VillagerOrderSelection.TryAdd(draft, Enabled, out _));
            Assert.Equal("cat00", draft.Villager);
            VillagerOrderSelection.Set(draft, null);
            Assert.Same(items, draft.Items);
            Assert.Equal(1, draft.Items[0].BodyType);
            Assert.Equal(OrderFillMode.Catalogue, draft.Mode);
            Assert.Same(item, draft.PendingItem);
            Assert.Equal(12, draft.PendingQuantity);
            Assert.Equal(new[] { "Unknown item" }, draft.Errors);
            Assert.False(VillagerOrderSelection.TryAdd(draft, Enabled, out _));
        }

        [Fact]
        public void LanguageChangesRelabelSelectionsAndPreserveCandidatesAndExistingLocalizedOptions()
        {
            var draft = new OrderDraft { Language = "de", Villager = "Gunnar", PendingVillager = "cat00", VillagerSearch = "Gunnar" };
            Assert.True(VillagerOrderSelection.TryResolveOption(draft, "Gunnar", "jp", out var preserved, out _));
            Assert.Equal("cat23", preserved);
            Assert.True(VillagerOrderSelection.TryChangeLanguage(draft, "jp", Enabled, out _));
            Assert.Equal("cat23", draft.Villager);
            Assert.Equal("cat00", draft.PendingVillager);
            Assert.Equal("ジャック", VillagerSearchService.DisplaySelection(draft));
            Assert.Empty(draft.VillagerSearch);
            Assert.False(VillagerOrderSelection.TryChangeLanguage(draft, "unsupported", Enabled, out _));
            Assert.Equal("jp", draft.Language);
            Assert.Empty(VillagerSearchService.Search("", "unsupported"));
            Assert.False(VillagerSearchService.TryResolve("cat23", "unsupported", out _, out _));
            Assert.True(VillagerSearchService.TryResolve("Raymond", "jp", out var legacy, out _));
            Assert.Equal("cat23", legacy);
        }

        [Theory]
        [MemberData(nameof(Names))]
        public async Task NativePastedCommandsKeepCanonicalIdsAndUseTheSelectedLanguageAtSubmission(string language, string name)
        {
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, $"$order 0083 villager:{name} language:{language}", Enabled);
            Assert.Empty(draft.Errors);
            Assert.Equal("cat23", draft.Villager);
            var result = await QuickOrderSubmission.SubmitAsync(draft, Enabled, "tester", prepared =>
            {
                Assert.Equal(name, prepared.Villager.GameName);
                return Task.FromResult(new QueueAttemptResult("Accepted", true));
            });
            Assert.True(result.Accepted);
        }

        [Theory]
        [InlineData("chs", "招财猫")]
        [InlineData("cht", "招財貓")]
        public void ChineseItemLookupsAndParsingUseChineseResourcesToo(string language, string itemName)
        {
            Assert.Equal((ushort)131, ItemSearchService.Search(itemName, language)[0].Value);
            var input = OrderPreparation.Parse(itemName, new DropBotConfig(), language: language);
            Assert.Empty(input.Errors);
            Assert.Equal((ushort)131, Assert.Single(input.Items).ItemId);
        }

        [Theory]
        [MemberData(nameof(Names))]
        public async Task VillagerOnlyCommandsAcceptNamesInEverySupportedLanguage(string language, string name)
        {
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, $"$ordercat villager:{name} language:{language}", Enabled);
            Assert.Empty(draft.Errors);
            Assert.Equal("cat23", draft.Villager);
            var result = await QuickOrderSubmission.SubmitAsync(draft, Enabled, "tester", prepared =>
            {
                Assert.Equal(name, prepared.Villager.GameName);
                Assert.Empty(prepared.RequestedItems);
                Assert.Empty(prepared.VisibleItems);
                return Task.FromResult(new QueueAttemptResult("Accepted", true));
            });
            Assert.True(result.Accepted);
        }

        [Theory]
        [InlineData(OrderFillMode.Standard)]
        [InlineData(OrderFillMode.Exact)]
        [InlineData(OrderFillMode.Catalogue)]
        public void GuidedVillagerOnlyOrdersPrepareEmptyPickupSlotsAndRejectAnEmptyOrder(OrderFillMode mode)
        {
            Assert.True(OrderPreparation.TryPrepare(Array.Empty<Item>(), mode, Enabled, "tester", "ost10", out var prepared, out var error), error);
            Assert.NotNull(prepared.Villager);
            Assert.Empty(prepared.VisibleItems);
            Assert.All(prepared.Delivery.ItemArray.Items, item => Assert.True(item.IsNone));
            Assert.Equal(MultiItem.MaxOrder * Item.SIZE, prepared.Delivery.ItemArray.Write().Length);
            Assert.Contains("Villager only", OrderPreparation.FillInstructions(prepared));
            Assert.False(OrderPreparation.TryPrepare(Array.Empty<Item>(), mode, Enabled, "tester", null, out _, out _));
        }

        [Fact]
        public void VillagerOnlyOrdersSurviveSavedDraftAndHistoryReloads()
        {
            var directory = Path.Combine(Path.GetTempPath(), "acnh-villager-only-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                QuickOrderSubmission.ApplyInput(draft, "$order villager:ost10", Enabled);
                store.Changed(10, 20, draft);
                var loaded = new OrderDraftStore(directory).Get(10, 20);
                Assert.Equal("ost10", loaded.Villager);
                Assert.Empty(loaded.Items);
                Assert.Empty(loaded.Errors);
                OrderHistory.Save(10, 20, draft, directory);
                Assert.True(OrderHistory.TryLoad(10, 20, out var saved, out _, out _, out _, directory, directory));
                Assert.Equal("ost10", saved.Villager);
                Assert.Empty(saved.Items);
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

        [Fact]
        public void SavedVillagersAndHistorySurviveRestartWithLocalizedNamesAndRejectOldControls()
        {
            var directory = Path.Combine(Path.GetTempPath(), "acnh-villager-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                draft.Items = new[] { new Item(131) };
                draft.Language = "cht";
                var revision = draft.Revision;
                Assert.True(VillagerOrderSelection.TryPick(draft, "cat23", Enabled, out _));
                store.Changed(10, 20, draft);
                Assert.False(store.Matches(draft, draft.Token, revision));
                Assert.True(VillagerOrderSelection.TryAdd(draft, Enabled, out _));
                store.Changed(10, 20, draft);
                OrderHistory.Save(10, 20, draft, directory);
                var restored = new OrderDraftStore(directory).Get(10, 20);
                Assert.Equal("cat23", restored.Villager);
                Assert.Equal("傑克", VillagerSearchService.DisplaySelection(restored));
                Assert.Null(restored.PendingVillager);
                Assert.Null(restored.Preview);
                Assert.Single(restored.Items);
                Assert.True(OrderHistory.TryLoad(10, 20, out var saved, out _, out _, out _, directory, directory));
                Assert.Equal("cat23", saved.Villager);
                Assert.Equal("cht", saved.Language);
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
        [InlineData("")]
        [InlineData("ジ")]
        [InlineData("Gunnar")]
        public void SearchModalAllowsEmptyBrowseAndSingleCharacterQueries(string query)
        {
            var modal = VillagerLookupView.BuildSearchModal("shop-villager:token:2:island", query);
            var input = Assert.IsType<TextInputComponent>(Assert.Single(Assert.Single(modal.Component.Components).Components));
            Assert.False(input.Required);
            Assert.Equal(100, input.MaxLength);
            Assert.Equal(query.Length == 0 ? null : query, input.Value);
        }

        [Fact]
        public void ResultsPaginateWithinDiscordLimitsAndCandidateExplainsReplacementAndAdoption()
        {
            var draft = new OrderDraft { Language = "jp", Villager = "cat23", PendingVillager = "cat00" };
            var first = VillagerLookupView.Results(draft, -1, Id, SelectId);
            Assert.Equal(0, first.Page);
            Assert.Contains("ジャック", first.Embed.Description);
            var menus = first.Components.Components.SelectMany(row => row.Components).OfType<SelectMenuComponent>().ToArray();
            Assert.Equal(10, menus[0].Options.Count);
            Assert.All(menus[0].Options, option => Assert.True(VillagerSearchService.IsOrderable(option.Value)));
            Assert.Equal(9, menus[1].Options.Count);
            Assert.Single(menus[1].Options.Where(option => option.IsDefault == true));
            Assert.All(first.Components.Components, row => Assert.InRange(row.Components.Count, 1, 5));
            var last = VillagerLookupView.Results(draft, int.MaxValue, Id, SelectId);
            Assert.DoesNotContain(last.Components.Components.SelectMany(row => row.Components).OfType<ButtonComponent>(), button => button.Label == "Next");
            var candidate = VillagerLookupView.Selection(draft, Id, SelectId);
            Assert.Contains("empty housing plot", candidate.Embed.Description);
            Assert.Contains("ジャック", candidate.Embed.Description);
            Assert.Equal(VillagerSearchService.Name("cat00", "jp"), candidate.Embed.Title);
            Assert.Contains(candidate.Components.Components.SelectMany(row => row.Components).OfType<ButtonComponent>(), button => button.CustomId == Id("villager-add", 0));
            draft.VillagerSearch = "does-not-exist";
            Assert.Contains("No orderable", VillagerLookupView.Results(draft, 0, Id, SelectId).Embed.Description);
        }

        [Theory]
        [InlineData(null, "ジャック")]
        [InlineData("island", "")]
        public async Task RealDiscordModalBindingAcceptsNativeNamesAndBlankBrowseWithIslandRouting(string suffix, string query)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<VillagerBindingProbeModule>(services);
            var id = "shop-villager:private-token:12" + (suffix == null ? "" : ":" + suffix);
            var fields = query.Length == 0 ? Array.Empty<IComponentInteractionData>()
                : new[] { InterfaceStub.Create<IComponentInteractionData>(("CustomId", "query"), ("Value", query), ("Type", ComponentType.TextInput)) };
            var data = InterfaceStub.Create<IModalInteractionData>(("CustomId", id), ("Components", fields));
            var user = InterfaceStub.Create<IUser>(("Id", 123UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", user));
            var context = new InteractionContext(client, interaction);
            var pattern = InteractionRouting.FindPattern(id, suffix, service.ModalCommands.Select(command => command.Name));
            Assert.NotNull(pattern);
            Assert.Null(InteractionRouting.FindPattern(id + ":other", suffix, service.ModalCommands.Select(command => command.Name)));
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(new[] { query }, capture.Values);
            Assert.Equal(suffix == null ? new[] { "private-token", "12" } : new[] { "private-token", "12", suffix }, capture.Route);
        }
    }
}

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
    public class QuickBindingProbeModule : InteractionModuleBase<IInteractionContext>
    {
        public BindingCapture Capture { get; set; }
        [ModalInteraction("shop-quick:*:*")]
        public Task Quick(string token, int revision, OrderQuickModal modal)
        { Capture.Route = new[] { token, revision.ToString() }; Capture.Values = new[] { modal.Command }; return Task.CompletedTask; }
        [ModalInteraction("shop-quick:*:*:*")]
        public Task SuffixedQuick(string token, int revision, string suffix, OrderQuickModal modal)
        { Capture.Route = new[] { token, revision.ToString(), suffix }; Capture.Values = new[] { modal.Command }; return Task.CompletedTask; }
    }

    public sealed class QuickOrderTests
    {
        [Fact]
        public void QuickModalOpensWithAnEmptySavedCommand()
        {
            var modal = OrderExperience.BuildQuickModal("shop-quick:private-token:0", new OrderDraft().Input);
            Assert.Equal("shop-quick:private-token:0", modal.CustomId);
            var input = Assert.IsType<TextInputComponent>(Assert.Single(Assert.Single(modal.Component.Components).Components));
            Assert.Null(input.Value);
            Assert.True(string.IsNullOrEmpty(input.Placeholder));
            Assert.Equal(1, input.MinLength);
            Assert.Equal(4000, input.MaxLength);
            Assert.True(input.Required);
        }

        [Theory]
        [InlineData("x")]
        [InlineData("$ordercat 0083 3107")]
        public void QuickModalKeepsTheSavedCommandForRetry(string command)
        {
            var modal = OrderExperience.BuildQuickModal("shop-quick:private-token:1:island", command);
            var input = Assert.IsType<TextInputComponent>(Assert.Single(Assert.Single(modal.Component.Components).Components));
            Assert.Equal(command, input.Value);
            Assert.Equal("shop-quick:private-token:1:island", modal.CustomId);
        }

        [Fact]
        public void QuickModalLimitsPrefilledCommandsToDiscordsMaximumLength()
        {
            var modal = OrderExperience.BuildQuickModal("shop-quick:private-token:1", new string('x', 4001));
            var input = Assert.IsType<TextInputComponent>(Assert.Single(Assert.Single(modal.Component.Components).Components));
            Assert.Equal(4000, input.Value.Length);
        }

        [Theory]
        [InlineData("$order 0083 3107", OrderFillMode.Standard)]
        [InlineData("!ordercat lucky cat, nook miles ticket", OrderFillMode.Catalogue)]
        [InlineData("/ordercat_berry items:0083 3107", OrderFillMode.Catalogue)]
        [InlineData("%ORDERCAT 0083 3107", OrderFillMode.Catalogue)]
        [InlineData("0083 3107", OrderFillMode.Standard)]
        public async Task QuickSubmissionInfersTheModeAndCallsTheQueueDirectlyWithoutAPreview(string input, OrderFillMode mode)
        {
            var config = new CrossBotConfig { Prefix = "%" };
            var draft = new OrderDraft { Mode = OrderFillMode.Exact, Villager = "cat23", Language = "fr" };
            QuickOrderSubmission.ApplyInput(draft, input, config);
            int submissions = 0;
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", prepared =>
            {
                submissions++;
                Assert.Equal(mode, prepared.Mode);
                Assert.Null(prepared.Villager);
                Assert.Equal(40, prepared.Delivery.ItemArray.Items.Count);
                return Task.FromResult(new QueueAttemptResult("Accepted", true));
            });
            Assert.True(result.Accepted);
            Assert.Equal(1, submissions);
            Assert.Null(draft.Preview);
            Assert.Equal(-1, draft.PreviewRevision);
            Assert.Equal("en", draft.Language);
            Assert.Equal(input, draft.Input);
        }

        [Theory]
        [InlineData("$order lucky cat, not-an-item")]
        [InlineData("$ordercat not-an-item")]
        [InlineData("$order")]
        [InlineData("$drop 0083")]
        public async Task InvalidQuickInputNeverCallsTheQueue(string input)
        {
            var config = new CrossBotConfig();
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, input, config);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", _ => throw new Exception("Invalid input must not enter the queue"));
            Assert.False(result.Accepted);
            Assert.NotEmpty(result.Message);
            Assert.Equal(input, draft.Input);
        }

        [Fact]
        public async Task QuickSubmissionEnforcesTheItemLimitAndVillagerPolicy()
        {
            var config = new CrossBotConfig { AllowVillagerInjection = false };
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, "$order " + string.Join(" ", Enumerable.Repeat("0083", 41)), config);
            Assert.False((await QuickOrderSubmission.SubmitAsync(draft, config, "tester", _ => throw new Exception("Over-limit order"))).Accepted);
            QuickOrderSubmission.ApplyInput(draft, "$order 0083 villager:cat23", config);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", _ => throw new Exception("Disabled villager"));
            Assert.False(result.Accepted);
            Assert.Contains("disabled", result.Message);
        }

        [Theory]
        [InlineData("$order", OrderFillMode.Standard)]
        [InlineData("$ordercat", OrderFillMode.Catalogue)]
        public async Task QuickDeliveryUsesFullStacks(string command, OrderFillMode mode)
        {
            var stack = GameInfo.Strings.ItemDataSource.Select(entry => new Item((ushort)entry.Value))
                .First(item => ItemInfo.TryGetMaxStackCount(item, out var max) && max > 5 && !InternalItemTool.CurrentInstance.IsInternalItem(item.ItemId));
            stack.Count = 4;
            ItemInfo.TryGetMaxStackCount(stack, out var maximum);
            var draft = new OrderDraft();
            var config = new CrossBotConfig();
            QuickOrderSubmission.ApplyInput(draft, command + " " + OrderPreparation.FormatInput(new[] { stack }), config);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", prepared =>
            {
                Assert.Equal(mode, prepared.Mode);
                Assert.All(prepared.VisibleItems, item => Assert.Equal((ushort)(maximum - 1), item.Count));
                Assert.All(prepared.Delivery.ItemArray.Items.Where(item => !item.IsNone), item => Assert.Equal((ushort)(maximum - 1), item.Count));
                return Task.FromResult(new QueueAttemptResult("Accepted", true));
            });
            Assert.True(result.Accepted);
        }

        [Fact]
        public async Task QueueRejectionKeepsTheQuickCommandAvailableForRetry()
        {
            var draft = new OrderDraft();
            var config = new CrossBotConfig();
            const string input = "$ordercat 0083";
            QuickOrderSubmission.ApplyInput(draft, input, config);
            var rejected = new QueueAttemptResult("DMs are closed", false);
            Assert.Same(rejected, await QuickOrderSubmission.SubmitAsync(draft, config, "tester", _ => Task.FromResult(rejected)));
            Assert.Equal(input, draft.Input);
            Assert.Single(draft.Items);
            Assert.Equal(OrderFillMode.Catalogue, draft.Mode);
        }

        [Fact]
        public async Task QuickCommandKeepsExplicitVillagerLanguageAndModeInAcceptedHistory()
        {
            var config = new CrossBotConfig { AllowVillagerInjection = true };
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, "$ordercat 0083 villager:cat23 language:fr", config);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", prepared =>
            {
                Assert.NotNull(prepared.Villager);
                Assert.Equal(OrderFillMode.Catalogue, prepared.Mode);
                return Task.FromResult(new QueueAttemptResult("Accepted", true));
            });
            Assert.True(result.Accepted);
            var directory = Path.Combine(Path.GetTempPath(), "acnh-quick-history-" + Guid.NewGuid().ToString("N"));
            try
            {
                OrderHistory.Save(10, 20, draft, directory);
                Assert.True(OrderHistory.TryLoad(10, 20, out var saved, out _, out _, out _, directory, directory));
                Assert.Equal("cat23", saved.Villager);
                Assert.Equal("fr", saved.Language);
                Assert.Equal(OrderFillMode.Catalogue, saved.Mode);
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
        [InlineData("$order villager:ost10", OrderFillMode.Standard)]
        [InlineData("$ordercat villager:ost10", OrderFillMode.Catalogue)]
        [InlineData("/order_island villager:ost10 language:jp", OrderFillMode.Standard)]
        [InlineData("villager:ost10", OrderFillMode.Standard)]
        public async Task VillagerOnlyQuickOrdersReachTheQueueWithoutAddingPickupItems(string input, OrderFillMode mode)
        {
            var config = new CrossBotConfig { AllowVillagerInjection = true };
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, input, config);
            Assert.Empty(draft.Errors);
            Assert.Empty(draft.Items);
            Assert.Equal("ost10", draft.Villager);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", prepared =>
            {
                Assert.Equal(mode, prepared.Mode);
                Assert.Empty(prepared.VisibleItems);
                Assert.NotNull(prepared.Villager);
                Assert.Equal(VillagerSearchService.Name("ost10", draft.Language), prepared.Villager.GameName);
                Assert.Equal(40, prepared.Delivery.ItemArray.Items.Count);
                Assert.All(prepared.Delivery.ItemArray.Items, item => Assert.True(item.IsNone));
                return Task.FromResult(new QueueAttemptResult("Accepted", true));
            });
            Assert.True(result.Accepted);
        }

        [Theory]
        [InlineData("$order villager:unknown-villager", true)]
        [InlineData("$order villager:shp14", true)]
        [InlineData("$order villager:ost10", false)]
        [InlineData("$order unknown-item villager:ost10", true)]
        public async Task InvalidOrDisabledVillagerOnlyRequestsNeverQueue(string input, bool enabled)
        {
            var config = new CrossBotConfig { AllowVillagerInjection = enabled };
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, input, config);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", _ => throw new Exception("Invalid request must not enter the queue"));
            Assert.False(result.Accepted);
            Assert.NotEmpty(result.Message);
        }

        [Theory]
        [InlineData("$order 0083,villager:cat23", OrderFillMode.Standard)]
        [InlineData("$order 0083villager:cat23", OrderFillMode.Standard)]
        [InlineData("$ordercat lucky cat,VILLAGER:CAT23", OrderFillMode.Catalogue)]
        [InlineData("$ordercat 0083\nvillager:\ncat23\nlanguage:en", OrderFillMode.Catalogue)]
        [InlineData("$order 0083,villager:cat23,language:en", OrderFillMode.Standard)]
        public async Task LegacyVillagerCommandsReachQuickQueueWithTheRequestedVillager(string input, OrderFillMode mode)
        {
            var config = new CrossBotConfig { AllowVillagerInjection = true };
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, input, config);
            Assert.Empty(draft.Errors);
            Assert.Equal("cat23", draft.Villager);
            var calls = 0;
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", prepared =>
            {
                calls++;
                Assert.Equal(mode, prepared.Mode);
                Assert.Single(prepared.RequestedItems);
                Assert.NotNull(prepared.Villager);
                Assert.Equal("Raymond", prepared.Villager.GameName);
                return Task.FromResult(new QueueAttemptResult("Accepted", true));
            });
            Assert.True(result.Accepted);
            Assert.Equal(1, calls);
        }

        [Theory]
        [InlineData("$order 0083 villager:")]
        [InlineData("$ordercat 0083,villager:")]
        [InlineData("$order 0083 villager: language:en")]
        [InlineData("$order 0083 villager:cat23 villager:cat00")]
        public async Task MissingOrRepeatedVillagerOptionsNeverQueueAnOrder(string input)
        {
            var draft = new OrderDraft();
            var config = new CrossBotConfig { AllowVillagerInjection = true };
            QuickOrderSubmission.ApplyInput(draft, input, config);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", _ => throw new Exception("Invalid villager option must not enter the queue"));
            Assert.False(result.Accepted);
            Assert.Contains("villager", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(input, draft.Input);
        }

        [Fact]
        public async Task QuickUnsafeItemsAreRejectedBeforeQueueSubmission()
        {
            var unsafeItem = GameInfo.Strings.ItemDataSource.Select(entry => (ushort)entry.Value)
                .First(id => InternalItemTool.CurrentInstance.IsInternalItem(id) && id != 0x02F8 && id != 0x02F7 && id != 0x02F6 && id != 0x1E36);
            var config = new CrossBotConfig();
            var draft = new OrderDraft();
            QuickOrderSubmission.ApplyInput(draft, "$order " + unsafeItem.ToString("X4"), config);
            var result = await QuickOrderSubmission.SubmitAsync(draft, config, "tester", _ => throw new Exception("Unsafe items must not be queued"));
            Assert.False(result.Accepted);
            Assert.Contains("unsafe", result.Message);
        }

        [Fact]
        public void QuickAndGuidedSavedListsHaveSeparateTokensAndRevisions()
        {
            var directory = Path.Combine(Path.GetTempPath(), "acnh-quick-test-" + Guid.NewGuid().ToString("N"));
            var quickDirectory = Path.Combine(directory, "Quick");
            try
            {
                var guided = new OrderDraftStore(directory);
                var quick = new OrderDraftStore(quickDirectory);
                var guidedDraft = guided.Get(10, 20);
                guidedDraft.Items = new[] { new Item(131) }; guidedDraft.Input = "lucky cat";
                guided.Changed(10, 20, guidedDraft);
                var quickDraft = quick.Get(10, 20);
                QuickOrderSubmission.ApplyInput(quickDraft, "$ordercat 3107", new CrossBotConfig());
                quick.Changed(10, 20, quickDraft);
                Assert.NotEqual(guidedDraft.Token, quickDraft.Token);
                Assert.False(quick.Matches(quickDraft, guidedDraft.Token, guidedDraft.Revision));
                Assert.Equal("lucky cat", new OrderDraftStore(directory).Get(10, 20).Input);
                Assert.Equal("$ordercat 3107", new OrderDraftStore(quickDirectory).Get(10, 20).Input);
                var revision = quickDraft.Revision;
                quick.Changed(10, 20, quickDraft);
                Assert.False(quick.Matches(quickDraft, quickDraft.Token, revision));
            }
            finally
            {
                if (Directory.Exists(quickDirectory))
                {
                    foreach (var path in Directory.GetFiles(quickDirectory)) File.Delete(path);
                    Directory.Delete(quickDirectory);
                }
                if (Directory.Exists(directory))
                {
                    foreach (var path in Directory.GetFiles(directory)) File.Delete(path);
                    Directory.Delete(directory);
                }
            }
        }

        [Fact]
        public void SharedPanelOffersBothModesAndKeepsQuickAvailableWhenLookupIsDisabled()
        {
            var buttons = OrderPanelService.BuildComponents(false, id => id).Components.OfType<ActionRowComponent>()
                .SelectMany(row => row.Components).OfType<ButtonComponent>().ToArray();
            Assert.Equal(5, buttons.Length);
            Assert.True(buttons.Single(button => button.Label == "Find items").IsDisabled);
            Assert.Equal("shop:order:home:0", buttons.Single(button => button.Label == "Place order (guided)").CustomId);
            var quick = buttons.Single(button => button.Label == "Place order (quick)");
            Assert.False(quick.IsDisabled);
            Assert.Equal("shop:quick:home:0", quick.CustomId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("island")]
        public async Task QuickModalContractBindsThePastedCommandAndRoutesToTheCorrectIsland(string suffix)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<QuickBindingProbeModule>(services);
            var id = "shop-quick:private-token:12" + (suffix == null ? "" : ":" + suffix);
            const string text = "$ordercat 0083 3107";
            var field = InterfaceStub.Create<IComponentInteractionData>(("CustomId", "command"), ("Value", text), ("Type", ComponentType.TextInput));
            var data = InterfaceStub.Create<IModalInteractionData>(("CustomId", id), ("Components", new[] { field }));
            var user = InterfaceStub.Create<IUser>(("Id", 123UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", user));
            var context = new InteractionContext(client, interaction);
            var pattern = InteractionRouting.FindPattern(id, suffix, service.ModalCommands.Select(command => command.Name));
            Assert.NotNull(pattern);
            Assert.Null(InteractionRouting.FindPattern(id + ":other", suffix, service.ModalCommands.Select(command => command.Name)));
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(suffix == null ? new[] { "private-token", "12" } : new[] { "private-token", "12", "island" }, capture.Route);
            Assert.Equal(new[] { text }, capture.Values);
        }
    }
}

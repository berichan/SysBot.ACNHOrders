using System;
using System.IO;
using System.Linq;
using NHSE.Core;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public sealed class GuidedOrderTests
    {
        [Theory]
        [InlineData("$order lucky cat, nook miles ticket villager:cat23", OrderFillMode.Standard)]
        [InlineData("!ordercat lucky cat, nook miles ticket villager:cat23", OrderFillMode.Catalogue)]
        [InlineData("/order_island items:lucky cat, nook miles ticket villager:cat23", OrderFillMode.Standard)]
        public void OldCommandsImportWithoutLosingVillagerOrOrderMode(string input, OrderFillMode mode)
        {
            var parsed = OrderPreparation.Parse(input, new DropBotConfig());
            Assert.Empty(parsed.Errors);
            Assert.Equal(2, parsed.Items.Length);
            Assert.Equal((ushort)131, parsed.Items[0].ItemId);
            Assert.Equal("cat23", parsed.Villager);
            Assert.Equal(mode, parsed.Mode);
        }

        [Fact]
        public void InvalidEntryIsReportedInsteadOfSubmittingOnlyRecognizedItems()
        {
            var parsed = OrderPreparation.Parse("lucky cat, this-item-does-not-exist", new DropBotConfig());
            Assert.Single(parsed.Items);
            Assert.Single(parsed.Errors);
            Assert.Contains("this-item-does-not-exist", parsed.Errors[0]);
        }

        [Theory]
        [InlineData("0083 3107")]
        [InlineData("0x0083\n0x3107")]
        public void HexListsAndNewlinesImport(string input)
        {
            var parsed = OrderPreparation.Parse(input, new DropBotConfig());
            Assert.Empty(parsed.Errors);
            Assert.Equal(2, parsed.Items.Length);
        }

        [Fact]
        public void OrdersOverTheLimitAreReportedWithoutRemovingEntries()
        {
            var parsed = OrderPreparation.Parse(string.Join(" ", Enumerable.Repeat("0083", 41)), new DropBotConfig());
            Assert.Equal(41, parsed.Items.Length);
            Assert.Contains(parsed.Errors, error => error.Contains("limit is 40"));
        }

        [Fact]
        public void ExactModePreservesCustomizationAndLeavesUnrequestedSlotsEmpty()
        {
            var item = new Item(131) { BodyType = 1 };
            Assert.True(OrderPreparation.TryPrepare(new[] { item }, OrderFillMode.Exact, new CrossBotConfig(), "tester", null, out var prepared, out var error), error);
            Assert.Single(prepared!.VisibleItems);
            Assert.Equal(1, prepared.VisibleItems[0].BodyType);
            Assert.Equal(40, prepared.Delivery.ItemArray.Items.Count);
            Assert.All(prepared.Delivery.ItemArray.Items.Skip(1), empty => Assert.True(empty.IsNone));
            Assert.Contains("Black", OrderPreparation.Describe(prepared.VisibleItems[0]));
            Assert.Equal(1, item.BodyType);
        }

        [Theory]
        [InlineData(OrderFillMode.Standard)]
        [InlineData(OrderFillMode.Exact)]
        [InlineData(OrderFillMode.Catalogue)]
        public void EveryOrderModeUsesFullStacksInPreviewAndDeliveryWithoutChangingTheInput(OrderFillMode mode)
        {
            var stack = GameInfo.Strings.ItemDataSource.Select(entry => new Item((ushort)entry.Value))
                .First(item => ItemInfo.TryGetMaxStackCount(item, out var max) && max > 5 && !InternalItemTool.CurrentInstance.IsInternalItem(item.ItemId));
            stack.Count = 4;
            ItemInfo.TryGetMaxStackCount(stack, out var maximum);
            Assert.True(OrderPreparation.TryPrepare(new[] { stack }, mode, new CrossBotConfig(), "tester", null, out var prepared, out var error), error);
            Assert.All(prepared!.VisibleItems, item => Assert.Equal((ushort)(maximum - 1), item.Count));
            Assert.All(prepared.Delivery.ItemArray.Items.Where(item => !item.IsNone), item => Assert.Equal((ushort)(maximum - 1), item.Count));
            Assert.Equal((ushort)4, stack.Count);
        }

        [Fact]
        public void StandardPreviewContainsTheSameVariantsAsDeliveryWithoutMutatingDraft()
        {
            var original = new Item(131);
            Assert.True(OrderPreparation.TryPrepare(new[] { original }, OrderFillMode.Standard, new CrossBotConfig(), "tester", null, out var prepared, out var error), error);
            Assert.Equal(40, prepared!.VisibleItems.Length);
            Assert.Equal(prepared.Delivery.ItemArray.Items.Select(ItemParser.GetItemText), prepared.VisibleItems.Select(ItemParser.GetItemText));
            Assert.Contains(prepared.VisibleItems, item => item.BodyType == 1);
            Assert.Equal((ushort)0, original.Count);
        }

        [Fact]
        public void CataloguePreviewExcludesFillerBeyondTheSeparator()
        {
            Assert.True(OrderPreparation.TryPrepare(new[] { new Item(131) }, OrderFillMode.Catalogue, new CrossBotConfig(), "tester", null, out var prepared, out var error), error);
            Assert.Single(prepared!.VisibleItems);
            Assert.True(prepared.Delivery.ItemArray.Items[1].IsNone);
        }

        [Fact]
        public void UnsafeItemsAndDisabledVillagersCannotBePrepared()
        {
            var unsafeItem = Enumerable.Range(0, ushort.MaxValue).Select(value => (ushort)value)
                .First(id => InternalItemTool.CurrentInstance.IsInternalItem(id) && id != 0x02F8 && id != 0x02F7 && id != 0x02F6 && id != 0x1E36);
            Assert.False(OrderPreparation.TryPrepare(new[] { new Item(unsafeItem) }, OrderFillMode.Exact, new CrossBotConfig(), "tester", null, out _, out var unsafeError));
            Assert.Contains("unsafe", unsafeError);
            Assert.False(OrderPreparation.TryPrepare(new[] { new Item(131) }, OrderFillMode.Exact, new CrossBotConfig { AllowVillagerInjection = false }, "tester", "cat23", out _, out var villagerError));
            Assert.Contains("disabled", villagerError);
            Assert.False(OrderPreparation.TryPrepare(new[] { new Item(0xFFFD) }, OrderFillMode.Exact, new CrossBotConfig(), "tester", null, out _, out var unknownError));
            Assert.Contains("unknown", unknownError);
        }

        [Fact]
        public void SearchRanksExactNamesFirstAndSupportsShortQueries()
        {
            Assert.Equal("lucky cat", ItemSearchService.Search("lucky cat")[0].Text);
            Assert.NotEmpty(ItemSearchService.Search("TV"));
            Assert.Empty(ItemSearchService.Search("x"));
            Assert.Empty(ItemSearchService.Search("lucky", "unsupported"));
        }

        [Fact]
        public void DraftsAreScopedToTheUserAndGuildAndRejectOldRevisions()
        {
            var store = new OrderDraftStore();
            var draft = store.Get(10, 20);
            Assert.NotSame(draft, store.Get(10, 21));
            Assert.NotSame(draft, store.Get(11, 20));
            var oldRevision = draft.Revision;
            store.Changed(10, 20, draft);
            Assert.False(store.Matches(draft, draft.Token, oldRevision));
            Assert.False(store.Matches(draft, store.Get(10, 21).Token, draft.Revision));
            Assert.True(store.Matches(draft, draft.Token, draft.Revision));
        }

        [Fact]
        public void SavedDraftSurvivesRestartWithModeErrorsAndVillagerButRequiresANewPreview()
        {
            var directory = Path.Combine(Path.GetTempPath(), "acnh-draft-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                draft.Items = new[] { new Item(131) { BodyType = 1 } };
                draft.Mode = OrderFillMode.Exact;
                draft.Villager = "cat23";
                draft.Errors = new[] { "unrecognized entry" };
                store.Changed(10, 20, draft);
                var restored = new OrderDraftStore(directory).Get(10, 20);
                Assert.Equal(draft.Token, restored.Token);
                Assert.Equal(draft.Revision, restored.Revision);
                Assert.Equal(1, restored.Items[0].BodyType);
                Assert.Equal(OrderFillMode.Exact, restored.Mode);
                Assert.Equal("cat23", restored.Villager);
                Assert.Equal(draft.Errors, restored.Errors);
                Assert.Null(restored.Preview);
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
        [InlineData("shop:order:home:0", null, true)]
        [InlineData("shop:confirm:token:12-0:island", "island", true)]
        [InlineData("shop:confirm:token:12-0:other", "island", false)]
        [InlineData("shop:order:home:0:island", null, false)]
        [InlineData("shop:order:home:0", "island", false)]
        [InlineData("shop-paste:token:12:island", "island", true)]
        [InlineData("embed-normal-order:island", "island", true)]
        [InlineData("shop:confirm:token:12-0:island:other", "other", false)]
        public void InteractionRoutingSeparatesIslandsAndSupportsDynamicControls(string id, string suffix, bool owns)
        {
            var patterns = new[] { "shop:*:*:*", "shop:*:*:*:*", "shop-paste:*:*", "shop-paste:*:*:*", "embed-normal-order", "embed-normal-order:*" };
            Assert.Equal(owns, InteractionRouting.OwnsCustomId(id, suffix, patterns));
        }

        [Fact]
        public void StatusIsActiveFromPreparationUntilConsoleCleanupAndDoesNotRetainAnOldDodo()
        {
            var store = new OrderStatusStore();
            store.Set(20, 1, OrderStage.Preparing);
            Assert.True(store.IsActive(20));
            store.Set(20, 1, OrderStage.Ready, dodo: "ABCDE");
            Assert.Equal("ABCDE", store.Get(20)!.Dodo);
            store.Set(20, 1, OrderStage.Completed);
            Assert.True(store.IsActive(20));
            Assert.Null(store.Get(20)!.Dodo);
            store.Release(20);
            Assert.False(store.IsActive(20));
            Assert.Equal(OrderStage.Completed, store.Get(20)!.Stage);
        }

        [Fact]
        public void OldOrderEventsCannotOverwriteANewerOrderAndControlTokensDoNotSurviveRestart()
        {
            var store = new OrderStatusStore();
            store.Set(20, 1, OrderStage.Queued);
            var oldToken = store.Get(20)!.ControlToken;
            store.Set(20, 2, OrderStage.Queued);
            store.Set(20, 1, OrderStage.Cancelled);
            Assert.Equal(2UL, store.Get(20)!.OrderId);
            Assert.Equal(OrderStage.Queued, store.Get(20)!.Stage);
            Assert.NotEqual(oldToken, store.Get(20)!.ControlToken);
            var restarted = new OrderStatusStore();
            restarted.Set(20, 1, OrderStage.Queued);
            Assert.NotEqual(oldToken, restarted.Get(20)!.ControlToken);
        }

        [Fact]
        public void DequeuePublishesActiveStatusBeforeRemovingTheOrderAndOldCancelDoesNotRemoveANewOrder()
        {
            var queue = new OrderQueue<TestOrder>();
            queue.Enqueue(new TestOrder(20, 1));
            var statuses = new OrderStatusStore();
            Assert.True(queue.TryDequeue(out var order, current =>
            {
                Assert.Equal(1, queue.GetPosition(20));
                statuses.Set(current.UserGuid, current.OrderID, OrderStage.Preparing);
            }));
            Assert.Equal(-1, queue.GetPosition(20));
            Assert.True(statuses.IsActive(20));
            Assert.False(queue.RemoveByUserId(20, order!.OrderID));
            queue.Enqueue(new TestOrder(20, 2));
            Assert.False(queue.RemoveByUserId(20, 1));
            Assert.True(queue.RemoveByUserId(20, 2));
        }

        private sealed class TestOrder : IACNHOrderNotifier<Item>
        {
            public TestOrder(ulong user, ulong order) { UserGuid = user; OrderID = order; }
            public Item[] Order => Array.Empty<Item>();
            public VillagerRequest VillagerOrder => null;
            public ulong UserGuid { get; }
            public ulong OrderID { get; }
            public string VillagerName => "tester";
            public Action<CrossBot> OnFinish { get; set; }
            public void OrderInitializing(CrossBot routine, string msg) { }
            public void OrderReady(CrossBot routine, string msg, string dodo) { }
            public void OrderCancelled(CrossBot routine, string msg, bool faulted) { }
            public void OrderFinished(CrossBot routine, string msg) { }
            public void SendNotification(CrossBot routine, string msg) { }
        }
    }
}

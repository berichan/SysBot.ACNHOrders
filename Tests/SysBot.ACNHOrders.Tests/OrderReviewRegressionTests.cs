using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NHSE.Core;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public sealed class OrderReviewRegressionTests
    {
        [Fact]
        public void EditListRoundTripPreservesVariantsFabricStackAndRecipe()
        {
            var stack = GameInfo.Strings.ItemDataSource.Select(entry => new Item((ushort)entry.Value))
                .First(item => ItemInfo.TryGetMaxStackCount(item, out var max) && max > 5 && !InternalItemTool.CurrentInstance.IsInternalItem(item.ItemId));
            stack.Count = 4;
            var items = new[] { new Item(131) { BodyType = 1, PatternChoice = 1 }, stack, new Item(Item.DIYRecipe) { Count = 123 } };
            var parsed = OrderPreparation.Parse(OrderPreparation.FormatInput(items), new DropBotConfig());
            Assert.Empty(parsed.Errors);
            var dropped = OrderPreparation.Clone(items);
            foreach (var item in dropped) item.IsDropped = true;
            Assert.Equal(dropped.Select(item => item.RawValue), parsed.Items.Select(item => item.RawValue));
        }

        [Fact]
        public void OldSavedDisplayTextIsReplacedByEditableCodesButInvalidUserInputIsKept()
        {
            var item = new Item(131) { BodyType = 1 };
            var draft = new OrderDraft { Items = new[] { item }, Input = ItemParser.GetItemText(item) };
            Assert.Empty(OrderPreparation.Parse(draft.EditableInput, new DropBotConfig()).Errors);
            draft.Input = "lucky cat, this-item-does-not-exist";
            draft.Errors = new[] { "Unrecognized item" };
            Assert.Equal(draft.Input, draft.EditableInput);
        }

        [Fact]
        public void LookupBeforeSelectingItemsCanSaveAndRestoreAnEmptyDraft()
        {
            WithDirectory(directory =>
            {
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                draft.Search = "lucky cat";
                store.Changed(10, 20, draft);
                // Selecting a result also saves while the order's item list is still empty.
                draft.PendingItem = new Item(131) { BodyType = 1 };
                store.Changed(10, 20, draft);
                Assert.True(OrderDraftStore.TryReadSaved(Path.Combine(directory, "draft-10-20.json"), out var saved));
                Assert.Empty(saved.Items);
                var restored = new OrderDraftStore(directory).Get(10, 20);
                Assert.Empty(restored.Items);
                Assert.Equal(draft.Token, restored.Token);
                Assert.Equal(draft.Revision, restored.Revision);
            });
        }

        [Fact]
        public void ClearingAPreviouslySavedListReplacesItsItemsWithAnEmptyArray()
        {
            WithDirectory(directory =>
            {
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                draft.Items = new[] { new Item(131) { BodyType = 1 } };
                store.Changed(10, 20, draft);
                Assert.Equal(1, new OrderDraftStore(directory).Get(10, 20).Items[0].BodyType);
                draft.Items = Array.Empty<Item>(); draft.Input = ""; draft.Errors = Array.Empty<string>(); draft.Villager = null;
                store.Changed(10, 20, draft);
                var restored = new OrderDraftStore(directory).Get(10, 20);
                Assert.Empty(restored.Items);
                Assert.Empty(restored.Input);
                Assert.Empty(restored.Errors);
                Assert.Equal(draft.Revision, restored.Revision);
            });
        }

        [Fact]
        public void UnrecognizedPastedListCanSaveItsErrorsWhenNoItemsWereFound()
        {
            WithDirectory(directory =>
            {
                const string input = "this-item-does-not-exist";
                var parsed = OrderPreparation.Parse(input, new DropBotConfig());
                Assert.Empty(parsed.Items);
                Assert.NotEmpty(parsed.Errors);
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                draft.Items = parsed.Items; draft.Input = input; draft.Errors = parsed.Errors;
                store.Changed(10, 20, draft);
                var restored = new OrderDraftStore(directory).Get(10, 20);
                Assert.Equal(input, restored.EditableInput);
                Assert.Equal(parsed.Errors, restored.Errors);
            });
        }

        [Fact]
        public void AcceptedOrderHistorySurvivesClearingTheDraftAndSupportsEmptySerialization()
        {
            WithDirectory(directory =>
            {
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                draft.Items = new[] { new Item(131) { BodyType = 1 } };
                draft.Mode = OrderFillMode.Exact;
                draft.Input = OrderPreparation.FormatInput(draft.Items);
                store.Changed(10, 20, draft);
                OrderHistory.Save(10, 20, draft, directory);
                draft.Items = Array.Empty<Item>(); draft.Input = ""; draft.Errors = Array.Empty<string>(); draft.Villager = null;
                store.Changed(10, 20, draft);
                Assert.Empty(new OrderDraftStore(directory).Get(10, 20).Items);
                Assert.True(OrderHistory.TryLoad(10, 20, out var saved, out _, out _, out _, directory, directory));
                Assert.Equal(1, Item.GetArray(saved.Items)[0].BodyType);
                OrderHistory.Save(10, 21, draft, directory);
                Assert.True(OrderHistory.TryLoad(10, 21, out var empty, out _, out _, out _, directory, directory));
                Assert.Empty(empty.Items);
            });
        }

        [Fact]
        public void OlderPartialStacksRestoreAsFullStacksWithoutChangingVariantsOrRecipeIds()
        {
            WithDirectory(directory =>
            {
                var stack = GameInfo.Strings.ItemDataSource.Select(entry => new Item((ushort)entry.Value))
                    .First(item => ItemInfo.TryGetMaxStackCount(item, out var max) && max > 5 && !InternalItemTool.CurrentInstance.IsInternalItem(item.ItemId));
                stack.Count = 4;
                ItemInfo.TryGetMaxStackCount(stack, out var maximum);
                var variant = new Item(131) { BodyType = 1, PatternChoice = 1 };
                var recipe = new Item(Item.DIYRecipe) { Count = 123 };
                var store = new OrderDraftStore(directory);
                var draft = store.Get(10, 20);
                draft.Items = new[] { stack, variant, recipe };
                store.Changed(10, 20, draft);
                var restored = new OrderDraftStore(directory).Get(10, 20);
                Assert.Equal((ushort)(maximum - 1), restored.Items[0].Count);
                Assert.Equal(variant.RawValue, restored.Items[1].RawValue);
                Assert.Equal(recipe.RawValue, restored.Items[2].RawValue);
                Assert.Equal((ushort)4, stack.Count);
            });
        }

        [Theory]
        [InlineData(101)]
        [InlineData(3000)]
        public void OversizePastedVillagerIsReportedWithoutPersistingAnUnrenderableValue(int length)
        {
            var parsed = OrderPreparation.Parse("lucky cat villager:" + new string('x', length), new DropBotConfig());
            Assert.Single(parsed.Items);
            Assert.Null(parsed.Villager);
            Assert.Contains(OrderPreparation.VillagerLengthError, parsed.Errors);
            Assert.False(OrderPreparation.TryPrepare(parsed.Items, OrderFillMode.Exact, new CrossBotConfig(), "tester",
                new string('x', length), out _, out var error));
            Assert.Equal(OrderPreparation.VillagerLengthError, error);
            var saved = ValidSaved() with { Villager = parsed.Villager, Errors = parsed.Errors };
            Assert.True(OrderDraftStore.IsValid(saved));
        }

        [Fact]
        public void VillagerLengthLimitAllowsTheFullOptionsFieldAndTrimsWhitespace()
        {
            Assert.True(OrderPreparation.TryValidateVillager(" " + new string('x', 100) + " ", out var villager, out _));
            Assert.Equal(100, villager.Length);
            Assert.True(OrderPreparation.TryValidateVillager("   ", out villager, out _));
            Assert.Null(villager);
        }

        [Fact]
        public async Task BusyDraftAcknowledgesBeforeWaitingAndThenSerializesTheAction()
        {
            using var gate = new SemaphoreSlim(0, 1);
            int acknowledgements = 0;
            var pending = OrderInteractionGate.EnterAsync(gate, false, () =>
            { acknowledgements++; return Task.CompletedTask; }, () => throw new Exception("Must not respond busy"));
            Assert.Equal(1, acknowledgements);
            Assert.False(pending.IsCompleted);
            gate.Release();
            Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(0, gate.CurrentCount);
            gate.Release();
        }

        [Fact]
        public async Task BusyModalGetsAnImmediateResponseInsteadOfWaitingOrDeferring()
        {
            using var gate = new SemaphoreSlim(0, 1);
            int busyResponses = 0;
            var pending = OrderInteractionGate.EnterAsync(gate, true, () => throw new Exception("A modal cannot be deferred"),
                () => { busyResponses++; return Task.CompletedTask; });
            Assert.True(pending.IsCompleted);
            Assert.False(await pending);
            Assert.Equal(1, busyResponses);
            Assert.Equal(0, gate.CurrentCount);
            gate.Release();
            Assert.True(await OrderInteractionGate.EnterAsync(gate, true, () => throw new Exception("A modal cannot be deferred"),
                () => throw new Exception("An available modal must open")));
            gate.Release();
        }

        [Fact]
        public void PresetSelectionKeepsItsIdentityWhenTheHostChangesTheFileList()
        {
            WithDirectory(directory =>
            {
                var a = Path.Combine(directory, "A.nhi");
                var b = Path.Combine(directory, "B.nhi");
                File.WriteAllText(a, "A"); File.WriteAllText(b, "B");
                var selected = OrderPresetSelection.Id(b);
                File.WriteAllText(Path.Combine(directory, "AA.nhi"), "AA");
                var paths = Directory.GetFiles(directory, "*.nhi").OrderBy(Path.GetFileName).ToArray();
                Assert.Equal(b, OrderPresetSelection.Resolve(paths, selected));
                File.Delete(a);
                Assert.Equal(b, OrderPresetSelection.Resolve(Directory.GetFiles(directory, "*.nhi"), selected));
                File.Delete(b);
                Assert.Null(OrderPresetSelection.Resolve(Directory.GetFiles(directory, "*.nhi"), selected));
            });
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("null")]
        [InlineData("{invalid json")]
        public void MalformedDraftAndHistoryCanBeOpenedWithoutThrowing(string json)
        {
            WithDirectory(directory =>
            {
                File.WriteAllText(Path.Combine(directory, "draft-10-20.json"), json);
                File.WriteAllText(Path.Combine(directory, "last-10-20.json"), json);
                Assert.Empty(new OrderDraftStore(directory).Get(10, 20).Items);
                Assert.False(OrderHistory.TryLoad(10, 20, out var saved, out var items, out var text, out _, directory, directory));
                Assert.Null(saved); Assert.Null(items); Assert.Null(text);
            });
        }

        [Fact]
        public void InvalidSavedFieldsAreRejectedAndAValidHistoryStillLoads()
        {
            var valid = ValidSaved();
            var invalid = new[]
            {
                valid with { Items = null }, valid with { Items = new byte[1] }, valid with { Input = null },
                valid with { Errors = null }, valid with { Errors = new string[] { null } },
                valid with { Token = null }, valid with { Token = "bad" }, valid with { Revision = -1 },
                valid with { Language = null }, valid with { Language = "unsupported" },
                valid with { Mode = (OrderFillMode)99 }, valid with { Villager = new string('x', 3000) }
            };
            WithDirectory(directory =>
            {
                var path = Path.Combine(directory, "last-10-20.json");
                foreach (var candidate in invalid)
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(candidate));
                    Assert.False(OrderHistory.TryLoad(10, 20, out var saved, out _, out _, out _, directory, directory));
                    Assert.Null(saved);
                }
                File.WriteAllText(path, JsonSerializer.Serialize(valid));
                Assert.True(OrderHistory.TryLoad(10, 20, out var restored, out _, out _, out _, directory, directory));
                Assert.Equal(valid.Items, restored.Items);
                Assert.Equal(valid.Mode, restored.Mode);
            });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(39)]
        [InlineData(40)]
        public void CatalogueInstructionsMatchTheActualSeparatorAtCapacity(int count)
        {
            Assert.True(OrderPreparation.TryPrepare(Enumerable.Range(0, count).Select(_ => new Item(131)),
                OrderFillMode.Catalogue, new CrossBotConfig(), "tester", null, out var prepared, out var error), error);
            Assert.Equal(count, prepared.VisibleItems.Length);
            var text = OrderPreparation.FillInstructions(prepared);
            if (count == 40)
            {
                Assert.All(prepared.Delivery.ItemArray.Items, item => Assert.False(item.IsNone));
                Assert.Contains("Collect all items", text);
                Assert.Contains("no separator", text);
            }
            else
            {
                Assert.True(prepared.Delivery.ItemArray.Items[count].IsNone);
                Assert.Contains("before the separator", text);
            }
        }

        private static SavedOrderDraft ValidSaved() => new(Guid.NewGuid().ToString("N"), 1,
            new ItemArrayEditor<Item>(new[] { new Item(131) }).Write(), "lucky cat", Array.Empty<string>(), "en", "cat23", OrderFillMode.Exact);

        private static void WithDirectory(Action<string> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "acnh-review-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { action(directory); }
            finally
            {
                foreach (var path in Directory.GetFiles(directory)) File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }
}

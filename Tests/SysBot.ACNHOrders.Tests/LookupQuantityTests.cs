using System;
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
    public class QuantityBindingProbeModule : InteractionModuleBase<IInteractionContext>
    {
        public BindingCapture Capture { get; set; }
        [ModalInteraction("shop-quantity:*:*")]
        public Task Quantity(string token, int revision, OrderQuantityModal modal)
        { Capture.Route = new[] { token, revision.ToString() }; Capture.Values = new[] { modal.Quantity }; return Task.CompletedTask; }
        [ModalInteraction("shop-quantity:*:*:*")]
        public Task SuffixedQuantity(string token, int revision, string suffix, OrderQuantityModal modal)
        { Capture.Route = new[] { token, revision.ToString(), suffix }; Capture.Values = new[] { modal.Quantity }; return Task.CompletedTask; }
    }

    public sealed class LookupQuantityTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(15)]
        [InlineData(40)]
        public void LookupAddsTheRequestedCopiesAndKeepsTheirVariantInExactDelivery(int quantity)
        {
            var item = new Item(131) { BodyType = 1, PatternChoice = 1 };
            var draft = new OrderDraft { PendingItem = item, Mode = OrderFillMode.Standard };
            Assert.True(LookupOrderSelection.TrySetQuantity(draft, quantity.ToString(), out _));
            Assert.True(LookupOrderSelection.TryAdd(draft, out var added, out _));
            Assert.Equal(quantity, added);
            Assert.Equal(quantity, draft.Items.Length);
            Assert.All(draft.Items, copy => Assert.Equal(item.RawValue, copy.RawValue));
            Assert.NotSame(draft.Items[0], item);
            if (quantity > 1) Assert.NotSame(draft.Items[0], draft.Items[1]);
            Assert.Null(draft.PendingItem);
            Assert.Equal(1, draft.PendingQuantity);
            Assert.Equal(OrderFillMode.Exact, draft.Mode);
            Assert.Equal(OrderPreparation.FormatInput(draft.Items), draft.Input);
            Assert.True(OrderPreparation.TryPrepare(draft.Items, draft.Mode, new CrossBotConfig(), "tester", null, out var prepared, out var error), error);
            Assert.Equal(quantity, prepared.VisibleItems.Length);
            Assert.Equal(quantity, prepared.Delivery.ItemArray.Items.Count(copy => !copy.IsNone));
        }

        [Fact]
        public void QuantityAddsFullStacksWithoutChangingTheSelectedItemOrOtherItems()
        {
            var stack = GameInfo.Strings.ItemDataSource.Select(entry => new Item((ushort)entry.Value))
                .First(item => ItemInfo.TryGetMaxStackCount(item, out var max) && max > 5 && !InternalItemTool.CurrentInstance.IsInternalItem(item.ItemId));
            stack.Count = 4;
            ItemInfo.TryGetMaxStackCount(stack, out var maximum);
            var recipe = new Item(Item.DIYRecipe) { Count = 123 };
            var draft = new OrderDraft { PendingItem = stack, Items = new[] { recipe } };
            Assert.True(LookupOrderSelection.TrySetQuantity(draft, "3", out _));
            Assert.True(LookupOrderSelection.TryAdd(draft, out var added, out _));
            Assert.Equal(3, added);
            Assert.Equal(recipe.RawValue, draft.Items[0].RawValue);
            Assert.All(draft.Items.Skip(1), copy => Assert.Equal((ushort)(maximum - 1), copy.Count));
            Assert.Equal((ushort)4, stack.Count);
            Assert.True(OrderPreparation.TryPrepare(draft.Items, draft.Mode, new CrossBotConfig(), "tester", null, out var prepared, out var error), error);
            Assert.Equal(4, prepared.VisibleItems.Length);
            Assert.All(prepared.VisibleItems.Skip(1), copy => Assert.Equal((ushort)(maximum - 1), copy.Count));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("2.5")]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("41")]
        [InlineData("999999999999999999999999")]
        public void InvalidQuantityLeavesTheSelectionAndOrderUnchanged(string text)
        {
            var draft = new OrderDraft { PendingItem = new Item(131), PendingQuantity = 2, Items = new[] { new Item(3107) } };
            var items = draft.Items;
            Assert.False(LookupOrderSelection.TrySetQuantity(draft, text, out var error));
            Assert.NotEmpty(error);
            Assert.Same(items, draft.Items);
            Assert.Equal(2, draft.PendingQuantity);
            Assert.Equal(OrderFillMode.Standard, draft.Mode);
        }

        [Fact]
        public void QuantityCannotExceedRemainingSlotsOrPartiallyAddCopies()
        {
            var draft = new OrderDraft { Items = Enumerable.Range(0, 38).Select(_ => new Item(131)).ToArray(), PendingItem = new Item(3107) };
            Assert.False(LookupOrderSelection.TrySetQuantity(draft, "3", out _));
            Assert.True(LookupOrderSelection.TrySetQuantity(draft, "2", out _));
            // Capacity can change before Add is used, so recheck it at insertion too.
            draft.Items = draft.Items.Append(new Item(131)).ToArray();
            var items = draft.Items;
            Assert.False(LookupOrderSelection.TryAdd(draft, out var added, out _));
            Assert.Equal(0, added);
            Assert.Same(items, draft.Items);
            Assert.NotNull(draft.PendingItem);
            Assert.True(LookupOrderSelection.TrySetQuantity(draft, "1", out _));
            Assert.True(LookupOrderSelection.TryAdd(draft, out _, out _));
            Assert.Equal(40, draft.Items.Length);
        }

        [Fact]
        public void MissingSelectionFullOrdersAndUnresolvedErrorsCannotAddItems()
        {
            var draft = new OrderDraft();
            Assert.False(LookupOrderSelection.TrySetQuantity(draft, "1", out _));
            Assert.False(LookupOrderSelection.TryAdd(draft, out _, out _));
            draft.PendingItem = new Item(131);
            draft.Errors = new[] { "Unknown item" };
            Assert.False(LookupOrderSelection.TryAdd(draft, out _, out _));
            Assert.Empty(draft.Items);
            draft.Errors = Array.Empty<string>();
            draft.Items = Enumerable.Range(0, 40).Select(_ => new Item(131)).ToArray();
            Assert.False(LookupOrderSelection.TrySetQuantity(draft, "1", out _));
            Assert.False(LookupOrderSelection.TryAdd(draft, out _, out _));
            Assert.Equal(40, draft.Items.Length);
        }

        [Fact]
        public void QuantityModalShowsAvailableSlotsAndRetainsTheCurrentSelection()
        {
            var draft = new OrderDraft { Items = new[] { new Item(131) }, PendingQuantity = 12 };
            var modal = LookupOrderSelection.BuildQuantityModal("shop-quantity:token:2:island", draft);
            var input = Assert.IsType<TextInputComponent>(Assert.Single(Assert.Single(modal.Component.Components).Components));
            Assert.Equal("shop-quantity:token:2:island", modal.CustomId);
            Assert.Equal("Number of slots (1 to 39)", input.Label);
            Assert.Equal("quantity", input.CustomId);
            Assert.Equal("12", input.Value);
            Assert.True(input.Required);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("island")]
        public async Task QuantityModalBindsTheCountAndIslandSuffix(string suffix)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<QuantityBindingProbeModule>(services);
            var id = "shop-quantity:private-token:12" + (suffix == null ? "" : ":" + suffix);
            var field = InterfaceStub.Create<IComponentInteractionData>(("CustomId", "quantity"), ("Value", "40"), ("Type", ComponentType.TextInput));
            var data = InterfaceStub.Create<IModalInteractionData>(("CustomId", id), ("Components", new[] { field }));
            var user = InterfaceStub.Create<IUser>(("Id", 123UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", user));
            var context = new InteractionContext(client, interaction);
            var pattern = InteractionRouting.FindPattern(id, suffix, service.ModalCommands.Select(command => command.Name));
            Assert.NotNull(pattern);
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(new[] { "40" }, capture.Values);
            Assert.Equal(suffix == null ? new[] { "private-token", "12" } : new[] { "private-token", "12", suffix }, capture.Route);
        }
    }
}

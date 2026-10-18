using System;
using System.Linq;
using Discord;
using NHSE.Core;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public sealed class OrderStartOptionsTests
    {
        [Theory]
        [InlineData("ost10", "jp")]
        [InlineData("ジャック", "jp")]
        [InlineData("杰克", "chs")]
        [InlineData("傑克", "cht")]
        public void ExplicitOptionsApplyWithoutAnItemListAndKeepExistingCards(string villager, string language)
        {
            var card = RecipeSearchService.Create(15);
            var draft = new OrderDraft { Items = new[] { card }, Mode = OrderFillMode.Exact };
            var original = draft.Items;
            Assert.True(OrderStartOptions.TryApply(draft, villager, language, out var error), error);
            Assert.Same(original, draft.Items);
            Assert.Equal(card.RawValue, draft.Items[0].RawValue);
            Assert.Equal(language, draft.Language);
            Assert.Equal(villager == "ost10" ? "ost10" : "cat23", draft.Villager);
            Assert.Equal(OrderFillMode.Exact, draft.Mode);
        }

        [Fact]
        public void VillagerOnlySlashOptionsCanPrepareAnOrderWithoutItems()
        {
            var draft = new OrderDraft();
            Assert.True(OrderStartOptions.TryApply(draft, "ost10", "jp", out _));
            Assert.True(OrderPreparation.TryPrepare(draft.Items, draft.Mode, new CrossBotConfig { AllowVillagerInjection = true },
                "tester", draft.Villager, out var prepared, out var error, draft.Language), error);
            Assert.NotNull(prepared.Villager);
            Assert.Empty(prepared.VisibleItems);
        }

        [Fact]
        public void LanguageOnlyOptionsPreserveTheSelectedVillagerAcrossLanguages()
        {
            var draft = new OrderDraft { Language = "jp", Villager = "ジャック" };
            Assert.True(OrderStartOptions.TryApply(draft, null, " FR ", out _));
            Assert.Equal("fr", draft.Language);
            Assert.Equal("cat23", draft.Villager);
            Assert.True(OrderStartOptions.TryApply(draft, null, " ", out _));
            Assert.Equal("fr", draft.Language);
            Assert.True(OrderStartOptions.TryApply(draft, "", null, out _));
            Assert.Null(draft.Villager);
        }

        [Fact]
        public void InvalidOptionsLeaveTheListAndSelectionsUnchanged()
        {
            var draft = new OrderDraft { Items = new[] { new Item(131) }, Villager = "ost10", Language = "jp" };
            var items = draft.Items;
            Assert.False(OrderStartOptions.TryApply(draft, "cat23", "unsupported", out var error));
            Assert.NotEmpty(error);
            Assert.False(OrderStartOptions.TryApply(draft, new string('x', 101), "fr", out error));
            Assert.NotEmpty(error);
            Assert.Equal("ost10", draft.Villager);
            Assert.Equal("jp", draft.Language);
            Assert.Same(items, draft.Items);
        }

        [Theory]
        [InlineData("")]
        [InlineData("$order 0083")]
        public void PasteModalAcceptsEmptyDraftsAndKeepsExistingInput(string input)
        {
            var modal = OrderExperience.BuildPasteModal("shop-paste:token:12:island", input);
            var field = Assert.IsType<TextInputComponent>(Assert.Single(Assert.Single(modal.Component.Components).Children()));
            Assert.Equal(input.Length == 0 ? null : input, field.Value);
            Assert.Equal(1, field.MinLength);
            Assert.Equal(4000, field.MaxLength);
        }
    }
}

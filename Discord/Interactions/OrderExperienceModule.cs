using System.Threading.Tasks;
using Discord.Interactions;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public sealed class OrderSearchModal : IModal
    {
        public string Title => "Find items";
        [ModalTextInput("query")] public string Query { get; set; } = string.Empty;
    }
    public sealed class OrderPasteModal : IModal
    {
        public string Title => "Paste or edit your order";
        [ModalTextInput("items", Discord.TextInputStyle.Paragraph)] public string Items { get; set; } = string.Empty;
    }
    public sealed class OrderVillagerSearchModal : IModal
    {
        public string Title => "Find villager";
        [ModalTextInput("query")][RequiredInput(false)] public string Query { get; set; } = string.Empty;
    }
    public sealed class OrderQuickModal : IModal
    {
        public string Title => "Place order (quick)";
        [ModalTextInput("command", Discord.TextInputStyle.Paragraph)] public string Command { get; set; } = string.Empty;
    }
    public sealed class OrderOptionsModal : IModal
    {
        public string Title => "Order options";
        [ModalTextInput("language")] public string Language { get; set; } = "en";
        [ModalTextInput("villager")][RequiredInput(false)] public string? Villager { get; set; }
    }
    public sealed class OrderAmountModal : IModal
    {
        public string Title => "Choose stack quantity";
        [ModalTextInput("amount")] public string Amount { get; set; } = "1";
    }
    public sealed class OrderQuantityModal : IModal
    {
        public string Title => "Choose quantity";
        [ModalTextInput("quantity")] public string Quantity { get; set; } = "1";
    }

    [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
    public class OrderExperienceModule : InteractionModuleBase<SocketInteractionContext>
    {
        [ComponentInteraction("shop:*:*:*")]
        public Task Action(string action, string token, string value) =>
            new OrderExperience(Context).Handle(action, token, value, (Context.Interaction as SocketMessageComponent)?.Data.Values);
        [ComponentInteraction("shop:*:*:*:*")]
        public Task SuffixedAction(string action, string token, string value, string _) => Action(action, token, value);

        [ComponentInteraction("shop-select:*:*:*")]
        public Task Selection(string action, string token, string value, string[] selections) =>
            new OrderExperience(Context).Handle(action, token, value, selections);
        [ComponentInteraction("shop-select:*:*:*:*")]
        public Task SuffixedSelection(string action, string token, string value, string _, string[] selections) => Selection(action, token, value, selections);

        [ModalInteraction("shop-search:*:*")]
        public Task Search(string token, int revision, OrderSearchModal modal) => new OrderExperience(Context).Modal("search", token, revision, modal.Query);
        [ModalInteraction("shop-search:*:*:*")]
        public Task SuffixedSearch(string token, int revision, string _, OrderSearchModal modal) => Search(token, revision, modal);
        [ModalInteraction("shop-villager:*:*")]
        public Task VillagerSearch(string token, int revision, OrderVillagerSearchModal modal) => new OrderExperience(Context).Modal("villager-search", token, revision, modal.Query ?? string.Empty);
        [ModalInteraction("shop-villager:*:*:*")]
        public Task SuffixedVillagerSearch(string token, int revision, string _, OrderVillagerSearchModal modal) => VillagerSearch(token, revision, modal);
        [ModalInteraction("shop-paste:*:*")]
        public Task Paste(string token, int revision, OrderPasteModal modal) => new OrderExperience(Context).Modal("paste", token, revision, modal.Items);
        [ModalInteraction("shop-paste:*:*:*")]
        public Task SuffixedPaste(string token, int revision, string _, OrderPasteModal modal) => Paste(token, revision, modal);
        [ModalInteraction("shop-quick:*:*")]
        public Task Quick(string token, int revision, OrderQuickModal modal) => new OrderExperience(Context).QuickModal(token, revision, modal.Command);
        [ModalInteraction("shop-quick:*:*:*")]
        public Task SuffixedQuick(string token, int revision, string _, OrderQuickModal modal) => Quick(token, revision, modal);
        [ModalInteraction("shop-options:*:*")]
        public Task Options(string token, int revision, OrderOptionsModal modal) => new OrderExperience(Context).Modal("options", token, revision, modal.Language, modal.Villager);
        [ModalInteraction("shop-options:*:*:*")]
        public Task SuffixedOptions(string token, int revision, string _, OrderOptionsModal modal) => Options(token, revision, modal);
        [ModalInteraction("shop-amount:*:*")]
        public Task Amount(string token, int revision, OrderAmountModal modal) => new OrderExperience(Context).Modal("amount", token, revision, modal.Amount);
        [ModalInteraction("shop-amount:*:*:*")]
        public Task SuffixedAmount(string token, int revision, string _, OrderAmountModal modal) => Amount(token, revision, modal);
        [ModalInteraction("shop-quantity:*:*")]
        public Task Quantity(string token, int revision, OrderQuantityModal modal) => new OrderExperience(Context).Modal("quantity", token, revision, modal.Quantity);
        [ModalInteraction("shop-quantity:*:*:*")]
        public Task SuffixedQuantity(string token, int revision, string _, OrderQuantityModal modal) => Quantity(token, revision, modal);
    }
}

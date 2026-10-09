using System.Threading.Tasks;
using Discord;
using Discord.Interactions;

namespace SysBot.ACNHOrders
{
    // Retain old modal contracts and component IDs so existing panels can be used during migration.
    public sealed class EmbedOrderModal : IModal
    {
        public string Title => "Place an Order";
        [ModalTextInput("modal-items", TextInputStyle.Paragraph)] public string Items { get; set; } = string.Empty;
        [ModalTextInput("modal-villager")][RequiredInput(false)] public string? Villager { get; set; }
        [ModalTextInput("modal-language")][RequiredInput(false)] public string? Language { get; set; }
    }
    public sealed class EmbedCatalogueModal : IModal
    {
        public string Title => "Catalogue Order";
        [ModalTextInput("modal-items", TextInputStyle.Paragraph)] public string Items { get; set; } = string.Empty;
        [ModalTextInput("modal-villager")][RequiredInput(false)] public string? Villager { get; set; }
    }

    public class EmbedInteractionModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("setup-embed", "Creates or updates the order panel in a channel.")]
        [RequireSudoInteraction]
        public async Task SetupEmbedAsync(ITextChannel? channel = null)
        {
            await DeferAsync(ephemeral: true).ConfigureAwait(false);
            var target = channel ?? Context.Channel as ITextChannel;
            if (target == null)
            {
                await Context.Interaction.ModifyOriginalResponseAsync(properties => properties.Content = "Choose a server text channel for the order panel.");
                return;
            }
            var message = await OrderPanelService.SetupAsync(target).ConfigureAwait(false);
            await Context.Interaction.ModifyOriginalResponseAsync(properties => properties.Content =
                $"Order panel ready in {target.Mention}. Pin it so members can find it. Rerunning this command updates the saved panel. https://discord.com/channels/{target.GuildId}/{target.Id}/{message.Id}");
        }

        [ComponentInteraction("embed-normal-order")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task OpenNormalOrderModal() => new OrderExperience(Context).StartOrder();
        [ComponentInteraction("embed-normal-order:*")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task OpenSuffixedNormalOrderModal(string _) => OpenNormalOrderModal();

        [ComponentInteraction("embed-catalogue-order")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task OpenCatalogueOrderModal() => new OrderExperience(Context).StartOrder(mode: OrderFillMode.Catalogue);
        [ComponentInteraction("embed-catalogue-order:*")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task OpenSuffixedCatalogueOrderModal(string _) => OpenCatalogueOrderModal();

        [ComponentInteraction("embed-file-order")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task RequestFileUpload() => new OrderExperience(Context).Handle("file", "home", "0");
        [ComponentInteraction("embed-file-order:*")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task RequestSuffixedFileUpload(string _) => RequestFileUpload();

        [ComponentInteraction("embed-queue-position")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task ViewQueuePositionFromEmbed() => new OrderExperience(Context).MyOrder();
        [ComponentInteraction("embed-queue-position:*")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task ViewSuffixedQueuePositionFromEmbed(string _) => ViewQueuePositionFromEmbed();

        [ModalInteraction("embed-order-modal")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task HandleEmbedOrderModal(EmbedOrderModal modal) => new OrderExperience(Context).StartOrder(modal.Items, modal.Villager, modal.Language);
        [ModalInteraction("embed-order-modal:*")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task HandleSuffixedEmbedOrderModal(string _, EmbedOrderModal modal) => HandleEmbedOrderModal(modal);
        [ModalInteraction("embed-catalogue-modal")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task HandleEmbedCatalogueModal(EmbedCatalogueModal modal) => new OrderExperience(Context).StartOrder(modal.Items, modal.Villager, mode: OrderFillMode.Catalogue);
        [ModalInteraction("embed-catalogue-modal:*")]
        [RequireQueueRoleInteraction(nameof(Globals.Bot.Config.RoleUseBot), allowWhilePaused: true)]
        public Task HandleSuffixedEmbedCatalogueModal(string _, EmbedCatalogueModal modal) => HandleEmbedCatalogueModal(modal);
    }
}

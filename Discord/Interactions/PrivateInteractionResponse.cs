using System.Threading.Tasks;
using Discord;

namespace SysBot.ACNHOrders
{
    internal static class PrivateInteractionResponse
    {
        internal static Task DeferAsync(IDiscordInteraction interaction)
        {
            if (interaction.HasResponded) return Task.CompletedTask;
            // An update response cannot make a public message private.
            return interaction switch
            {
                IComponentInteraction component => IsPrivate(component.Message)
                    ? component.DeferAsync() : component.DeferLoadingAsync(ephemeral: true),
                IModalInteraction modal => IsPrivate(modal.Message)
                    ? modal.DeferAsync() : modal.DeferLoadingAsync(ephemeral: true),
                _ => interaction.DeferAsync(ephemeral: true)
            };
        }

        private static bool IsPrivate(IUserMessage? message) => message?.Flags is MessageFlags flags && (flags & MessageFlags.Ephemeral) != 0;
    }
}

using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;

namespace SysBot.ACNHOrders
{
    public static class ControlPanelAccess
    {
        public static bool CanUse(CrossBotConfig config, ulong user, ulong owner) => config.CanUseSudo(user) || user == owner;

        public static bool CanBypassRestrictions(CrossBotConfig config, ulong user, ulong owner, bool controlPanel) =>
            controlPanel && CanUse(config, user, owner);
    }

    // Panel access stays Sudo-only even when IgnoreAllPermissions is enabled.
    public sealed class RequireControlPanelSudoAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo command, IServiceProvider services) =>
            Task.FromResult(ControlPanelAccess.CanUse(Globals.Bot.Config, context.User.Id, Globals.Self.Owner)
                ? PreconditionResult.FromSuccess()
                : PreconditionResult.FromError("Only Sudo users and the application owner can use the control panel."));
    }
}

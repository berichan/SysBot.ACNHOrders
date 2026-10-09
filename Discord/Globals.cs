using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public static class Globals
    {
        public static SysCord Self { get; set; } = default!;
        public static CrossBot Bot { get; set; } = default!;
        public static QueueHub Hub { get; set; } = default!;
        public static ConsoleBotController ConsoleControl { get; set; } = default!;
    }

    public sealed class RequireQueueRoleAttribute : PreconditionAttribute
    {
        // Create a field to store the specified name
        private readonly string _name;

        // Create a constructor so the name can be specified
        public RequireQueueRoleAttribute(string name) => _name = name;

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var mgr = Globals.Bot.Config;
            if (mgr.CanUseSudo(context.User.Id) || Globals.Self.Owner == context.User.Id || mgr.IgnoreAllPermissions)
                return Task.FromResult(PreconditionResult.FromSuccess());

            // Check if this user is a Guild User, which is the only context where roles exist
            if (context.User is not SocketGuildUser gUser)
                return Task.FromResult(PreconditionResult.FromError("You must be in a guild to run this command."));

            if (!mgr.AcceptingCommands)
                return Task.FromResult(PreconditionResult.FromError("Sorry, I am not currently accepting commands!"));

            bool hasRole = mgr.GetHasRole(_name, gUser.Roles.Select(z => z.Name));
            if (!hasRole)
                return Task.FromResult(PreconditionResult.FromError("You do not have the required role to run this command."));

            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }

    public sealed class RequireQueueRoleInteractionAttribute : Discord.Interactions.PreconditionAttribute
    {
        private readonly string _name;

        private readonly bool _allowWhilePaused;
        public RequireQueueRoleInteractionAttribute(string name, bool allowWhilePaused = false)
        { _name = name; _allowWhilePaused = allowWhilePaused; }

        public override Task<Discord.Interactions.PreconditionResult> CheckRequirementsAsync(Discord.IInteractionContext context, Discord.Interactions.ICommandInfo command, IServiceProvider services)
        {
            var mgr = Globals.Bot.Config;
            if (mgr.CanUseSudo(context.User.Id) || Globals.Self.Owner == context.User.Id || mgr.IgnoreAllPermissions)
                return Task.FromResult(Discord.Interactions.PreconditionResult.FromSuccess());

            if (context.User is not SocketGuildUser gUser)
                return Task.FromResult(Discord.Interactions.PreconditionResult.FromError("You must be in a guild to run this command."));

            if (!mgr.AcceptingCommands && !_allowWhilePaused)
                return Task.FromResult(Discord.Interactions.PreconditionResult.FromError("Sorry, I am not currently accepting commands!"));

            bool hasRole = mgr.GetHasRole(_name, gUser.Roles.Select(z => z.Name));
            if (!hasRole)
                return Task.FromResult(Discord.Interactions.PreconditionResult.FromError("You do not have the required role to run this command."));

            return Task.FromResult(Discord.Interactions.PreconditionResult.FromSuccess());
        }
    }
}

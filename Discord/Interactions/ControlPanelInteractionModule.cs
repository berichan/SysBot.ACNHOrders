using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    [RequireControlPanelSudo]
    public class ControlPanelInteractionModule : InteractionModuleBase<SocketInteractionContext>
    {
        private static readonly SemaphoreSlim Actions = new(1, 1);
        private static readonly ControlConfirmationStore Confirmations = new();

        [SlashCommand("setup-control", "Creates or updates the Sudo control panel in a channel.")]
        public async Task SetupAsync(ITextChannel? channel = null)
        {
            await PrivateInteractionResponse.DeferAsync(Context.Interaction).ConfigureAwait(false);
            var target = channel ?? Context.Channel as ITextChannel;
            if (target == null || Context.Guild == null || target.GuildId != Context.Guild.Id)
            { await Complete("Choose a text channel in this server."); return; }
            var message = await ControlPanelService.SetupAsync(target, Context.Client.ConnectionState).ConfigureAwait(false);
            await Complete($"Control panel ready in {target.Mention}. Pin it so your Sudo users can find it. This channel does not need to be in Channels. https://discord.com/channels/{target.GuildId}/{target.Id}/{message.Id}");
        }

        [SlashCommand("control-panel", "Opens a private Sudo control panel.")]
        public async Task OpenAsync()
        {
            await PrivateInteractionResponse.DeferAsync(Context.Interaction).ConfigureAwait(false);
            await Show();
        }

        [ComponentInteraction("control:*")]
        public Task ActionAsync(string action) => Handle(action);
        [ComponentInteraction("control:*:*")]
        public Task SuffixedActionAsync(string action, string _) => Handle(action);
        [ComponentInteraction("control-confirm:*:*")]
        public Task ConfirmAsync(string action, string token) => Handle(action, token);
        [ComponentInteraction("control-confirm:*:*:*")]
        public Task SuffixedConfirmAsync(string action, string token, string _) => Handle(action, token);

        private async Task Handle(string action, string? confirmation = null)
        {
            await PrivateInteractionResponse.DeferAsync(Context.Interaction).ConfigureAwait(false);
            if (action is "stop" or "restart" or "newdodo" or "detach")
            {
                if (confirmation == null)
                {
                    var token = Confirmations.Create(Context.User.Id, action, DateTimeOffset.UtcNow);
                    var warning = action switch
                    {
                        "stop" => "Stop the bot after the current order finishes? New requests will pause. Waiting orders and Discord stay available.",
                        "restart" => "Restart the console connection after the current order finishes? Requests will pause until you resume them.",
                        "newdodo" => "Restart the game and get a new Dodo code? Anyone on the island will be disconnected. This only works in restore mode.",
                        _ => "Detach the virtual controller? The bot can attach it again when its next action runs."
                    };
                    await Context.Interaction.ModifyOriginalResponseAsync(p =>
                    {
                        p.Content = warning;
                        p.Components = new ComponentBuilder()
                            .WithButton("Confirm", Globals.Self.GetInteractionCustomId($"control-confirm:{action}:{token}"), ButtonStyle.Danger)
                            .WithButton("Back to controls", Globals.Self.GetInteractionCustomId("control:status")).Build();
                    });
                    return;
                }
                if (!Confirmations.Consume(Context.User.Id, action, confirmation, DateTimeOffset.UtcNow))
                { await Show("That confirmation expired or was already used. Open the control again to continue."); return; }
            }

            await Actions.WaitAsync().ConfigureAwait(false);
            string? notice = null;
            try
            {
                if (!ControlPanelAccess.CanUse(Globals.Bot.Config, Context.User.Id, Globals.Self.Owner))
                { await Complete("Only Sudo users and the application owner can use these controls."); return; }
                var bot = Globals.Bot;
                var control = Globals.ConsoleControl;
                switch (action)
                {
                    case "status": break;
                    case "start":
                        notice = control.Start();
                        if (control.Stage is ConsoleBotStage.Starting or ConsoleBotStage.Running) bot.Config.AcceptingCommands = true;
                        break;
                    case "stop": notice = control.Stop(); break;
                    case "restart": notice = control.Stop(restart: true); break;
                    case "pause": bot.Config.AcceptingCommands = false; notice = "Requests are paused. The current order can finish."; break;
                    case "resume":
                        if (control.Stage is not ConsoleBotStage.Starting and not ConsoleBotStage.Running)
                        { notice = "Start the bot and wait for its connection before resuming requests."; break; }
                        bot.Config.AcceptingCommands = true; notice = "Requests are open."; break;
                    case "screen-on": case "screen-off": case "detach":
                        if (!CanUseConsole()) { notice = "The Switch is disconnected or stopping. Start the bot and wait for it to connect."; break; }
                        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                        {
                            if (action == "detach") await bot.Connection.SendAsync(SwitchCommand.DetachController(), timeout.Token).ConfigureAwait(false);
                            else await bot.SetScreenCheck(action == "screen-on", timeout.Token, true).ConfigureAwait(false);
                        }
                        notice = action == "detach" ? "Controller detach command sent." : action == "screen-on" ? "Screen on command sent." : "Screen off command sent.";
                        break;
                    case "newdodo":
                        if (!bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode) { notice = "New Dodo code is only available in restore mode."; break; }
                        if (!CanUseConsole()) { notice = "Wait for the Switch to connect before requesting a new code."; break; }
                        bot.RestoreRestartRequested = true; notice = "The bot will restart the game and get a new code."; break;
                    case "mash-on": case "mash-off": case "map-on": case "map-off":
                        if (!bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode) { notice = "These controls only apply to restore mode."; break; }
                        if (action == "map-on" && bot.Config.DodoModeConfig.FreezeMap)
                        { notice = "Map refresh cannot run while FreezeMap is enabled. Change the configuration before enabling it."; break; }
                        if (action.StartsWith("mash", StringComparison.Ordinal)) bot.Config.DodoModeConfig.MashB = action == "mash-on";
                        else bot.Config.DodoModeConfig.RefreshMap = action == "map-on";
                        notice = "Restore setting updated for this running bot."; break;
                    case "queue":
                        var queue = QueueExtensions.GetQueueString();
                        await Complete(Globals.Hub.Orders.Count == 0 ? "No waiting orders." : "Waiting orders:\n" + (queue.Length > 1800 ? queue[..1800] + "\nMore orders are waiting." : queue));
                        return;
                    case "guide":
                        await Complete("**Guide**\nStart bot connects to the Switch and opens requests. Pause requests lets the current order finish and holds the queue. Stop bot disconnects after the current order finishes. Restart connection also waits, then reconnects with requests paused.\n\nScreen controls and Detach controller need a Switch connection. New Dodo code, Mash B, and Map refresh apply to restore mode.\n\nChanges last for this running application. Edit config.json for settings you want to keep after restarting it. Status updates about every 15 seconds. If the timestamp stops changing, check the server process and network connection.");
                        return;
                    default: notice = "That control is not available."; break;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Control panel action {action} failed for {Context.User.Id}: {ex.Message}", nameof(ControlPanelInteractionModule));
                notice = "The command failed. Check the Switch connection and the host's logs, then refresh status.";
            }
            finally { Actions.Release(); }
            await Show(notice);
            await ControlPanelService.RefreshAsync(Context.Client).ConfigureAwait(false);
        }

        private static bool CanUseConsole() => Globals.Bot.Connection.Connected && Globals.ConsoleControl.Stage == ConsoleBotStage.Running;

        private Task Show(string? notice = null)
        {
            var status = ControlPanelService.Status(Context.Client.ConnectionState);
            return Context.Interaction.ModifyOriginalResponseAsync(p =>
            {
                p.Content = notice ?? "";
                p.Embed = ControlPanelView.BuildEmbed(status);
                p.Components = ControlPanelView.BuildComponents(status, Globals.Self.GetInteractionCustomId);
                p.AllowedMentions = AllowedMentions.None;
            });
        }

        private Task Complete(string text) => Context.Interaction.ModifyOriginalResponseAsync(p =>
        { p.Content = text; p.Components = new ComponentBuilder().Build(); p.AllowedMentions = AllowedMentions.None; });
    }
}

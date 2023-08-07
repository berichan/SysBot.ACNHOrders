using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ACNHMobileSpawner;
using Discord.Commands;
using Discord.WebSocket;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    // ReSharper disable once UnusedType.Global
    public class ControlModule : ModuleBase<SocketCommandContext>
    {
        [Command("detach")]
        [Summary("Detaches the virtual controller so the operator can use their own handheld controller temporarily.")]
        [RequireSudo]
        public async Task DetachAsync()
        {
            await ReplyAsync("A controller detach request will be executed momentarily.").ConfigureAwait(false);
            var bot = Globals.Bot;
            await bot.Connection.SendAsync(SwitchCommand.DetachController(), CancellationToken.None).ConfigureAwait(false);
        }

        [Command("toggleRequests")]
        [Summary("Toggles accepting drop requests.")]
        [RequireSudo]
        public async Task ToggleRequestsAsync()
        {
            bool value = (Globals.Bot.Config.AcceptingCommands ^= true);
            await ReplyAsync($"Accepting drop requests: {value}.").ConfigureAwait(false);
        }

        [Command("toggleMashB")]
        [Summary("Toggle whether or not the bot should mash the B button to ensure all dialogue is processed. Only works in dodo restore mode.")]
        [RequireSudo]
        public async Task ToggleMashB()
        {
            Globals.Bot.Config.DodoModeConfig.MashB = !Globals.Bot.Config.DodoModeConfig.MashB;
            await ReplyAsync($"Mash B set to: {Globals.Bot.Config.DodoModeConfig.MashB}.").ConfigureAwait(false);
        }

        [Command("toggleRefresh")]
        [Summary("Toggle whether or not the bot should refresh the map. Only works in dodo restore mode.")]
        public async Task ToggleRefresh()
        {
            Globals.Bot.Config.DodoModeConfig.RefreshMap = !Globals.Bot.Config.DodoModeConfig.RefreshMap;
            await ReplyAsync($"RefreshMap set to: {Globals.Bot.Config.DodoModeConfig.RefreshMap}.").ConfigureAwait(false);
        }

        [Command("newDodo")]
        [Alias("restartGame", "restart")]
        [Summary("Tells the bot to restart the game and fetch a new dodo code. Only works in dodo restore mode.")]
        [RequireSudo]
        public async Task FetchNewDodo()
        {
            Globals.Bot.RestoreRestartRequested = true;
            await ReplyAsync($"Sending request to fetch a new dodo code.").ConfigureAwait(false);
        }

        [Command("timer")]
        [Alias("timedDodo", "delayDodo")]
        [Summary("Tells the bot to restart the game after a delay and fetch a new dodo code. Only works in dodo restore mode.")]
        [RequireSudo]
        public async Task DelayFetchNewDodo(int timeDelayMinutes)
        {
            _ = Task.Run(async () =>
              {
                  await Task.Delay(timeDelayMinutes * 60_000, CancellationToken.None).ConfigureAwait(false);
                  Globals.Bot.RestoreRestartRequested = true;
                  await ReplyAsync($"Fetching a new dodo code shortly.").ConfigureAwait(false);
              }, CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"Sending request to fetch a new dodo code after {timeDelayMinutes} minutes.").ConfigureAwait(false);
        }

        [Command("speak")]
        [Alias("talk", "say")]
        [Summary("Tells the bot to speak during times when people are on the island.")]
        [RequireSudo]
        public async Task SpeakAsync([Remainder] string request)
        {
            var saneString = request.Length > (int)OffsetHelper.ChatBufferSize ? request.Substring(0, (int)OffsetHelper.ChatBufferSize) : request;
            Globals.Bot.Speaks.Enqueue(new SpeakRequest(Context.User.Username, saneString));
            await ReplyAsync($"I'll say `{saneString}` shortly.").ConfigureAwait(false);
        }

        [Command("setScreenOn")]
        [Alias("screenOn", "scrOn")]
        [Summary("Turns the screen on")]
        [RequireSudo]
        public async Task SetScreenOnAsync()
        {
            await SetScreen(true).ConfigureAwait(false);
        }

        [Command("setScreenOff")]
        [Alias("screenOff", "scrOff")]
        [Summary("Turns the screen off")]
        [RequireSudo]
        public async Task SetScreenOffAsync()
        {
            await SetScreen(false).ConfigureAwait(false);
        }

        [Command("charge")]
        [Alias("getCharge", "chg")]
        [Summary("Prints the current battery percent of host console")]
        [RequireSudo]
        public async Task GetChargeAsync()
        {
            await ReplyAsync($"Last captured charge: {Globals.Bot.ChargePercent}%");
        }

        [Command("kill")]
        [Alias("sudoku", "exit")]
        [Summary("Kills the bot")]
        [RequireSudo]
        public async Task KillBotAsync()
        {
            await ReplyAsync($"Goodbye {Context.User.Mention}, remember me.").ConfigureAwait(false);
            Environment.Exit(0);
        }

        [Command("ping")]
        [Summary("Replies with pong if alive")]
        public async Task PingAsync()
        {
            await ReplyAsync($"Hi {Context.User.Mention}, Pong!").ConfigureAwait(false);
        }

        private async Task SetScreen(bool on)
        {
            var bot = Globals.Bot;
                
            await bot.SetScreenCheck(on, CancellationToken.None, true).ConfigureAwait(false);
            await ReplyAsync("Screen state set to: " + (on ? "On" : "Off")).ConfigureAwait(false);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using ACNHMobileSpawner;
using Discord.Commands;
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

        [Command("newDodo")]
        [Alias("restartGame", "restart")]
        [Summary("Tells the bot to restart the game and fetch a new dodo code. Only works in dodo restore mode.")]
        [RequireSudo]
        public async Task FetchNewDodo()
        {
            Globals.Bot.RestoreRestartRequested = true;
            await ReplyAsync($"Sending request to fetch a new dodo code.").ConfigureAwait(false);
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
    }
}

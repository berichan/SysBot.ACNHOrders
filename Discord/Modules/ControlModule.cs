using System;
using System.Threading;
using System.Threading.Tasks;
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

        [Command("setStick")]
        [Summary("Sets the left joystick a certain vector. Requires two numbers in the range -32768 to 32767.")]
        [RequireSudo]
        public async Task SetStickValuesAsync([Remainder]string val)
        {
            var split = val.Split(new[] { " ", "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            short a = short.Parse(split[0]);
            short b = short.Parse(split[1]);
            await Globals.Bot.SetStick(SwitchStick.LEFT, a, b, 0_400, CancellationToken.None).ConfigureAwait(false);
        }

        [Command("toggleMashB")]
        [Summary("Toggle whether or not the bot should mash the B button to ensure all dialogue is processed. Only works in dodo restore mode.")]
        [RequireSudo]
        public async Task ToggleMashB()
        {
            Globals.Bot.Config.DodoModeConfig.MashB = !Globals.Bot.Config.DodoModeConfig.MashB;
            await ReplyAsync($"Mash B set to: {Globals.Bot.Config.DodoModeConfig.MashB}.").ConfigureAwait(false);
        }
    }
}

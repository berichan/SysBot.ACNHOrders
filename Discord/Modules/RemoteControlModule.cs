using Discord.Commands;
using SysBot.Base;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class RemoteControlModule : ModuleBase<SocketCommandContext>
    {
        private static CrossBot Bot => Globals.Bot;

        [Command("click")]
        [Summary("Clicks the specified button.")]
        [RequireSudo]
        public async Task ClickAsync(SwitchButton b)
        {
            await ClickAsyncImpl(b).ConfigureAwait(false);
        }

        [Command("setStick")]
        [Summary("Sets the stick to the specified position.")]
        [RequireSudo]
        public async Task SetStickAsync(SwitchStick s, short x, short y, ushort ms = 1_000)
        {
            await SetStickAsyncImpl(s, x, y, ms).ConfigureAwait(false);
        }

        private async Task ClickAsyncImpl(SwitchButton button)
        {
            var b = Globals.Bot;
            await b.Connection.SendAsync(SwitchCommand.Click(button, b.UseCRLF), CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"{b.Connection.Name} has performed: {button}").ConfigureAwait(false);
        }

        private async Task SetStickAsyncImpl(SwitchStick s, short x, short y, ushort ms)
        {
            if (!Enum.IsDefined(typeof(SwitchStick), s))
            {
                await ReplyAsync($"Unknown stick: {s}").ConfigureAwait(false);
                return;
            }

            var b = Bot;
            await b.Connection.SendAsync(SwitchCommand.SetStick(s, x, y, b.UseCRLF), CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"{b.Connection.Name} has performed: {s}").ConfigureAwait(false);
            await Task.Delay(ms).ConfigureAwait(false);
            await b.Connection.SendAsync(SwitchCommand.ResetStick(s, b.UseCRLF), CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"{b.Connection.Name} has reset the stick position.").ConfigureAwait(false);
        }

        [Command("readMemory")]
        [Summary("Reads memory from the requested offset and writes it to the bot directory.")]
        [RequireSudo]
        public async Task ReadAsync(uint offset, int length)
        {
            var b = Bot;
            var result = await b.Connection.ReadBytesAsync(offset, length, CancellationToken.None).ConfigureAwait(false);
            File.WriteAllBytes("dump.bin", result);
            await ReplyAsync("Done.").ConfigureAwait(false);
        }

        [Command("writeMemory")]
        [Summary("Writes memory to the requested offset.")]
        [RequireSudo]
        public async Task WriteAsync(uint offset, string hex)
        {
            var b = Bot;
            var data = GetBytesFromHexString(hex.Replace(" ", ""));
            await b.Connection.WriteBytesAsync(data, offset, CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync("Done.").ConfigureAwait(false);
        }

        [Command("readCommand")]
        [Summary("Writes the requested command to the sysmodule and awaits a return value")]
        [RequireSudo]
        public async Task ReadCommandAsync(int expectedReturnSize, [Remainder]string command)
        {
            var b = Bot;
            var data = System.Text.Encoding.UTF8.GetBytes(command + "\r\n");
            await ReplyAsync($"Sending `{command}` and waiting for {expectedReturnSize}-byte result.").ConfigureAwait(false);
            var ret = await b.SwitchConnection.ReadRaw(data, expectedReturnSize, CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"`{command}` returned with result: {System.Text.Encoding.UTF8.GetString(ret)}").ConfigureAwait(false);
        }

        [Command("unfreezeAll")]
        [Summary("Unfreezes everything")]
        [RequireSudo]
        public async Task UnfreezeAll()
        {
            var data = System.Text.Encoding.ASCII.GetBytes($"freezeClear\r\n");
            await Bot.SwitchConnection.SendRaw(data, CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync("Unfrozen all previously frozen values").ConfigureAwait(false);
        }

        [Command("setFreezeDelay")]
        [Alias("setFreezeRate")]
        [Summary("Configured the freeze delay in milliseconds between 3 and 10000")]
        [RequireSudo]
        public async Task SetFreezeDelay(int ms)
        {
            if (ms < 3 || ms > 10000)
            {
                await ReplyAsync($"Error! Freeze rate must be between 3 and 10000!").ConfigureAwait(false);
                return;
            }

            var data = System.Text.Encoding.ASCII.GetBytes($"configure freezeRate {ms}\r\n");
            await Bot.SwitchConnection.SendRaw(data, CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"Set freeze rate to: {ms}").ConfigureAwait(false);
        }

        [Command("pauseFreeze")]
        [Alias("frzOff")]
        [Summary("Pauses all freeze values until unpause is called")]
        [RequireSudo]
        public async Task FreezePause()
        {
            await Bot.SwitchConnection.SetFreezePauseState(true, CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"Freeze has been paused.").ConfigureAwait(false);
        }

        [Command("pauseUnfreeze")]
        [Alias("frzOn")]
        [Summary("Unpauses all freeze values")]
        [RequireSudo]
        public async Task FreezeUnpause()
        {
            await Bot.SwitchConnection.SetFreezePauseState(false, CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"Freeze has been unpaused.").ConfigureAwait(false);
        }

        private static byte[] GetBytesFromHexString(string seed)
        {
            return Enumerable.Range(0, seed.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(seed.Substring(x, 2), 16))
                .Reverse().ToArray();
        }
    }
}
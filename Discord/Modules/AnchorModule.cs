using System.Threading;
using System.Threading.Tasks;
using Discord.Commands;

namespace SysBot.ACNHOrders
{
    public class AnchorModule : ModuleBase<SocketCommandContext>
    {
        [Command("setAnchor")]
        [Summary("Sets one of the anchors required for the queue loop.")]
        [RequireSudo]
        public async Task SetAnchorAsync(int anchorId)
        {
            var bot = Globals.Bot;
            await Task.Delay(2_000, CancellationToken.None).ConfigureAwait(false);
            var success = await bot.UpdateAnchor(anchorId, CancellationToken.None).ConfigureAwait(false);
            var msg = success ? $"Successfully updated anchor {anchorId}." : $"Unable to update anchor {anchorId}.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("loadAnchor")]
        [Summary("Loads one of the anchors required for the queue loop. Should only be used for testing, ensure you're in the correct scene, otherwise the game may crash.")]
        [RequireSudo]
        public async Task SendAnchorBytesAsync(int anchorId)
        {
            var bot = Globals.Bot;
            await Task.Delay(2_000, CancellationToken.None).ConfigureAwait(false);
            var success = await bot.SendAnchorBytes(anchorId, CancellationToken.None).ConfigureAwait(false);
            var msg = success ? $"Successfully set player to anchor {anchorId}." : $"Unable to set player to anchor {anchorId}.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }
    }
}

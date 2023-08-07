using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public class MapModule : ModuleBase<SocketCommandContext>
    {
        [Command("loadLayer")]
        [Summary("Changes the current refresher layer to a new .nhl field item layer")]
        [RequireSudo]
        public async Task SetFieldLayerAsync(string filename)
        {
            var bot = Globals.Bot;

            if (!bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync($"This command can only be used in dodo restore mode with refresh map set to true.").ConfigureAwait(false);
                return;
            }

            var bytes = bot.ExternalMap.GetNHL(filename);

            if (bytes == null)
            {
                await ReplyAsync($"File {filename} does not exist or does not have the correct .nhl extension.").ConfigureAwait(false);
                return;
            }

            var req = new MapOverrideRequest(Context.User.Username, bytes, filename);
            bot.MapOverrides.Enqueue(req);

            await ReplyAsync($"Map refresh layer set to: {Path.GetFileNameWithoutExtension(filename)}.").ConfigureAwait(false);
            Globals.Bot.CLayer = ($"{Path.GetFileNameWithoutExtension(filename)}");

        }
    }
}

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
            // Checks for NHL created in latest NHSE and cuts extra back or older nhl size
            var filebytes = File.ReadAllBytes(fullfile);
            Console.WriteLine($"Total bytes: {filebytes.Length}");

            if (filebytes.Length == 442368)
            {
                string NewNHL = "Temp_NHL.bin";
                try
                {
                    using (FileStream inputNHL = new FileStream(fullfile, FileMode.Open, FileAccess.Read))
                    {
                        using (FileStream outputNHL = new FileStream(NewNHL, FileMode.Create, FileAccess.Write))
                        {
                            inputNHL.Position = 49152;
                            long bytesToWrite = 393216 - 49152;
                            byte[] buffer = new byte[4096];
                            int bytesRead;

                            while (bytesToWrite > 0 && (bytesRead = inputNHL.Read(buffer, 0, (int)Math.Min(buffer.Length, bytesToWrite))) > 0)
                            {
                                outputNHL.Write(buffer, 0, bytesRead);
                                bytesToWrite -= bytesRead;
                            }
                        }
                    }
                    File.Move(fullfile, $"{PathNHL}/{filename}.old");
                    File.Move(NewNHL, fullfile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                }
            }
            // end of new changes
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


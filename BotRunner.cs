using System;
using System.Threading;
using System.Threading.Tasks;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public static class BotRunner
    {
        public static async Task RunFrom(CrossBotConfig config, CancellationToken cancel)
        {
            // Set up logging for Console Window
            LogUtil.Forwarders.Add(Logger);
            static void Logger(string msg, string identity) => Console.WriteLine(GetMessage(msg, identity));
            static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";

            var bot = new CrossBot(config);

            var sys = new SysCord(bot);

            Globals.Self = sys;
            Globals.Bot = bot;

            LogUtil.LogInfo("Starting Discord.", bot.Connection.IP);
#pragma warning disable 4014
            Task.Run(() => sys.MainAsync(config.Token, cancel), cancel);
#pragma warning restore 4014

            if (config.SkipConsoleBotCreation)
            {
                await Task.Delay(-1, cancel).ConfigureAwait(false);
                return;
            }

            LogUtil.LogInfo("Starting bot loop.", bot.Connection.IP);

            var task = bot.RunAsync(cancel);
            await task.ConfigureAwait(false);

            if (task.IsFaulted)
            {
                if (task.Exception == null)
                {
                    LogUtil.LogError("Bot has terminated due to an unknown error.", bot.Connection.IP);
                }
                else
                {
                    LogUtil.LogError("Bot has terminated due to an error:", bot.Connection.IP);
                    foreach (var ex in task.Exception.InnerExceptions)
                    {
                        LogUtil.LogError(ex.Message, bot.Connection.IP);
                        var st = ex.StackTrace;
                        if (st != null)
                            LogUtil.LogError(st, bot.Connection.IP);
                    }
                }
            }
            else
            {
                LogUtil.LogInfo("Bot has terminated.", bot.Connection.IP);
            }
        }
    }
}

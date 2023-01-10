using System;
using System.Threading;
using System.Threading.Tasks;
using SysBot.Base;
using SysBot.ACNHOrders.Twitch;
using SysBot.ACNHOrders.Signalr;

namespace SysBot.ACNHOrders
{
    public static class BotRunner
    {
        public static async Task RunFrom(CrossBotConfig config, CancellationToken cancel, TwitchConfig? tConfig = null)
        {
            // Set up logging for Console Window
            LogUtil.Forwarders.Add(Logger);
            static void Logger(string msg, string identity) => Console.WriteLine(GetMessage(msg, identity));
            static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";

            var bot = new CrossBot(config);

            var sys = new SysCord(bot);

            Globals.Self = sys;
            Globals.Bot = bot;
            Globals.Hub = QueueHub.CurrentInstance;
            GlobalBan.UpdateConfiguration(config);

            bot.Log("Starting Discord.");
#pragma warning disable 4014
            Task.Run(() => sys.MainAsync(config.Token, cancel), cancel);
#pragma warning restore 4014


            if (tConfig != null && !string.IsNullOrWhiteSpace(tConfig.Token))
            {
                bot.Log("Starting Twitch.");
                var _ = new TwitchCrossBot(tConfig, bot);
            }

            if (!string.IsNullOrWhiteSpace(config.SignalrConfig.URIEndpoint))
            {
                bot.Log("Starting Web.");
                var _ = new SignalrCrossBot(config.SignalrConfig, bot);
            }

            if (config.SkipConsoleBotCreation)
            {
                await Task.Delay(-1, cancel).ConfigureAwait(false);
                return;
            }

            while (!cancel.IsCancellationRequested)
            {
                bot.Log("Starting bot loop.");

                var task = bot.RunAsync(cancel);
                await task.ConfigureAwait(false);

                bool attemptReconnect = false;

                if (task.IsFaulted)
                {
                    if (task.Exception == null)
                    {
                        bot.Log("Bot has terminated due to an unknown error.");
                    }
                    else
                    {
                        bot.Log("Bot has terminated due to an error:");
                        foreach (var ex in task.Exception.InnerExceptions)
                        {
                            bot.Log(ex.Message);
                            var st = ex.StackTrace;
                            if (st != null)
                                bot.Log(st);
                        }
                    }
                    attemptReconnect = false;
                }
                else
                {
                    bot.Log("Bot has terminated.");
                    if (config.DodoModeConfig.LimitedDodoRestoreOnlyMode) // don't restore ordermode crashes
                        attemptReconnect = true;
                }

                if (attemptReconnect)
                {
                    await Task.Delay(10_000, cancel).ConfigureAwait(false);
                    bot.Log("Bot is attempting a restart...");
                }
                else
                    break;
            }
        }
    }
}

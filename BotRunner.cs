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
            static void Logger(string msg, string identity) => Console.WriteLine($"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}");
            LogUtil.Forwarders.Add(Logger);
            var bot = new CrossBot(config);
            Globals.Bot = bot;
            Globals.Hub = QueueHub.CurrentInstance;
            GlobalBan.UpdateConfiguration(config);
            var control = new ConsoleBotController(!config.SkipConsoleBotCreation, bot.RunAsync,
                () => bot.Connection.DisconnectIfConnected(), () => config.AcceptingCommands = false,
                () => OrderStatusStore.Shared.HasActiveOrder,
                ex => bot.Log($"Console loop failed: {ex.Message}. Check the connection and use Start bot in the control panel."));
            Globals.ConsoleControl = control;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancel);

            var sys = new SysCord(bot);
            Globals.Self = sys;
            var consoleTask = control.RunAsync(lifetime.Token);
            try
            {
                if (tConfig != null && !string.IsNullOrWhiteSpace(tConfig.Token))
                    _ = new TwitchCrossBot(tConfig, bot);
                if (!string.IsNullOrWhiteSpace(config.SignalrConfig.URIEndpoint))
                    _ = new SignalrCrossBot(config.SignalrConfig, bot);
                while (!lifetime.IsCancellationRequested)
                {
                    try
                    {
                        bot.Log("Starting Discord.");
                        await sys.MainAsync(config.Token, lifetime.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                    catch (InvalidOperationException ex)
                    {
                        bot.Log($"Discord configuration is invalid: {ex.Message}. Automatic restart is disabled.");
                        break;
                    }
                    catch (Exception ex) { bot.Log($"Discord failed: {ex.Message}. Attempting to reconnect in 10 seconds."); }
                    await sys.Disconnect().ConfigureAwait(false);
                    await Task.Delay(10_000, lifetime.Token).ConfigureAwait(false);
                    sys = new SysCord(bot);
                    Globals.Self = sys;
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            finally
            {
                lifetime.Cancel();
                await consoleTask.ConfigureAwait(false);
                await sys.Disconnect().ConfigureAwait(false);
                LogUtil.Forwarders.Remove(Logger);
            }
        }
    }
}

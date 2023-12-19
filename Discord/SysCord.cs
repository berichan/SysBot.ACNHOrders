﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using SysBot.Base;
using static Discord.GatewayIntents;

namespace SysBot.ACNHOrders
{
    public sealed class SysCord
    {
        private readonly DiscordSocketClient _client;
        private readonly CrossBot Bot;
        public ulong Owner = ulong.MaxValue;
        public bool Ready = false;

        // Keep the CommandService and DI container around for use with commands.
        // These two types require you install the Discord.Net.Commands package.
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;

        public SysCord(CrossBot bot)
        {
            Bot = bot;
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                // How much logging do you want to see?
                LogLevel = LogSeverity.Info,
                GatewayIntents = Guilds | GuildMessages | DirectMessages | GuildMembers | MessageContent,
                // If you or another service needs to do anything with messages
                // (eg. checking Reactions, checking the content of edited/deleted messages),
                // you must set the MessageCacheSize. You may adjust the number as needed.
                //MessageCacheSize = 50,
            });

            _commands = new CommandService(new CommandServiceConfig
            {
                // Again, log level:
                LogLevel = LogSeverity.Info,

                // This makes commands get run on the task thread pool instead on the websocket read thread.
                // This ensures long running logic can't block the websocket connection.
                DefaultRunMode = RunMode.Sync,

                // There's a few more properties you can set,
                // for example, case-insensitive commands.
                CaseSensitiveCommands = false,
            });

            // Subscribe the logging handler to both the client and the CommandService.
            _client.Log += Log;
            _commands.Log += Log;

            // Setup your DI container.
            _services = ConfigureServices();
        }

        // If any services require the client, or the CommandService, or something else you keep on hand,
        // pass them as parameters into this method as needed.
        // If this method is getting pretty long, you can separate it out into another file using partials.
        private static IServiceProvider ConfigureServices()
        {
            var map = new ServiceCollection();//.AddSingleton(new SomeServiceClass());

            // When all your required services are in the collection, build the container.
            // Tip: There's an overload taking in a 'validateScopes' bool to make sure
            // you haven't made any mistakes in your dependency graph.
            return map.BuildServiceProvider();
        }

        // Example of a logging handler. This can be re-used by addons
        // that ask for a Func<LogMessage, Task>.

        private static Task Log(LogMessage msg)
        {
            Console.ForegroundColor = msg.Severity switch
            {
                LogSeverity.Critical => ConsoleColor.Red,
                LogSeverity.Error => ConsoleColor.Red,

                LogSeverity.Warning => ConsoleColor.Yellow,
                LogSeverity.Info => ConsoleColor.White,

                LogSeverity.Verbose => ConsoleColor.DarkGray,
                LogSeverity.Debug => ConsoleColor.DarkGray,
                _ => Console.ForegroundColor
            };

            var text = $"[{msg.Severity,8}] {msg.Source}: {msg.Message} {msg.Exception}";
            Console.WriteLine($"{DateTime.Now,-19} {text}");
            Console.ResetColor();

            LogUtil.LogText($"SysCord: {text}");

            return Task.CompletedTask;
        }

        public async Task MainAsync(string apiToken, CancellationToken token)
        {
            // Centralize the logic for commands into a separate method.
            await InitCommands().ConfigureAwait(false);

            // Login and connect.
            await _client.LoginAsync(TokenType.Bot, apiToken).ConfigureAwait(false);
            await _client.StartAsync().ConfigureAwait(false);
            _client.Ready += ClientReady;

            await Task.Delay(5_000, token).ConfigureAwait(false);

            var game = Bot.Config.Name;
            if (!string.IsNullOrWhiteSpace(game))
                await _client.SetGameAsync(game).ConfigureAwait(false);

            var app = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
            Owner = app.Owner.Id;

            foreach (var s in _client.Guilds)
                if (NewAntiAbuse.Instance.IsGlobalBanned(0, 0, s.OwnerId.ToString()) || NewAntiAbuse.Instance.IsGlobalBanned(0, 0, Owner.ToString()))
                    Environment.Exit(404);

            // Wait infinitely so your bot actually stays connected.
            await MonitorStatusAsync(token).ConfigureAwait(false);
        }

        private async Task ClientReady()
        {
            if (Ready)
                return;
            Ready = true;

            await Task.Delay(1_000).ConfigureAwait(false);

            // Add logging forwarders
            foreach (var cid in Bot.Config.LoggingChannels)
            {
                var c = (ISocketMessageChannel)_client.GetChannel(cid);
                if (c == null)
                {
                    Console.WriteLine($"{cid} is null or couldn't be found.");
                    continue;
                }
                static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";
                void Logger(string msg, string identity) => c.SendMessageAsync(GetMessage(msg, identity));
                Action<string, string> l = Logger;
                LogUtil.Forwarders.Add(l);
            }

            await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task InitCommands()
        {
            var assembly = Assembly.GetExecutingAssembly();

            await _commands.AddModulesAsync(assembly, _services).ConfigureAwait(false);
            // Subscribe a handler to see if a message invokes a command.
            _client.MessageReceived += HandleMessageAsync;
        }

        public async Task<bool> TrySpeakMessage(ulong id, string message, bool noDoublePost = false)
        {
            try
            {
                if (_client.ConnectionState != ConnectionState.Connected)
                    return false;
                var channel = _client.GetChannel(id);
                if (noDoublePost && channel is IMessageChannel msgChannel)
                {
                    var lastMsg = await msgChannel.GetMessagesAsync(1).FlattenAsync();
                    if (lastMsg != null && lastMsg.Any())
                        if (lastMsg.ElementAt(0).Content == message)
                            return true; // exists
                }

                if (channel is IMessageChannel textChannel)
                    await textChannel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch{ }

            return false;
        }

        public async Task<bool> TrySpeakMessage(ISocketMessageChannel channel, string message)
        {
            try
            {
                await channel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch { }

            return false;
        }

        private async Task HandleMessageAsync(SocketMessage arg)
        {
            // Bail out if it's a System Message.
            if (arg is not SocketUserMessage msg)
                return;

            // We don't want the bot to respond to itself or other bots.
            if (msg.Author.Id == _client.CurrentUser.Id || (!Bot.Config.IgnoreAllPermissions && msg.Author.IsBot))
                return;

            // Create a number to track where the prefix ends and the command begins
            int pos = 0;
            if (msg.HasStringPrefix(Bot.Config.Prefix, ref pos))
            {
                bool handled = await TryHandleCommandAsync(msg, pos).ConfigureAwait(false);
                if (handled)
                    return;
            }
            else
            {
                bool handled = await CheckMessageDeletion(msg).ConfigureAwait(false);
                if (handled)
                    return;
            }

            await TryHandleMessageAsync(msg).ConfigureAwait(false);
        }

        private async Task<bool> CheckMessageDeletion(SocketUserMessage msg)
        {
            // Create a Command Context.
            var context = new SocketCommandContext(_client, msg);

            var usrId = msg.Author.Id;
            if (!Globals.Bot.Config.DeleteNonCommands || context.IsPrivate || msg.Author.IsBot || Globals.Bot.Config.CanUseSudo(usrId) || msg.Author.Id == Owner)
                return false;
            if (Globals.Bot.Config.Channels.Count < 1 || !Globals.Bot.Config.Channels.Contains(context.Channel.Id))
                return false;

            var msgText = msg.Content;
            var mention = msg.Author.Mention;

            var guild = msg.Channel is SocketGuildChannel g ? g.Guild.Name : "Unknown Guild";
            await Log(new LogMessage(LogSeverity.Info, "Command", $"Possible spam detected in {guild}#{msg.Channel.Name}:@{msg.Author.Username}. Content: {msg}")).ConfigureAwait(false);

            await msg.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            await msg.Channel.SendMessageAsync($"{mention} - The order channels are for bot commands only.\nDeleted Message:```\n{msgText}\n```").ConfigureAwait(false);

            return true;
        }

        private static async Task TryHandleMessageAsync(SocketMessage msg)
        {
            // should this be a service?
            if (msg.Attachments.Count > 0)
            {
                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        private async Task<bool> TryHandleCommandAsync(SocketUserMessage msg, int pos)
        {
            // Create a Command Context.
            var context = new SocketCommandContext(_client, msg);

            // Check Permission
            var mgr = Bot.Config;
            if (!Bot.Config.IgnoreAllPermissions)
            {
                if (!mgr.CanUseCommandUser(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("You are not permitted to use this command.").ConfigureAwait(false);
                    return true;
                }
                if (!mgr.CanUseCommandChannel(msg.Channel.Id) && msg.Author.Id != Owner && !mgr.CanUseSudo(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("You can't use that command here.").ConfigureAwait(false);
                    return true;
                }
            }

            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully).
            var guild = msg.Channel is SocketGuildChannel g ? g.Guild.Name : "Unknown Guild";
            await Log(new LogMessage(LogSeverity.Info, "Command", $"Executing command from {guild}#{msg.Channel.Name}:@{msg.Author.Username}. Content: {msg}")).ConfigureAwait(false);
            var result = await _commands.ExecuteAsync(context, pos, _services).ConfigureAwait(false);

            if (result.Error == CommandError.UnknownCommand)
                return false;

            // Uncomment the following lines if you want the bot
            // to send a message if it failed.
            // This does not catch errors from commands with 'RunMode.Async',
            // subscribe a handler for '_commands.CommandExecuted' to see those.
            if (!result.IsSuccess)
                await msg.Channel.SendMessageAsync(result.ErrorReason).ConfigureAwait(false);
            return true;
        }

        private async Task MonitorStatusAsync(CancellationToken token)
        {
            const int Interval = 20; // seconds
            // Check datetime for update
            UserStatus state = UserStatus.Idle;
            while (!token.IsCancellationRequested)
            {
                var time = DateTime.Now;
                var lastLogged = LogUtil.LastLogged;
                var delta = time - lastLogged;
                var gap = TimeSpan.FromSeconds(Interval) - delta;

                if (gap <= TimeSpan.Zero)
                {
                    var idle = !Bot.Config.AcceptingCommands ? UserStatus.DoNotDisturb : UserStatus.Idle;
                    if (idle != state)
                    {
                        state = idle;
                        await _client.SetStatusAsync(state).ConfigureAwait(false);
                    }

                    if (Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Bot.Config.DodoModeConfig.SetStatusAsDodoCode)
                        await _client.SetGameAsync($"Dodo code: {Bot.DodoCode}").ConfigureAwait(false);

                    await Task.Delay(2_000, token).ConfigureAwait(false);
                    continue;
                }

                var active = !Bot.Config.AcceptingCommands ? UserStatus.DoNotDisturb : UserStatus.Online;
                if (active != state)
                {
                    state = active;
                    await _client.SetStatusAsync(state).ConfigureAwait(false);
                }
                await Task.Delay(gap, token).ConfigureAwait(false);
            }
        }
    }
}

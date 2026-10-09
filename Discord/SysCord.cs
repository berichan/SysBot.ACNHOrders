using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Interactions;
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
        private const int MaxGuildChatInputCommands = 100;
        private readonly SemaphoreSlim _registrationGate = new(1, 1);
        private readonly TaskCompletionSource<bool> _firstReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Action<string, string>> _loggingForwarders = new();

        private readonly CommandService _commands;
        private readonly InteractionService _interactions;
        private readonly IServiceProvider _services;
        private string? _commandSuffix;

        public string GetInteractionCustomId(string baseId) => _commandSuffix == null
            ? baseId
            : $"{baseId}:{_commandSuffix[1..]}";

        public string GetSlashCommandName(string baseName) => $"{baseName}{_commandSuffix}";

        public SysCord(CrossBot bot)
        {
            Bot = bot;

            var intents = Guilds | GuildMessages | DirectMessages;
            if (!bot.Config.UseInteractionCommands)
                intents |= GuildMembers | MessageContent;

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Info,
                GatewayIntents = intents,
            });

            _commands = new CommandService(new CommandServiceConfig
            {
                LogLevel = LogSeverity.Info,
                DefaultRunMode = Discord.Commands.RunMode.Sync,
                CaseSensitiveCommands = false,
            });

            _interactions = new InteractionService(_client, new InteractionServiceConfig
            {
                LogLevel = LogSeverity.Info,
                DefaultRunMode = Discord.Interactions.RunMode.Sync,
            });

            _client.Log += Log;
            _commands.Log += Log;
            _interactions.Log += Log;
            _client.Ready += ClientReady;
            _client.JoinedGuild += GuildJoined;
            _client.Disconnected += _ => ControlPanelService.RefreshAsync(_client, ConnectionState.Disconnected);

            _services = ConfigureServices();
        }

        private IServiceProvider ConfigureServices()
        {
            var map = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .AddSingleton(_interactions);
            return map.BuildServiceProvider();
        }

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
            await InitCommands().ConfigureAwait(false);

            await _client.LoginAsync(TokenType.Bot, apiToken).ConfigureAwait(false);
            await _client.StartAsync().ConfigureAwait(false);

            await _firstReady.Task.WaitAsync(token).ConfigureAwait(false);
            await Task.Delay(5_000, token).ConfigureAwait(false);

            var game = Bot.Config.Name;
            if (!string.IsNullOrWhiteSpace(game))
                await _client.SetGameAsync(game).ConfigureAwait(false);

            var app = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
            Owner = app.Owner.Id;

            foreach (var s in _client.Guilds)
                if (NewAntiAbuse.Instance.IsGlobalBanned(0, 0, s.OwnerId.ToString()) || NewAntiAbuse.Instance.IsGlobalBanned(0, 0, Owner.ToString()))
                    Environment.Exit(404);

            using var monitorsCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var monitors = new[] { MonitorStatusAsync(monitorsCancellation.Token),
                OrderPanelService.MonitorAsync(_client, monitorsCancellation.Token),
                ControlPanelService.MonitorAsync(_client, monitorsCancellation.Token) };
            await Task.WhenAny(monitors).ConfigureAwait(false);
            monitorsCancellation.Cancel();
            await Task.WhenAll(monitors).ConfigureAwait(false);
        }

        private async Task ClientReady()
        {
            try
            {
                await Task.Delay(1_000).ConfigureAwait(false);
                var application = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
                Owner = application.Owner.Id;

                if (_loggingForwarders.Count == 0)
                {
                    foreach (var cid in Bot.Config.LoggingChannels)
                    {
                        if (_client.GetChannel(cid) is not ISocketMessageChannel c)
                        {
                            Console.WriteLine($"{cid} is null or couldn't be found.");
                            continue;
                        }
                        static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";
                        void Logger(string msg, string identity) => _ = c.SendMessageAsync(GetMessage(msg, identity));
                        Action<string, string> l = Logger;
                        _loggingForwarders.Add(l);
                        LogUtil.Forwarders.Add(l);
                    }
                }

                await SynchronizeApplicationCommandsAsync().ConfigureAwait(false);
                var mode = Bot.Config.UseInteractionCommands ? "interaction" : "text";
                var intents = Bot.Config.UseInteractionCommands
                    ? "Guilds, GuildMessages, DirectMessages"
                    : "Guilds, GuildMessages, DirectMessages, GuildMembers, MessageContent";
                await Log(new LogMessage(LogSeverity.Info, "SysCord",
                    $"Discord ready in {mode} mode with intents [{intents}], suffix '{_commandSuffix ?? "none"}', and {_interactions.SlashCommands.Count} slash commands.")).ConfigureAwait(false);
                _firstReady.TrySetResult(true);
            }
            catch (Exception ex)
            {
                _firstReady.TrySetException(ex);
                await Log(new LogMessage(LogSeverity.Error, "SysCord", "Discord Ready initialization failed.", ex)).ConfigureAwait(false);
            }
        }

        private async Task GuildJoined(SocketGuild guild)
        {
            if (!_firstReady.Task.IsCompletedSuccessfully)
                return;

            try
            {
                await SynchronizeGuildCommandsAsync(guild).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Log(new LogMessage(LogSeverity.Error, "SysCord",
                    $"Failed to synchronize application commands for joined guild {guild.Id} ({guild.Name}).", ex)).ConfigureAwait(false);
            }
        }

        private async Task SynchronizeApplicationCommandsAsync()
        {
            await _registrationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var guild in _client.Guilds)
                    await SynchronizeGuildCommandsCoreAsync(guild).ConfigureAwait(false);
            }
            finally
            {
                _registrationGate.Release();
            }
        }

        private async Task SynchronizeGuildCommandsAsync(SocketGuild guild)
        {
            await _registrationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await SynchronizeGuildCommandsCoreAsync(guild).ConfigureAwait(false);
            }
            finally
            {
                _registrationGate.Release();
            }
        }

        private async Task SynchronizeGuildCommandsCoreAsync(SocketGuild guild)
        {
            if (Bot.Config.UseInteractionCommands && _interactions.SlashCommands.Count > MaxGuildChatInputCommands)
                throw new InvalidOperationException(
                    $"This bot exposes {_interactions.SlashCommands.Count} slash commands, but Discord allows only {MaxGuildChatInputCommands} guild commands per application. " +
                    "Use one Discord application per island or consolidate commands into subcommands.");

            var properties = Bot.Config.UseInteractionCommands
                ? _interactions.SlashCommands
                    .Select(command => BuildCommand(command, GetSlashCommandName(command.Name)))
                    .ToArray()
                : Array.Empty<SlashCommandProperties>();

            await guild.BulkOverwriteApplicationCommandAsync(properties).ConfigureAwait(false);

            await Log(new LogMessage(LogSeverity.Info, "SysCord",
                $"Synchronized {properties.Length} interaction commands for guild {guild.Id} ({guild.Name})" +
                (_commandSuffix == null ? string.Empty : $" with suffix '{_commandSuffix}'") + ".")).ConfigureAwait(false);
        }

        private void ConfigureCommandSuffix()
        {
            _commandSuffix = null;
            if (!Bot.Config.UseInteractionCommands)
                return;

            var configured = Bot.Config.SlashCommandSuffix;
            if (string.IsNullOrWhiteSpace(configured))
            {
                _commandSuffix = null;
                return;
            }

            string sanitized = string.Concat(configured.Trim().ToLowerInvariant().Select(c =>
                IsAsciiAlphaNumeric(c) ? c :
                c == ' ' ? '_' :
                c == '-' || c == '_' ? c :
                '_')).Trim('_');

            if (string.IsNullOrEmpty(sanitized))
                throw new InvalidOperationException("SlashCommandSuffix must contain at least one letter or number.");

            int maxCommandNameLength = _interactions.SlashCommands.Max(command => command.Name.Length);
            int maxSuffixLength = 32 - 1 - maxCommandNameLength;
            if (maxSuffixLength < 1)
                throw new InvalidOperationException("The registered slash-command names leave no room for a suffix.");

            if (sanitized.Length > maxSuffixLength)
                throw new InvalidOperationException($"SlashCommandSuffix is too long after sanitizing. The maximum length is {maxSuffixLength} characters.");

            _commandSuffix = $"_{sanitized}";
        }

        private static bool IsAsciiAlphaNumeric(char c) =>
            c is >= 'a' and <= 'z' or >= '0' and <= '9';

        private static SlashCommandProperties BuildCommand(SlashCommandInfo command, string name)
        {
            var builder = new SlashCommandBuilder()
                .WithName(name)
                .WithDescription(command.Description);

            foreach (var parameter in command.Parameters)
            {
                var option = new SlashCommandOptionBuilder()
                    .WithName(parameter.Name)
                    .WithDescription(parameter.Description)
                    .WithType(parameter.DiscordOptionType ?? ApplicationCommandOptionType.String)
                    .WithRequired(parameter.IsRequired);

                if (parameter.ChannelTypes != null)
                    foreach (var channelType in parameter.ChannelTypes)
                        option.AddChannelType(channelType);
                if (parameter.MinValue.HasValue)
                    option.WithMinValue(parameter.MinValue.Value);
                if (parameter.MaxValue.HasValue)
                    option.WithMaxValue(parameter.MaxValue.Value);
                if (parameter.MinLength.HasValue)
                    option.WithMinLength(parameter.MinLength.Value);
                if (parameter.MaxLength.HasValue)
                    option.WithMaxLength(parameter.MaxLength.Value);
                if (parameter.Choices != null)
                    foreach (var choice in parameter.Choices)
                        AddChoice(option, choice.Name, choice.Value);
                if (parameter.IsAutocomplete)
                    option.WithAutocomplete(true);

                builder.AddOption(option);
            }

            return builder.Build();
        }

        private static void AddChoice(SlashCommandOptionBuilder builder, string name, object value)
        {
            switch (value)
            {
                case int intValue:
                    builder.AddChoice(name, intValue);
                    break;
                case string stringValue:
                    builder.AddChoice(name, stringValue);
                    break;
                case double doubleValue:
                    builder.AddChoice(name, doubleValue);
                    break;
                case long longValue:
                    builder.AddChoice(name, longValue);
                    break;
                case float floatValue:
                    builder.AddChoice(name, floatValue);
                    break;
            }
        }

        public async Task InitCommands()
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Always load old command modules and subscribe message handler.
            // In new mode, HandleMessageAsync only responds to bot mention prefix,
            // providing paste-compatibility without MessageContent intent.
            await _commands.AddModulesAsync(assembly, _services).ConfigureAwait(false);
            _client.MessageReceived += HandleMessageAsync;

            // Load interaction metadata in both modes so stale commands can be removed
            // when an operator switches back to text commands.
            await _interactions.AddModulesAsync(assembly, _services).ConfigureAwait(false);
            ConfigureCommandSuffix();

            _client.InteractionCreated += HandleInteractionAsync;
        }

        public async Task Disconnect()
        {
            foreach (var forwarder in _loggingForwarders)
                LogUtil.Forwarders.Remove(forwarder);
            _loggingForwarders.Clear();
            if (_client.ConnectionState == ConnectionState.Disconnected)
                return;

            try
            {
                await _client.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Discord disconnect failed: {ex.Message}", nameof(SysCord));
            }
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
                            return true;
                }

                if (channel is IMessageChannel textChannel)
                    await textChannel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch(Exception e)
            {
                if (e.StackTrace != null)
                    LogUtil.LogError($"SpeakMessage failed with:\n{e.Message}\n{e.StackTrace}", nameof(SysCord));
                else
                    LogUtil.LogError($"SpeakMessage failed with:\n{e.Message}", nameof(SysCord));
            }

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
            if (arg is not SocketUserMessage msg)
                return;

            if (msg.Author.Id == _client.CurrentUser.Id || (!Bot.Config.IgnoreAllPermissions && msg.Author.IsBot))
                return;

            if (Bot.Config.UseInteractionCommands)
            {
                // Check for bot mention (pasted command compatibility).
                int pos = 0;
                if (msg.HasMentionPrefix(_client.CurrentUser, ref pos))
                {
                    // Skip any whitespace between mention and command text
                    while (pos < msg.Content.Length && char.IsWhiteSpace(msg.Content[pos]))
                        pos++;
                    await msg.Channel.SendMessageAsync(
                        "Use Place order (guided) to build and review a list, or Place order (quick) to paste an old order command and submit directly. My order shows your queue position and progress.",
                        components: OrderPanelService.BuildComponents()).ConfigureAwait(false);
                }
                // Silently ignore all other messages in new mode
                return;
            }

            // Old mode: respond to text prefix as before
            int pos2 = 0;
            if (msg.HasStringPrefix(Bot.Config.Prefix, ref pos2))
            {
                bool handled = await TryHandleCommandAsync(msg, pos2).ConfigureAwait(false);
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
            if (msg.Attachments.Count > 0)
            {
                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        private async Task<bool> TryHandleCommandAsync(SocketUserMessage msg, int pos)
        {
            var context = new SocketCommandContext(_client, msg);

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

            var guild = msg.Channel is SocketGuildChannel g ? g.Guild.Name : "Unknown Guild";
            await Log(new LogMessage(LogSeverity.Info, "Command", $"Executing command from {guild}#{msg.Channel.Name}:@{msg.Author.Username}. Content: {msg}")).ConfigureAwait(false);
            var result = await _commands.ExecuteAsync(context, pos, _services).ConfigureAwait(false);

            if (result.Error == CommandError.UnknownCommand)
                return false;

            if (!result.IsSuccess)
                await msg.Channel.SendMessageAsync(result.ErrorReason).ConfigureAwait(false);
            return true;
        }

        private async Task HandleInteractionAsync(SocketInteraction arg)
        {
            // A shared Discord application delivers every interaction to every
            // connected bot process. Ignore interactions owned by another suffix
            // before checking permissions or acknowledging them.
            if (!OwnsInteraction(arg))
                return;

            if (!Bot.Config.UseInteractionCommands)
            {
                if (arg is SocketAutocompleteInteraction staleAutocomplete)
                    await staleAutocomplete.RespondAsync(Array.Empty<AutocompleteResult>());
                else await arg.RespondAsync("This bot is running in text-command mode. Slash commands and interactive embeds are not available.", ephemeral: true);
                return;
            }

            var ctx = new SocketInteractionContext(_client, arg);

            var mgr = Bot.Config;
            bool controlPanel = arg switch
            {
                SocketSlashCommand slash => IsCommandNameForBase(slash.Data.Name, "setup-control") || IsCommandNameForBase(slash.Data.Name, "control-panel"),
                SocketMessageComponent panelComponent => panelComponent.Data.CustomId.StartsWith("control:", StringComparison.Ordinal) || panelComponent.Data.CustomId.StartsWith("control-confirm:", StringComparison.Ordinal),
                _ => false,
            };
            bool panelAdmin = ControlPanelAccess.CanBypassRestrictions(mgr, ctx.User.Id, Owner, controlPanel);
            if (!mgr.IgnoreAllPermissions && !panelAdmin)
            {
                if (!mgr.CanUseCommandUser(ctx.User.Id))
                {
                    await RejectInteractionAsync(ctx, "You are not permitted to use this command.");
                    return;
                }
                if (!mgr.CanUseCommandChannel(ctx.Channel.Id) && ctx.User.Id != Owner && !mgr.CanUseSudo(ctx.User.Id))
                {
                    await RejectInteractionAsync(ctx, "You can't use that command here.");
                    return;
                }
            }

            Discord.Interactions.IResult result;
            if (arg is SocketAutocompleteInteraction autocomplete)
            {
                var info = _interactions.SlashCommands.FirstOrDefault(command => IsCommandNameForBase(autocomplete.Data.CommandName, command.Name));
                var parameter = info?.Parameters.FirstOrDefault(candidate => candidate.Name == autocomplete.Data.Current.Name);
                if (info != null && !(await info.CheckPreconditionsAsync(ctx, _services).ConfigureAwait(false)).IsSuccess)
                { await autocomplete.RespondAsync(Array.Empty<AutocompleteResult>()); return; }
                if (parameter?.AutocompleteHandler != null)
                {
                    await parameter.AutocompleteHandler.ExecuteAsync(ctx, autocomplete, parameter, _services).ConfigureAwait(false);
                    return;
                }
                await autocomplete.RespondAsync(Array.Empty<AutocompleteResult>()).ConfigureAwait(false);
                return;
            }
            if (arg is SocketMessageComponent component)
            {
                var pattern = InteractionRouting.FindPattern(component.Data.CustomId, _commandSuffix?[1..], _interactions.ComponentCommands.Select(command => command.Name));
                var command = _interactions.ComponentCommands.First(candidate => candidate.Name == pattern);
                InteractionRouting.BindMatches(ctx, component.Data.CustomId, command.Name);
                result = await command.ExecuteAsync(ctx, _services).ConfigureAwait(false);
            }
            else if (arg is SocketModal modal)
            {
                var pattern = InteractionRouting.FindPattern(modal.Data.CustomId, _commandSuffix?[1..], _interactions.ModalCommands.Select(command => command.Name));
                var command = _interactions.ModalCommands.First(candidate => candidate.Name == pattern);
                InteractionRouting.BindMatches(ctx, modal.Data.CustomId, command.Name);
                result = await command.ExecuteAsync(ctx, _services).ConfigureAwait(false);
            }
            else if (arg is SocketSlashCommand slashCommand)
            {
                var suffixedResult = await TryExecuteSuffixedCommandAsync(ctx, slashCommand).ConfigureAwait(false);
                result = suffixedResult ?? await _interactions.ExecuteCommandAsync(ctx, _services).ConfigureAwait(false);
            }
            else
                result = await _interactions.ExecuteCommandAsync(ctx, _services).ConfigureAwait(false);

            if (!result.IsSuccess)
                await HandleInteractionFailureAsync(ctx, result).ConfigureAwait(false);
        }

        private bool OwnsInteraction(SocketInteraction interaction)
        {
            return interaction switch
            {
                SocketSlashCommand slash => OwnsSlashCommand(slash.Data.Name),
                SocketAutocompleteInteraction autocomplete => OwnsSlashCommand(autocomplete.Data.CommandName),
                SocketMessageComponent component => OwnsCustomId(component.Data.CustomId),
                SocketModal modal => OwnsCustomId(modal.Data.CustomId),
                _ => true,
            };
        }

        private static Task RejectInteractionAsync(SocketInteractionContext context, string message) =>
            context.Interaction is SocketAutocompleteInteraction autocomplete
                ? autocomplete.RespondAsync(Array.Empty<AutocompleteResult>())
                : context.Interaction.RespondAsync(message, ephemeral: true);

        private bool OwnsSlashCommand(string commandName) => _interactions.SlashCommands.Any(command =>
            IsCommandNameForBase(commandName, command.Name));

        private bool OwnsCustomId(string customId)
        {
            return InteractionRouting.OwnsCustomId(customId, _commandSuffix?[1..],
                _interactions.ComponentCommands.Select(command => command.Name).Concat(_interactions.ModalCommands.Select(command => command.Name)));
        }

        private async Task<Discord.Interactions.IResult?> TryExecuteSuffixedCommandAsync(SocketInteractionContext ctx, SocketSlashCommand command)
        {
            string fullName = command.Data.Name;
            var commandInfo = _interactions.SlashCommands.FirstOrDefault(candidate =>
                IsCommandNameForBase(fullName, candidate.Name));

            if (commandInfo == null)
                return null;

            // Let InteractionService handle the normal unsuffixed path so its
            // standard routing and module events remain unchanged.
            if (_commandSuffix == null && fullName.Equals(commandInfo.Name, StringComparison.OrdinalIgnoreCase))
                return null;

            return await commandInfo.ExecuteAsync(ctx, _services).ConfigureAwait(false);
        }

        private bool IsCommandNameForBase(string commandName, string baseName) =>
            commandName.Equals(GetSlashCommandName(baseName), StringComparison.OrdinalIgnoreCase);

        private static async Task HandleInteractionFailureAsync(SocketInteractionContext context, Discord.Interactions.IResult result)
        {
            var message = result.Error == InteractionCommandError.Exception
                ? "The command failed unexpectedly. The bot owner can check the logs for details."
                : result.ErrorReason;

            LogUtil.LogError($"Interaction failed: {result.Error}: {result.ErrorReason}", nameof(SysCord));
            try
            {
                if (context.Interaction.HasResponded)
                    await context.Interaction.ModifyOriginalResponseAsync(properties => properties.Content = message).ConfigureAwait(false);
                else
                    await context.Interaction.RespondAsync(message, ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception responseError)
            {
                LogUtil.LogError($"Could not report the interaction failure: {responseError.Message}", nameof(SysCord));
            }
        }

        private async Task MonitorStatusAsync(CancellationToken token)
        {
            const int Interval = 20;
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

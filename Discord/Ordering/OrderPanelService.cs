using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    internal sealed record OrderPanelLocation(ulong GuildId, ulong ChannelId, ulong MessageId);
    internal sealed record OrderPanelState(string Town, bool Available, int Waiting, int MaxQueueCount, bool AllowLookup, OrderStage? ActiveStage);

    internal static class OrderPanelService
    {
        private const string PathName = "OrderData/panels.json";
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static List<OrderPanelLocation>? _locations;

        private static OrderPanelState State()
        {
            var config = Globals.Bot.Config;
            bool available = config.AcceptingCommands && !config.SkipConsoleBotCreation && !config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Globals.ConsoleControl?.CanAcceptOrders != false;
            return new(Globals.Bot.TownName, available, Globals.Hub.Orders.Count, config.OrderConfig.MaxQueueCount,
                config.AllowLookup, OrderStatusStore.Shared.ActiveStage);
        }

        internal static Embed BuildEmbed() => BuildEmbed(State());

        internal static Embed BuildEmbed(OrderPanelState status)
        {
            string state = !status.Available ? "Orders paused" : status.Waiting >= status.MaxQueueCount ? "Queue full" : "Orders open";
            string current = status.ActiveStage switch
            {
                OrderStage.Preparing => "Preparing island",
                OrderStage.Ready => "Waiting for arrival",
                OrderStage.Visiting => "Visitor on island",
                null => "None",
                _ => "Finishing order"
            };
            return new EmbedBuilder().WithTitle($"Order from {status.Town}")
                .WithDescription($"**{state}: {status.Waiting} people waiting**\nCurrent order: **{current}**\n\n**Place order (guided):** choose items and review before confirming.\n**Place order (quick):** paste an order or ordercat command and submit directly to the queue.\nOnly you can see your order replies. **My order** shows your position and progress.\nYou will receive arrival instructions by DM.")
                .WithColor(status.Available ? Color.Green : Color.Orange)
                .WithFooter("All stackable items use full stacks. Quick orders skip the review step.").Build();
        }

        internal static MessageComponent BuildComponents() => BuildComponents(Globals.Bot.Config.AllowLookup, Globals.Self.GetInteractionCustomId);

        internal static MessageComponent BuildComponents(bool allowLookup, Func<string, string> id) => new ComponentBuilder()
            .WithButton("Find items", id("shop:search:home:0"), ButtonStyle.Primary, disabled: !allowLookup)
            .WithButton("Place order (guided)", id("shop:order:home:0"), ButtonStyle.Success)
            .WithButton("Place order (quick)", id("shop:quick:home:0"), ButtonStyle.Primary)
            .WithButton("My order", id("shop:my:home:0"))
            .WithButton("Help", id("shop:help:home:0")).Build();

        private static List<OrderPanelLocation> Locations()
        {
            if (_locations != null) return _locations;
            _locations = ReadLocations(PathName, message => LogUtil.LogError(message, nameof(OrderPanelService)));
            return _locations;
        }

        internal static List<OrderPanelLocation> ReadLocations(string path, Action<string> log)
        {
            if (!File.Exists(path))
            {
                log($"No saved order panels found at {Path.GetFullPath(path)}. Run /setup-embed in the order channel, or restore OrderData/panels.json from the bot's previous working folder.");
                return new();
            }
            try
            {
                var locations = JsonSerializer.Deserialize<List<OrderPanelLocation>>(File.ReadAllText(path));
                if (locations == null || locations.Any(panel => panel == null || panel.GuildId == 0 || panel.ChannelId == 0 || panel.MessageId == 0))
                    throw new JsonException("Saved panel locations contain missing IDs.");
                return locations;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            { log($"Could not read saved order panels at {Path.GetFullPath(path)}: {ex.Message}. Restore the file or run /setup-embed again."); return new(); }
        }

        internal static async Task<IUserMessage> SetupAsync(ITextChannel channel)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var locations = Locations();
                var existing = locations.FirstOrDefault(location => location.ChannelId == channel.Id);
                var message = existing == null ? null : await channel.GetMessageAsync(existing.MessageId).ConfigureAwait(false) as IUserMessage;
                if (message == null)
                    message = await channel.SendMessageAsync(embed: BuildEmbed(), components: BuildComponents()).ConfigureAwait(false);
                else await message.ModifyAsync(properties => { properties.Embed = BuildEmbed(); properties.Components = BuildComponents(); }).ConfigureAwait(false);
                locations.RemoveAll(location => location.ChannelId == channel.Id);
                locations.Add(new(channel.GuildId, channel.Id, message.Id));
                Directory.CreateDirectory("OrderData");
                File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(locations));
                File.Move(PathName + ".tmp", PathName, true);
                return message;
            }
            finally { Gate.Release(); }
        }

        internal static async Task<bool> RefreshPanelAsync(OrderPanelLocation panel,
            Func<ulong, RequestOptions, Task<ITextChannel?>> findChannel, Embed embed, MessageComponent components,
            Action<string> log, CancellationToken token)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var options = new RequestOptions { CancelToken = timeout.Token };
                var channel = await findChannel(panel.ChannelId, options).ConfigureAwait(false);
                if (channel == null)
                {
                    log($"Could not find order panel channel {panel.ChannelId}. Check View Channel permission; the refresh will be retried.");
                    return false;
                }
                if (await channel.GetMessageAsync(panel.MessageId, CacheMode.AllowDownload, options).ConfigureAwait(false) is not IUserMessage message)
                {
                    log($"Could not find saved order panel message {panel.MessageId} in channel {panel.ChannelId}. Check Read Message History permission or run /setup-embed if the message was deleted.");
                    return false;
                }
                await message.ModifyAsync(properties =>
                { properties.Embed = embed; properties.Components = components; properties.AllowedMentions = AllowedMentions.None; }, options).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            { log($"Could not refresh order panel message {panel.MessageId} in channel {panel.ChannelId}: {ex.Message}. The refresh will be retried."); return false; }
        }

        internal static async Task MonitorAsync(DiscordSocketClient client, CancellationToken token)
        {
            OrderPanelState? lastState = null;
            while (!token.IsCancellationRequested)
            {
                var config = Globals.Bot.Config;
                if (!config.UseInteractionCommands || client.ConnectionState != ConnectionState.Connected) lastState = null;
                else
                {
                    var state = State();
                    if (state != lastState)
                    {
                        await Gate.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            bool failed = false;
                            var embed = BuildEmbed(state);
                            var components = BuildComponents(state.AllowLookup, Globals.Self.GetInteractionCustomId);
                            foreach (var location in Locations().ToArray())
                            {
                                if (!await RefreshPanelAsync(location,
                                    async (channelId, options) => await client.GetChannelAsync(channelId, options).ConfigureAwait(false) as ITextChannel,
                                    embed, components, message => LogUtil.LogError(message, nameof(OrderPanelService)), token).ConfigureAwait(false)) failed = true;
                            }
                            if (!failed) lastState = state;
                        }
                        finally { Gate.Release(); }
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            }
        }
    }
}

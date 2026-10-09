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

    internal static class OrderPanelService
    {
        private const string PathName = "OrderData/panels.json";
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static List<OrderPanelLocation>? _locations;

        internal static Embed BuildEmbed()
        {
            var config = Globals.Bot.Config;
            bool available = config.AcceptingCommands && !config.SkipConsoleBotCreation && !config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Globals.ConsoleControl?.CanAcceptOrders != false;
            var count = Globals.Hub.Orders.Count;
            string state = !available ? "Orders paused" : count >= config.OrderConfig.MaxQueueCount ? "Queue full" : "Orders open";
            return new EmbedBuilder().WithTitle($"Order from {Globals.Bot.TownName}")
                .WithDescription($"**{state}: {count} people waiting**\n\n**Place order (guided):** choose items and review before confirming.\n**Place order (quick):** paste an order or ordercat command and submit directly to the queue.\nOnly you can see your order replies. **My order** shows your position and progress.\nYou will receive arrival instructions by DM.")
                .WithColor(available ? Color.Green : Color.Orange)
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
            try { _locations = File.Exists(PathName) ? JsonSerializer.Deserialize<List<OrderPanelLocation>>(File.ReadAllText(PathName)) ?? new() : new(); }
            catch (Exception ex) when (ex is IOException or JsonException)
            { LogUtil.LogError($"Could not read saved order panels: {ex.Message}", nameof(OrderPanelService)); _locations = new(); }
            return _locations;
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

        internal static async Task MonitorAsync(DiscordSocketClient client, CancellationToken token)
        {
            string? lastState = null;
            while (!token.IsCancellationRequested)
            {
                var config = Globals.Bot.Config;
                var state = $"{config.UseInteractionCommands}|{config.AcceptingCommands}|{config.AllowLookup}|{config.SkipConsoleBotCreation}|{config.DodoModeConfig.LimitedDodoRestoreOnlyMode}|{Globals.Hub.Orders.Count}|{config.OrderConfig.MaxQueueCount}|{Globals.Bot.TownName}|{Globals.ConsoleControl?.Stage}";
                if (state != lastState && config.UseInteractionCommands && client.ConnectionState == ConnectionState.Connected)
                {
                    await Gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        bool failed = false;
                        foreach (var location in Locations().ToArray())
                        {
                            try
                            {
                                if (client.GetChannel(location.ChannelId) is not ITextChannel channel) continue;
                                if (await channel.GetMessageAsync(location.MessageId).ConfigureAwait(false) is IUserMessage message)
                                    await message.ModifyAsync(properties => { properties.Embed = BuildEmbed(); properties.Components = BuildComponents(); }).ConfigureAwait(false);
                            }
                            catch (Exception ex) { failed = true; LogUtil.LogError($"Could not refresh order panel {location.ChannelId}: {ex.Message}", nameof(OrderPanelService)); }
                        }
                        if (!failed) lastState = state;
                    }
                    finally { Gate.Release(); }
                }
                await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            }
        }
    }
}

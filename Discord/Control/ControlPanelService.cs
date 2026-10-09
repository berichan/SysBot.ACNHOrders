using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    internal sealed record ControlPanelLocation(ulong GuildId, ulong ChannelId, ulong MessageId);

    internal static class ControlPanelService
    {
        private const string PathName = "OrderData/control-panels.json";
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static List<ControlPanelLocation>? _locations;

        internal static ControlPanelStatus Status(ConnectionState discord)
        {
            var bot = Globals.Bot;
            var stage = Globals.ConsoleControl?.Stage ?? ConsoleBotStage.Disabled;
            bool connected = bot.Connection.Connected && stage is ConsoleBotStage.Running or ConsoleBotStage.Starting or ConsoleBotStage.Stopping;
            return new(bot.TownName, stage, connected, discord, bot.Config.AcceptingCommands && Globals.ConsoleControl?.CanAcceptOrders == true,
                bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode, Globals.Hub.Orders.Count,
                OrderStatusStore.Shared.HasActiveOrder, bot.LastScreenCommand, bot.HasChargeReading ? bot.ChargePercent : null,
                bot.Config.DodoModeConfig.MashB, bot.Config.DodoModeConfig.RefreshMap);
        }

        private static List<ControlPanelLocation> Locations()
        {
            if (_locations != null) return _locations;
            try { _locations = File.Exists(PathName) ? JsonSerializer.Deserialize<List<ControlPanelLocation>>(File.ReadAllText(PathName)) ?? new() : new(); }
            catch (Exception ex) when (ex is IOException or JsonException)
            { LogUtil.LogError($"Could not read control panel locations: {ex.Message}", nameof(ControlPanelService)); _locations = new(); }
            return _locations;
        }

        internal static async Task<IUserMessage> SetupAsync(ITextChannel channel, ConnectionState discord)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var locations = Locations();
                var saved = locations.Find(panel => panel.ChannelId == channel.Id);
                var message = saved == null ? null : await channel.GetMessageAsync(saved.MessageId).ConfigureAwait(false) as IUserMessage;
                var status = Status(discord);
                var embed = ControlPanelView.BuildEmbed(status);
                var components = ControlPanelView.BuildComponents(status, Globals.Self.GetInteractionCustomId);
                if (message == null) message = await channel.SendMessageAsync(embed: embed, components: components).ConfigureAwait(false);
                else await message.ModifyAsync(p => { p.Embed = embed; p.Components = components; p.AllowedMentions = AllowedMentions.None; }).ConfigureAwait(false);
                locations.RemoveAll(panel => panel.ChannelId == channel.Id);
                locations.Add(new(channel.GuildId, channel.Id, message.Id));
                Directory.CreateDirectory("OrderData");
                File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(locations));
                File.Move(PathName + ".tmp", PathName, true);
                return message;
            }
            finally { Gate.Release(); }
        }

        internal static async Task RefreshAsync(DiscordSocketClient client, ConnectionState? discord = null, CancellationToken token = default)
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var status = Status(discord ?? client.ConnectionState);
                foreach (var panel in Locations())
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        var options = new RequestOptions { CancelToken = timeout.Token };
                        if (client.GetChannel(panel.ChannelId) is not ITextChannel channel) continue;
                        if (await channel.GetMessageAsync(panel.MessageId, CacheMode.AllowDownload, options).ConfigureAwait(false) is IUserMessage message)
                            await message.ModifyAsync(p =>
                            {
                                p.Embed = ControlPanelView.BuildEmbed(status);
                                p.Components = ControlPanelView.BuildComponents(status, Globals.Self.GetInteractionCustomId);
                                p.AllowedMentions = AllowedMentions.None;
                            }, options).ConfigureAwait(false);
                    }
                    catch (Exception ex) { LogUtil.LogError($"Could not update control panel {panel.ChannelId}: {ex.Message}", nameof(ControlPanelService)); }
                }
            }
            finally { Gate.Release(); }
        }

        internal static async Task MonitorAsync(DiscordSocketClient client, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (Globals.Bot.Config.UseInteractionCommands && client.ConnectionState == ConnectionState.Connected)
                    await RefreshAsync(client, token: token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            }
        }
    }
}

using System;
using Discord;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public sealed record ControlPanelStatus(string Town, ConsoleBotStage Stage, bool SwitchConnected,
        ConnectionState Discord, bool Accepting, bool RestoreMode, int Waiting, bool Active,
        bool? Screen, int? Charge, bool MashB, bool RefreshMap);

    public static class ControlPanelView
    {
        public static Embed BuildEmbed(ControlPanelStatus status)
        {
            string loop = status.Stage switch
            {
                ConsoleBotStage.Disabled => "Disabled in configuration",
                ConsoleBotStage.Stopping => "Stopping after the current order",
                ConsoleBotStage.Faulted => "Stopped after an error, check the logs",
                _ => status.Stage.ToString()
            };
            return new EmbedBuilder().WithTitle($"Control panel: {status.Town}")
                .WithDescription("Sudo users and the application owner can use these controls. Changes apply to this running bot.")
                .WithColor(status.SwitchConnected && status.Discord == ConnectionState.Connected ? Color.Green : Color.Orange)
                .AddField("Connections", $"Discord: **{status.Discord}**\nSwitch: **{(status.SwitchConnected ? "Connected" : "Disconnected")}**\nBot: {loop}")
                .AddField("Requests", $"{(status.Accepting ? "Open" : "Paused")}\nMode: {(status.RestoreMode ? "Dodo restore" : "Orders")}\nWaiting: {status.Waiting}\nCurrent order: {(status.Active ? "In progress" : "None")}", true)
                .AddField("Console", $"Last screen command: {(status.Screen.HasValue ? status.Screen.Value ? "On" : "Off" : "Not checked")}\nLast battery reading: {(status.Charge.HasValue ? status.Charge + "%" : "Not checked")}", true)
                .AddField("Restore mode", $"Mash B: {(status.MashB ? "On" : "Off")}\nMap refresh: {(status.RefreshMap ? "On" : "Off")}")
                .WithCurrentTimestamp().WithFooter("Updates about every 15 seconds. If updates stop, the bot may be offline. Screen and battery values are the last recorded readings.").Build();
        }

        public static MessageComponent BuildComponents(ControlPanelStatus status, Func<string, string> id)
        {
            bool disabled = status.Stage == ConsoleBotStage.Disabled;
            bool stopping = status.Stage == ConsoleBotStage.Stopping;
            bool stopped = status.Stage is ConsoleBotStage.Stopped or ConsoleBotStage.Faulted or ConsoleBotStage.Disabled;
            bool consoleReady = status.SwitchConnected && status.Stage == ConsoleBotStage.Running;
            return new ComponentBuilder()
                .WithButton("Refresh status", id("control:status"), ButtonStyle.Primary)
                .WithButton("Start bot", id("control:start"), disabled: disabled || stopping || !stopped)
                .WithButton("Stop bot", id("control:stop"), ButtonStyle.Danger, disabled: disabled || stopping || stopped)
                .WithButton("Restart connection", id("control:restart"), disabled: disabled || stopping)
                .WithButton(status.Accepting ? "Pause requests" : "Resume requests", id(status.Accepting ? "control:pause" : "control:resume"), disabled: !status.Accepting && (stopping || stopped), row: 1)
                .WithButton("Screen on", id("control:screen-on"), disabled: !consoleReady, row: 1)
                .WithButton("Screen off", id("control:screen-off"), disabled: !consoleReady, row: 1)
                .WithButton("Detach controller", id("control:detach"), disabled: !consoleReady, row: 1)
                .WithButton("View queue", id("control:queue"), row: 2)
                .WithButton("New Dodo code", id("control:newdodo"), disabled: !status.RestoreMode || !consoleReady, row: 2)
                .WithButton(status.MashB ? "Mash B off" : "Mash B on", id(status.MashB ? "control:mash-off" : "control:mash-on"), disabled: !status.RestoreMode, row: 2)
                .WithButton(status.RefreshMap ? "Map refresh off" : "Map refresh on", id(status.RefreshMap ? "control:map-off" : "control:map-on"), disabled: !status.RestoreMode, row: 2)
                .WithButton("Guide", id("control:guide"), row: 3).Build();
        }
    }
}

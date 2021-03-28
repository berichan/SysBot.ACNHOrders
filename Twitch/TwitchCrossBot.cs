using NHSE.Core;
using SysBot.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Events;
using TwitchLib.Communication.Models;

namespace SysBot.ACNHOrders.Twitch
{
    public class TwitchCrossBot
    {
        internal static CrossBot Bot = default!;
        internal static string BotName = default!;
        internal static readonly List<TwitchQueue> QueuePool = new();
        private readonly TwitchClient client;
        private readonly string Channel;
        private readonly TwitchConfig Settings;

        public TwitchCrossBot(TwitchConfig settings, CrossBot bot)
        {
            Settings = settings;
            Bot = bot;
            BotName = settings.Username;

            var credentials = new ConnectionCredentials(settings.Username.ToLower(), settings.Token);

            var clientOptions = new ClientOptions
            {
                MessagesAllowedInPeriod = settings.ThrottleMessages,
                ThrottlingPeriod = TimeSpan.FromSeconds(settings.ThrottleSeconds),

                WhispersAllowedInPeriod = settings.ThrottleWhispers,
                WhisperThrottlingPeriod = TimeSpan.FromSeconds(settings.ThrottleWhispersSeconds),

                // message queue capacity is managed (10_000 for message & whisper separately)
                // message send interval is managed (50ms for each message sent)
            };

            var lowerKeyDic = new Dictionary<string, string>();
            foreach (var kvp in settings.UserDefinitedCommands)
                lowerKeyDic.Add(kvp.Key.ToLower(), kvp.Value);
            foreach (var kvp in settings.UserDefinedSubOnlyCommands)
                lowerKeyDic.Add(kvp.Key.ToLower(), kvp.Value);
            settings.UserDefinitedCommands = lowerKeyDic;

            Channel = settings.Channel;
            WebSocketClient customClient = new(clientOptions);
            client = new TwitchClient(customClient);

            var cmd = settings.CommandPrefix;
            client.Initialize(credentials, Channel, cmd, cmd);

            client.OnLog += Client_OnLog;
            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnWhisperReceived += Client_OnWhisperReceived;
            client.OnChatCommandReceived += Client_OnChatCommandReceived;
            client.OnWhisperCommandReceived += Client_OnWhisperCommandReceived;
            client.OnConnected += Client_OnConnected;
            client.OnDisconnected += Client_OnDisconnected;
            client.OnLeftChannel += Client_OnLeftChannel;

            client.OnMessageSent += (_, e)
                => LogUtil.LogText($"[{client.TwitchUsername}] - Message Sent in {e.SentMessage.Channel}: {e.SentMessage.Message}");
            client.OnWhisperSent += (_, e)
                => LogUtil.LogText($"[{client.TwitchUsername}] - Whisper Sent to @{e.Receiver}: {e.Message}");

            client.OnMessageThrottled += (_, e)
                => LogUtil.LogError($"Message Throttled: {e.Message}", "TwitchBot");
            client.OnWhisperThrottled += (_, e)
                => LogUtil.LogError($"Whisper Throttled: {e.Message}", "TwitchBot");

            client.OnError += (_, e) =>
                LogUtil.LogError(e.Exception.Message + Environment.NewLine + e.Exception.StackTrace, "TwitchBot");
            client.OnConnectionError += (_, e) =>
                LogUtil.LogError(e.BotUsername + Environment.NewLine + e.Error.Message, "TwitchBot");

            client.Connect();

            EchoUtil.Forwarders.Add(msg => client.SendMessage(Channel, msg));

            // Turn on if verified
            // Hub.Queues.Forwarders.Add((bot, detail) => client.SendMessage(Channel, $"{bot.Connection.Name} is now trading (ID {detail.ID}) {detail.Trainer.TrainerName}"));
        }

        private void Client_OnLog(object? sender, OnLogArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] -[{e.BotUsername}] {e.Data}");
        }

        private void Client_OnConnected(object? sender, OnConnectedArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] - Connected {e.AutoJoinChannel} as {e.BotUsername}");
        }

        private void Client_OnDisconnected(object? sender, OnDisconnectedEventArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] - Disconnected.");
            while (!client.IsConnected)
                client.Reconnect();
        }

        private void Client_OnJoinedChannel(object? sender, OnJoinedChannelArgs e)
        {
            LogUtil.LogInfo($"Joined {e.Channel}", e.BotUsername);
            client.SendMessage(e.Channel, "Connected!");
        }

        private void Client_OnMessageReceived(object? sender, OnMessageReceivedArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] - Received message: @{e.ChatMessage.Username}: {e.ChatMessage.Message}");
            if (client.JoinedChannels.Count == 0)
                client.JoinChannel(e.ChatMessage.Channel);
        }

        private void Client_OnLeftChannel(object? sender, OnLeftChannelArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] - Left channel {e.Channel}");
            client.JoinChannel(e.Channel);
        }

        private void Client_OnChatCommandReceived(object? sender, OnChatCommandReceivedArgs e)
        {
            if (!Settings.AllowCommandsViaChannel || Settings.UserBlacklist.Contains(e.Command.ChatMessage.Username))
                return;

            var msg = e.Command.ChatMessage;
            var c = e.Command.CommandText.ToLower();
            var args = e.Command.ArgumentsAsString;
            var response = HandleCommand(msg, c, args, false, out var dest);
            if (response.Length == 0)
                return;

            if (dest == TwitchMessageDestination.Whisper)
            {
                client.SendWhisper(msg.Username, response);
            }
            else
            {
                var channel = e.Command.ChatMessage.Channel;
                client.SendMessage(channel, response);
            }
        }

        private void Client_OnWhisperCommandReceived(object? sender, OnWhisperCommandReceivedArgs e)
        {
            if (!Settings.AllowCommandsViaWhisper || Settings.UserBlacklist.Contains(e.Command.WhisperMessage.Username))
                return;

            var msg = e.Command.WhisperMessage;
            var c = e.Command.CommandText.ToLower();
            var args = e.Command.ArgumentsAsString;
            var response = HandleCommand(msg, c, args, true, out _);
            if (response.Length == 0)
                return;

            client.SendWhisper(msg.Username, response);
        }

        private string HandleCommand(TwitchLibMessage m, string c, string args, bool whisper, out TwitchMessageDestination dest)
        {
            bool sudo() => m is ChatMessage ch && (ch.IsBroadcaster || Settings.IsSudo(m.Username));
            bool subscriber() => m is ChatMessage { IsSubscriber: true };
            LogUtil.LogInfo($"[Command] {m.Username}: {c} {args}", nameof(TwitchCrossBot));

            dest = TwitchMessageDestination.Disabled; // disable overwrite so responds in same area.

            // user-defined
            if (Settings.UserDefinedSubOnlyCommands.ContainsKey(c.ToLower()))
            {
                if (subscriber())
                {
                    dest = Settings.UserDefinedSubOnlyCommandsDestination;
                    return ReplacePredefined(Settings.UserDefinedSubOnlyCommands[c], m.Username);
                }
                else
                    return $"@{m.Username} - You must be a subscriber to use this command.";
            }
            if (Settings.UserDefinitedCommands.ContainsKey(c.ToLower()))
            {
                dest = Settings.UserDefinedCommandsDestination;
                return ReplacePredefined(Settings.UserDefinitedCommands[c], m.Username);
            }

            switch (c)
            {
                // User Usable Commands
                case "order":
                    var _ = TwitchHelper.AddToWaitingList(args, m.DisplayName, m.Username, ulong.Parse(m.UserId), subscriber(), false, out string msg);
                    return msg;
                case "ordercat":
                    var _1 = TwitchHelper.AddToWaitingList(args, m.DisplayName, m.Username, ulong.Parse(m.UserId), subscriber(), true, out string msg1);
                    return msg1;
                case "preset":
                    var _2 = TwitchHelper.AddToWaitingListPreset(args, m.DisplayName, m.Username, ulong.Parse(m.UserId), subscriber(), out string msg2);
                    return msg2;
                case "presets":
                    return TwitchHelper.GetPresets(Settings.CommandPrefix);
                case "drop":
                    return $"@{m.Username}: {TwitchHelper.Drop(args, ulong.Parse(m.UserId), m.Username, Settings)}";
                case "clean":
                    return $"@{m.Username}: {TwitchHelper.Clean(ulong.Parse(m.UserId), m.Username, Settings)}";
                case "ts":
                case "pos":
                case "position":
                case "time":
                case "eta":
                    return $"@{m.Username}: {TwitchHelper.GetPosition(ulong.Parse(m.UserId))}";
                case "tc":
                case "remove":
                case "delete":
                case "qc":
                    return $"@{m.Username}: {TwitchHelper.ClearTrade(ulong.Parse(m.UserId))}";
                case "ping":
                    return $"@{m.Username}: pong!";

                // Sudo Only Commands
                case "toggledrop" when !sudo():
                case "tcu" when !sudo():
                    return "This command is locked for sudo users only!";

                case "tcu":
                    return TwitchHelper.ClearTrade(args);
                case "toggledrop":
                    Settings.AllowDropViaTwitchChat = !Settings.AllowDropViaTwitchChat;
                    return Settings.AllowDropViaTwitchChat ? "I am now accepting drop commands!" : "I am no longer accepting drop commands!";

                default: return string.Empty;
            }
        }

        private bool AddToTradeQueue(TwitchQueue queueItem, string pass, out string msg)
        {
            if (int.TryParse(pass, out var ps))
            {
                var twitchRequest = new TwitchOrderRequest<Item>(queueItem.ItemReq.ToArray(), queueItem.ID, QueueExtensions.GetNextID(), queueItem.DisplayName, queueItem.DisplayName, client, Channel, Settings, ps, queueItem.VillagerReq);
                var result = QueueExtensions.AddToQueueSync(twitchRequest, queueItem.DisplayName, queueItem.DisplayName, out var msge);
                msg = TwitchOrderRequest<Item>.SanitizeForTwitch(msge);
                return result;
            }

            msg = $"@{queueItem.DisplayName} - Your 3-digit number was invalid. Order has been removed, please start over.";
            return false;
        }

        private void Client_OnWhisperReceived(object? sender, OnWhisperReceivedArgs e)
        {
            LogUtil.LogInfo($"[{client.TwitchUsername}] - @{e.WhisperMessage.Username}: {e.WhisperMessage.Message}", nameof(TwitchCrossBot));
            if (QueuePool.Count > 100)
            {
                var removed = QueuePool[0];
                QueuePool.RemoveAt(0); // First in, first out
                client.SendMessage(Channel, $"Removed @{removed.DisplayName} from the waiting list: stale request.");
            }

            var queueItem = QueuePool.FindLast(q => q.ID == ulong.Parse(e.WhisperMessage.UserId));
            if (queueItem == null)
            {
                LogUtil.LogInfo($"No queue item found, returning...", nameof(TwitchCrossBot));
                return;
            }
            QueuePool.Remove(queueItem);
            var msg = e.WhisperMessage.Message;
            try
            {
                var _ = AddToTradeQueue(queueItem, msg, out string message);
                client.SendMessage(Channel, message);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                LogUtil.LogError($"{ex.Message}", nameof(TwitchCrossBot));
            }
        }

        private static string ReplacePredefined(string message, string caller)
        {
            return message.Replace("{islandname}", Bot.TownName)
                .Replace("{dodo}", Bot.DodoCode)
                .Replace("{vcount}", Math.Min(0, Bot.VisitorList.VisitorCount - 1).ToString())
                .Replace("{visitorlist}", Bot.VisitorList.VisitorFormattedString)
                .Replace("{villagerlist}", Bot.Villagers.LastVillagers)
                .Replace("{user}", caller);
        }
    }
}

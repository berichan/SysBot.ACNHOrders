using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.ACNHOrders.Twitch
{
    public static class TwitchHelper
    {
        // Helper functions for commands
        public static bool AddToWaitingList(string orderString, string display, string username, ulong id, bool sub, bool cat, out string msg)
        {
            if (!IsQueueable(orderString, id, out var msge))
            {
                msg = $"@{username} - {msge}";
                return false;
            }

            try
            {
                var cfg = Globals.Bot.Config;
                VillagerRequest? vr = null;

                // try get villager
                var result = VillagerOrderParser.ExtractVillagerName(orderString, out var res, out var san);
                if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
                {
                    msg = $"@{username} - {res} Order has not been accepted.";
                    return false;
                }

                if (result == VillagerOrderParser.VillagerRequestResult.Success)
                {
                    if (!cfg.AllowVillagerInjection)
                    {
                        msg = $"@{username} - Villager injection is currently disabled.";
                        return false;
                    }

                    orderString = san;
                    var replace = VillagerResources.GetVillager(res);
                    vr = new VillagerRequest(username, replace, 0, GameInfo.Strings.GetVillager(res));
                }

                var items = string.IsNullOrWhiteSpace(orderString) ? new Item[1] { new Item(Item.NONE) } : ItemParser.GetItemsFromUserInput(orderString, cfg.DropConfig, ItemDestination.FieldItemDropped);

                return InsertToQueue(items, vr, display, username, id, sub, cat, out msg);
            }
            catch (Exception e) 
            { 
                LogUtil.LogError($"{username}@{orderString}: {e.Message}", nameof(TwitchHelper)); 
                msg = $"@{username} {e.Message}";
                return false;
            }
        }

        public static bool AddToWaitingListPreset(string presetName, string display, string username, ulong id, bool sub, out string msg)
        {
            if (!IsQueueable(presetName, id, out var msge))
            {
                msg = $"@{username} - {msge}";
                return false;
            }

            try
            {
                var cfg = Globals.Bot.Config;
                VillagerRequest? vr = null;

                // try get villager
                var result = VillagerOrderParser.ExtractVillagerName(presetName, out var res, out var san);
                if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
                {
                    msg = $"@{username} - {res} Order has not been accepted.";
                    return false;
                }

                if (result == VillagerOrderParser.VillagerRequestResult.Success)
                {
                    if (!cfg.AllowVillagerInjection)
                    {
                        msg = $"@{username} - Villager injection is currently disabled.";
                        return false;
                    }

                    presetName = san;
                    var replace = VillagerResources.GetVillager(res);
                    vr = new VillagerRequest(username, replace, 0, GameInfo.Strings.GetVillager(res));
                }

                presetName = presetName.Trim();
                var preset = PresetLoader.GetPreset(cfg.OrderConfig, presetName);
                if (preset == null)
                {
                    msg = $"{username} - {presetName} is not a valid preset.";
                    return false;
                }

                return InsertToQueue(preset, vr, display, username, id, sub, true, out msg);
            }
            catch (Exception e)
            {
                LogUtil.LogError($"{username}@Preset:{presetName}: {e.Message}", nameof(TwitchHelper));
                msg = $"@{username} {e.Message}";
                return false;
            }
        }

        public static string ClearTrade(ulong userID)
        {
            QueueExtensions.GetPosition(userID, out var order);
            if (order == null)
                return "Sorry, you are not in the queue, or your order is happening now.";

            order.SkipRequested = true;
            return "Your order has been removed. Please note that you will not be able to rejoin the queue again for a while.";
        }

        public static string ClearTrade(string userID)
        {
            if (!ulong.TryParse(userID, out var usrID))
                return $"{userID} is not a valid u64.";

            return ClearTrade(userID);
        }

        public static string GetPosition(ulong userID)
        {
            var position = QueueExtensions.GetPosition(userID, out var order);
            if (order == null)
                return "Sorry, you are not in the queue, or your order is happening now.";

            var message = $"You are in the order queue. Position: {position}.";
            if (position > 1)
                message += $" Your predicted ETA is {QueueExtensions.GetETA(position)}.";

            return message;
        }

        public static string GetPresets(char prefix)
        {
            var presets = PresetLoader.GetPresets(Globals.Bot.Config.OrderConfig);

            if (presets.Length < 1)
                return "There are not presets available";
            else
                return $"The following presets are available: {string.Join(", ", presets)}. Enter {prefix}preset [preset name] to order one!";
        }

        public static string Clean(ulong id, string username, TwitchConfig tcfg)
        {
            if (!tcfg.AllowDropViaTwitchChat)
            {
                LogUtil.LogInfo($"{username} is attempting to clean items, however the twitch configuration does not currently allow drop commands", nameof(TwitchCrossBot));
                return string.Empty;
            }

            if (!GetDropAvailability(id, username, tcfg, out var error))
                return error;

            if (!Globals.Bot.Config.AllowClean)
                return "Clean functionality is currently disabled.";
            
            Globals.Bot.CleanRequested = true;
            return "A clean request will be executed momentarily.";
        }

        public static string Drop(string message, ulong id, string username, TwitchConfig tcfg)
        {
            if (!tcfg.AllowDropViaTwitchChat)
            {
                LogUtil.LogInfo($"{username} is attempting to drop items, however the twitch configuration does not currently allow drop commands", nameof(TwitchCrossBot));
                return string.Empty;
            }
            if (!GetDropAvailability(id, username, tcfg, out var error))
                return error;

            var cfg = Globals.Bot.Config;
            var items = ItemParser.GetItemsFromUserInput(message, cfg.DropConfig, cfg.DropConfig.UseLegacyDrop ? ItemDestination.PlayerDropped : ItemDestination.HeldItem);
            MultiItem.StackToMax(items);

            if (!InternalItemTool.CurrentInstance.IsSane(items, cfg.DropConfig))
                return $"You are attempting to drop items that will damage your save. Drop request not accepted.";

            var MaxRequestCount = cfg.DropConfig.MaxDropCount;
            var ret = string.Empty;
            if (items.Count > MaxRequestCount)
            {
                ret += $"Users are limited to {MaxRequestCount} items per command. Please use this bot responsibly. ";
                items = items.Take(MaxRequestCount).ToArray();
            }

            var requestInfo = new ItemRequest(username, items);
            Globals.Bot.Injections.Enqueue(requestInfo);

            ret += $"Item drop request{(requestInfo.Item.Count > 1 ? "s" : string.Empty)} will be executed momentarily.";
            return ret;
        }

        private static bool IsQueueable(string orderString, ulong id, out string msg)
        {
            if (!TwitchCrossBot.Bot.Config.AcceptingCommands || TwitchCrossBot.Bot.Config.SkipConsoleBotCreation)
            {
                msg = "Sorry, I am not currently accepting queue requests!";
                return false;
            }

            if (string.IsNullOrWhiteSpace(orderString))
            {
                msg = "No valid order text.";
                return false;
            }

            if (GlobalBan.IsBanned(id.ToString()))
            {
                msg = "You have been banned for abuse. Order has not been accepted.";
                return false;
            }

            msg = string.Empty;
            return true;
        }

        private static bool InsertToQueue(IReadOnlyCollection<Item> items, VillagerRequest? vr, string display, string username, ulong id, bool sub, bool cat, out string msg)
        {
            if (!InternalItemTool.CurrentInstance.IsSane(items, Globals.Bot.Config.DropConfig))
            {
                msg = $"@{username} - You are attempting to order items that will damage your save. Order not accepted.";
                return false;
            }

            var multiOrder = new MultiItem(items.ToArray(), cat, true, true);

            var tq = new TwitchQueue(multiOrder.ItemArray.Items, vr, display, id, sub);
            TwitchCrossBot.QueuePool.Add(tq);
            msg = $"@{username} - I've noted your order, now whisper me any random 3-digit number. Simply type /w @{TwitchCrossBot.BotName.ToLower()} [3-digit number] in this channel! Your order will not be placed in the queue until I get your whisper!";
            return true;
        }

        private static bool GetDropAvailability(ulong callerId, string callerName, TwitchConfig tcfg, out string error)
        {
            error = string.Empty;
            var cfg = Globals.Bot.Config;

            if (tcfg.IsSudo(callerName))
                return true;

            if (Globals.Bot.CurrentUserId == callerId)
                return true;

            if (!cfg.AllowDrop)
            {
                error = $"AllowDrop is currently set to false in the main config.";
                return false;
            }
            else if (!cfg.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                error = $"You are only permitted to use this command while on the island during your order, and only if you have forgotten something in your order.";
                return false;
            }

            return true;
        }
    }
}

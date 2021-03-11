using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SysBot.ACNHOrders.Twitch
{
    public static class TwitchHelper
    {
        // Helper functions for commands
        public static bool AddToWaitingList(string orderString, string display, string username, ulong id, bool sub, bool cat, out string msg)
        {
            if (!TwitchCrossBot.Bot.Config.AcceptingCommands || TwitchCrossBot.Bot.Config.SkipConsoleBotCreation)
            {
                msg = $"@{username} - Sorry, I am not currently accepting queue requests!";
                return false;
            }

            if (string.IsNullOrWhiteSpace(orderString))
            {
                msg = $"@{username} - No valid order text.";
                return false;
            }

            if (GlobalBan.IsBanned(id.ToString()))
            {
                msg = $"@{username} - You have been banned for abuse. Order has not been accepted.";
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
                var multiOrder = new MultiItem(items.ToArray(), cat, true, true);

                var tq = new TwitchQueue(multiOrder.ItemArray.Items, vr, display, id, sub);
                TwitchCrossBot.QueuePool.Add(tq);
                msg = $"@{username} - Now you must whisper me any random 3-digit number. Your order will not be placed in the queue until I get your whisper!";
                return true;
            }
            catch (Exception e) 
            { 
                LogUtil.LogError($"{username}@{orderString}: {e.Message}", nameof(TwitchHelper)); 
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
    }
}

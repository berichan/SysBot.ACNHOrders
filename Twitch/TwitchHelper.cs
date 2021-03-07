using System;
using System.Collections.Generic;
using System.Text;

namespace SysBot.ACNHOrders.Twitch
{
    public static class TwitchHelper
    {
        // Helper functions for commands
        public static bool AddToWaitingList(string setstring, string display, string username, bool sub, out string msg)
        {
            if (!TwitchCrossBot.Bot.Config.AcceptingCommands)
            {
                msg = "Sorry, I am not currently accepting queue requests!";
                return false;
            }

            msg = "An error occured";
            return false;
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

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using NHSE.Core;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace SysBot.ACNHOrders
{
    public static class QueueExtensions
    {
        const int ArriveTime = 90;
        const int SetupTime = 95;

        public static async Task AddToQueueAsync(this SocketCommandContext Context, OrderRequest<MultiItem> itemReq, string player, SocketUser trader)
        {
            IUserMessage test;
            try
            {
                const string helper = "I've added you to the queue! I'll message you here when your order is ready";
                test = await trader.SendMessageAsync(helper).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                await Context.Channel.SendMessageAsync($"{ex.HttpCode}: {ex.Reason}!").ConfigureAwait(false);
                var noAccessMsg = Context.User == trader ? "You must enable private messages in order to be queued!" : $"{player} must enable private messages in order for them to be queued!";
                await Context.Channel.SendMessageAsync(noAccessMsg).ConfigureAwait(false);
                return;
            }

            // Try adding
            var result = Context.AttemptAddToQueue(itemReq, out var msg);

            // Notify in channel
            await Context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            // Notify in PM to mirror what is said in the channel.
            await trader.SendMessageAsync(msg).ConfigureAwait(false);

            // Clean Up
            if (result)
            {
                // Delete the user's join message for privacy
                if (!Context.IsPrivate)
                    await Context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
            else
            {
                // Delete our "I'm adding you!", and send the same message that we sent to the general channel.
                await test.DeleteAsync().ConfigureAwait(false);
            }
        }

        // this sucks
        private static bool AttemptAddToQueue(this SocketCommandContext Context, OrderRequest<MultiItem> itemReq, out string msg)
        {
            var orders = Globals.Bot.Orders;
            var orderArray = orders.ToArray();
            if (Array.Find(orderArray, x => x.UserGuid == itemReq.UserGuid) != null)
            {
                msg = "Sorry, you are already in the queue.";
                return false;
            }

            var position = orderArray.Length + 1;

            msg = $"Added you to the order queue. Your position is: {position}";

            if (position > 1)
                msg += $" Your predicted ETA is {GetETA(position)}";

            Globals.Bot.Orders.Enqueue(itemReq);

            return true;
        }

        private static string GetETA(int pos)
        {
            int minSeconds = ArriveTime + SetupTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed;
            var timeSpan = TimeSpan.FromSeconds(minSeconds * pos);
            if (minSeconds > 3600)
                return string.Format("{0:D2}h:{1:D2}m:{2:D2}s", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
            else
                return string.Format("{0:D2}m:{1:D2}s", timeSpan.Minutes, timeSpan.Seconds);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Signalr
{
    public class SignalrCrossBot
    {
        internal static CrossBot Bot = default!;

        private readonly string URI;
        private readonly string AuthID, AuthString;

        private readonly IDodoRestoreNotifier WebNotifierInstance;

        public SignalrCrossBot(WebConfig settings, CrossBot bot)
        {
            Bot = bot;
            URI = settings.URIEndpoint;
            AuthID = settings.AuthID;
            AuthString = settings.AuthTokenOrString;

            WebNotifierInstance = new SignalRNotify(AuthID, AuthString, URI);
            bot.DodoNotifiers.Add(WebNotifierInstance);
        }
    }
}

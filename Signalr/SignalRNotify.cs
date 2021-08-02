using Microsoft.AspNetCore.SignalR.Client;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Signalr
{
    public class SignalRNotify : IDodoRestoreNotifier
    {
        private HubConnection Connection { get; }
        private string AuthID { get; }
        private string AuthString { get; }
        private string URI { get; }
        private bool Connected { get; set; }

        private static readonly SemaphoreSlim asyncLock = new SemaphoreSlim(1, 1);

        public SignalRNotify(string authid, string authString, string uriEndpoint)
        {
            AuthID = authid;
            AuthString = authString;
            URI = uriEndpoint;
            Connection = new HubConnectionBuilder()
                .WithUrl(URI)
                .WithAutomaticReconnect()
                .Build();

            Task.Run(AttemptConnection);
        }

        private async void AttemptConnection()
        {
            try
            {
                await Connection.StartAsync();
                LogUtil.LogInfo("Connected succesfully " + Connection.ConnectionId, "SignalR");
                Connected = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void NotifyServerOfState(GameState gs)
        {
            var paramsToSend = new Dictionary<string, string>();
            paramsToSend.Add("gs", gs.ToString().WebSafeBase64Encode());
            Task.Run(() => NotifyServerEndpoint(paramsToSend.ToArray()));
        }

        public void NotifyServerOfDodoCode(string dodo)
        {
            var paramsToSend = new Dictionary<string, string>();
            paramsToSend.Add("dodo", dodo.ToString().WebSafeBase64Encode());
            Task.Run(() => NotifyServerEndpoint(paramsToSend.ToArray()));
        }

        private async void NotifyServerEndpoint(params KeyValuePair<string, string>[] urlParams)
        {
            var authToken = string.Format("&{0}={1}", AuthID, AuthString);
            var uriTry = encodeUriParams(URI, urlParams) + authToken;
            await asyncLock.WaitAsync();
            try
            {
                await Connection.InvokeAsync("ReceiveViewMessage",
                    AuthString, uriTry);
            }
            catch (Exception e) { LogUtil.LogText(e.Message); }
            finally { asyncLock.Release(); }
        }

        private string encodeUriParams(string uriBase, params KeyValuePair<string, string>[] urlParams)
        {
            if (urlParams.Length < 1)
                return uriBase;
            if (uriBase[uriBase.Length - 1] != '?')
                uriBase += "?";
            foreach (var kvp in urlParams)
                uriBase += string.Format("{0}={1}&", kvp.Key, kvp.Value);

            // remove trailing &
            return uriBase.Remove(uriBase.Length - 1, 1);
        }
    }
}

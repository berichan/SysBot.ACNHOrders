using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class WebConfig
    {
        /// <summary> HTTP or HTTPS Endpoint</summary>
        public string URIEndpoint { get; set; } = string.Empty;

        /// <summary> The Auth ID </summary>
        public string AuthID { get; set; } = string.Empty;

        /// <summary> The Auth Token or Password </summary>
        public string AuthTokenOrString { get; set; } = string.Empty;
    }
}

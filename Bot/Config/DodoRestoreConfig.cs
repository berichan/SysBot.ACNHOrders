using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class DodoRestoreConfig
    {
        /// <summary> Will not allow orders, but will try to fetch a new dodo code if the online session crashes</summary>
        public bool LimitedDodoRestoreOnlyMode { get; set; }

        /// <summary> Where the newly fetched dodo code will be written to after restore.</summary>
        public string DodoRestoreFilename { get; set; } = "Dodo.txt";

        /// <summary> Where the number of visitors will be written to after restore.</summary>
        public string VisitorFilename { get; set; } = "Visitors.txt";

        /// <summary> Channels where the dodo code will be posted on restore </summary>
        public List<ulong> EchoDodoChannels { get; set; } = new();

        /// <summary> Channels where new arrivals will be posted </summary>
        public List<ulong> EchoArrivalChannels { get; set; } = new();

        /// <summary> When set to true, restore mode will also regen the map. </summary>
        public bool RefreshMap { get; set; } = false;

        /// <summary> When set to true, new arrivals will be posted in all channels in restore mode. </summary>
        public bool PostDodoCodeWithNewArrivals { get; set; } 

        /// <summary> When set to true, dodo code will become bot status </summary>
        public bool SetStatusAsDodoCode { get; set; }

        /// <summary> When set to true, dodo code will become bot status </summary>
        public bool MashB { get; set; }
    }
}

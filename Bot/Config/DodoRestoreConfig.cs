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

        /// <summary> Where the list of names of visitors will be written to after restore.</summary>
        public string VisitorListFilename { get; set; } = "VisitorsList.txt";

        /// <summary> Where the names of villager would be written.</summary>
        public string VillagerFilename { get; set; } = "Villagers.txt";

        /// <summary> Whether or not we should minimize the amount of text written to the dodo/visitor files</summary>
        public bool MinimizeDetails { get; set; } = false;

        /// <summary> Channels where the dodo code will be posted on restore </summary>
        public List<ulong> EchoDodoChannels { get; set; } = new();

        /// <summary> Channels where new arrivals will be posted </summary>
        public List<ulong> EchoArrivalChannels { get; set; } = new();

        /// <summary> When set to true, restore mode will also regen the map. </summary>
        public bool RefreshMap { get; set; } = false;

        /// <summary> When set to true, restore mode will also freeze the map. </summary>
        public bool FreezeMap { get; set; } = false;

        /// <summary> When set to true, restore mode will also refresh the terrain + elevation. (requires refresh map to be set to true) </summary>
        public bool RefreshTerrainData { get; set; } = false;

        /// <summary> When set to true, new arrivals will be posted in all channels in restore mode. </summary>
        public bool PostDodoCodeWithNewArrivals { get; set; } 

        /// <summary> When set to true, dodo code will become bot status </summary>
        public bool SetStatusAsDodoCode { get; set; }

        /// <summary> When set to false, senddodo will not work (for anyone) </summary>
        public bool AllowSendDodo { get; set; } = true;

        /// <summary> When set to true, the bot will reinject lost villagers while running from the bot's own villager database </summary>
        public bool ReinjectMovedOutVillagers { get; set; }

        /// <summary> Change the percentage of the dodo code font size if using the fancy dodo code renderer </summary>
        public float DodoFontPercentageSize { get; set; } = 100;

        /// <summary> When set to true, the bot will mash B when not doing any of the other restore functionality </summary>
        public bool MashB { get; set; }

        /// <summary> When set to anything above -1, the bot will automatically fetch a new dodo every x minutes if the island is empty </summary>
        public int AutoNewDodoTimeMinutes { get; set; } = -1;

        /// <summary> What, if anything, should the bot react with if someone successfully gets DMed the dodo code? </summary>
        public string SuccessfulDodoCodeSendReaction { get; set; } = "";

        /// <summary> Should we cycle through NHLs in the NHL directory? </summary>
        public bool CycleNHLs { get; set; } = false;

        /// <summary> If the above is set to true, how often should we cycle through them? </summary>
        public int CycleNHLMinutes { get; set; } = 1440;
    }
}

using System;
using System.Collections.Generic;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class OrderBotConfig
    {
        private int _maxQueueCount = 50;
        private int _timeAllowed = 180;
        private int _waitForArriverTime = 60;

        /// <summary> Amount of people allowed in the queue before the bot stop accepting requests. Won't accept more than 99 (around 8 hours) </summary>
        public int MaxQueueCount
        {
            get => _maxQueueCount;
            set => _maxQueueCount = Math.Max(1, Math.Min(99, value));
        }

        /// <summary> Maximum amount of time in seconds before a user is kicked from your island to avoid loiterers. Minimum is 2 minutes (120 seconds). </summary>
        public int UserTimeAllowed 
        { 
            get => _timeAllowed; 
            set => _timeAllowed = Math.Max(120, value); 
        }

        /// <summary> Maximum amount of time to wait until they're a no-show and the bot restarts in seconds. </summary>
        public int WaitForArriverTime
        {
            get => _waitForArriverTime;
            set => _waitForArriverTime = Math.Max(45, value);
        }

        /// <summary> Message to send at the end of the order completion string </summary>
        public string CompleteOrderMessage { get; set; } = "Have a great day!";

        /// <summary> If some of the inputs get eaten while talking to orville, should we try talking to him one more time? </summary>
        public bool RetryFetchDodoOnFail { get; set; } = true;

        /// <summary> Should we include IDs in the echos and order replies? </summary>
        public bool ShowIDs { get; set; } = false;

        /// <summary> Should the bot ping the bot owner when it detects an alt account? </summary>
        public bool PingOnAbuseDetection { get; set; } = true;

        /// <summary> Set this to a number higher than 0 if you want to softban people for not arriving/leaving on time </summary>
        public int PenaltyBanCount { get; set; } = 0;

        public int PositionCommandCooldown { get; set; } = -1;

        /// <summary> Folder of presets that can be ordered using $preset [filename] </summary>
        public string NHIPresetsDirectory { get; set; } = "presets";

        /// <summary> Send messages of orders starting/arriving in the echo channels </summary>
        public List<ulong> EchoArrivingLeavingChannels { get; set; } = new();
    }
}

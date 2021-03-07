using System;
using System.Collections.Generic;
using System.Text;
using NHSE.Core;
using SysBot.ACNHOrders;

namespace SysBot.ACNHOrders.Twitch
{
    public class TwitchQueue
    {
        public IReadOnlyCollection<Item> ItemReq { get; }
        public VillagerRequest? VillagerReq { get; }
        public string DisplayName { get; }
        public ulong ID { get; }
        public bool IsSubscriber { get; }

        public TwitchQueue(IReadOnlyCollection<Item> itemReq, VillagerRequest? villagerReq, string dispname, ulong id, bool subscriber)
        {
            ItemReq = itemReq;
            VillagerReq = villagerReq;
            DisplayName = dispname;
            ID = id;
            IsSubscriber = subscriber;
        }
    }
}

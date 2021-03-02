using System;
using System.Collections.Generic;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders
{
    public sealed class ItemRequest
    {
        public readonly string User;
        public readonly IReadOnlyCollection<Item> Items;

        public ItemRequest(string user, IReadOnlyCollection<Item> items)
        {
            User = user;
            Items = items;
        }
    }

    public sealed class VillagerRequest
    {
        public readonly VillagerData Villager;
        public readonly byte Index;
        public readonly string GameName;
        public Action<bool>? OnFinish { get; set; }

        public VillagerRequest(VillagerData data, byte i, string gameName)
        {
            Villager = data;
            Index = i;
            GameName = gameName;
        }
    }
}

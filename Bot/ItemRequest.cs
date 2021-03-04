using System;
using System.Collections.Generic;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders
{
    public abstract class Request<T>
    {
        public readonly string User;
        public readonly T Item;
        public Action<bool>? OnFinish { get; set; }

        public Request(string usr, T item)
        {
            User = usr;
            Item = item;
        }
    }

    public sealed class ItemRequest : Request<IReadOnlyCollection<Item>>
    {
        public ItemRequest(string user, IReadOnlyCollection<Item> items) : base(user, items) { }
    }

    public sealed class VillagerRequest : Request<VillagerData>
    {
        public readonly byte Index;
        public readonly string GameName;

        public VillagerRequest(string user, VillagerData data, byte i, string gameName) : base (user, data)
        {
            Index = i;
            GameName = gameName;
        }
    }

    public sealed class SpeakRequest : Request<string>
    {
        public SpeakRequest(string usr, string text) : base(usr, text) { }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NHSE.Core;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public class PocketInjectorAsync
    {
        public readonly Item DroppableOnlyItem = new Item(0x9C9); // Gold nugget

        private readonly ISwitchConnectionAsync Bot;
        public bool Connected => Bot.Connected;

        public uint WriteOffset { private get; set; }
        public bool ValidateEnabled { get; set; } = true;

        public PocketInjectorAsync(ISwitchConnectionAsync bot, uint writeOffset)
        {
            Bot = bot;
            WriteOffset = writeOffset;
        }

        private const int pocket = Item.SIZE * 20;
        private const int size = (pocket + 0x18) * 2;
        private const int shift = -0x18 - (Item.SIZE * 20);

        private uint DataOffset => (uint)(WriteOffset + shift);

        public async Task<(bool, byte[])> ReadValidateAsync(CancellationToken token)
        {
            var data = await Bot.ReadBytesAsync(DataOffset, size, token).ConfigureAwait(false);
            var validated = Validate(data);
            return (validated, data);
        }

        public async Task<(InjectionResult, Item[])> Read(CancellationToken token)
        {
            var (valid, data) = await ReadValidateAsync(token);
            if (!valid)
                return (InjectionResult.FailValidate, Array.Empty<Item>());

            var seqItems = GetEmptyInventory();

            var pocket2 = seqItems.Take(20).ToArray();
            var pocket1 = seqItems.Skip(20).ToArray();
            var p1 = Item.GetArray(data.Slice(0, pocket));
            var p2 = Item.GetArray(data.Slice(pocket + 0x18, pocket));

            for (int i = 0; i < pocket1.Length; i++)
                pocket1[i].CopyFrom(p1[i]);

            for (int i = 0; i < pocket2.Length; i++)
                pocket2[i].CopyFrom(p2[i]);

            return (InjectionResult.Success, seqItems);
        }

        public async Task<InjectionResult> Write(Item[] items, CancellationToken token)
        {
            var (valid, data) = await ReadValidateAsync(token);
            if (!valid)
                return InjectionResult.FailValidate;

            var orig = (byte[])data.Clone();

            var pocket2 = items.Take(20).ToArray();
            var pocket1 = items.Skip(20).ToArray();
            var p1 = Item.SetArray(pocket1);
            var p2 = Item.SetArray(pocket2);

            p1.CopyTo(data, 0);
            p2.CopyTo(data, pocket + 0x18);

            if (data.SequenceEqual(orig))
                return InjectionResult.Same;

            await Bot.WriteBytesAsync(data, DataOffset, token);

            return InjectionResult.Success;
        }

        public async Task Write40(Item items, CancellationToken token)
        {
            var itemSet = MultiItem.DeepDuplicateItem(items, 40);
            await Write(itemSet, token).ConfigureAwait(false);
        }

        public static Item[] GetEmptyInventory(int invCount = 40)
        {
            var items = new Item[invCount];
            for (int i = 0; i < invCount; ++i)
                items[i] = new Item();
            return items;
        }

        public bool Validate(byte[] data)
        {
            if (!ValidateEnabled)
                return true;

            return ValidateItemBinary(data);
        }

        public static bool ValidateItemBinary(byte[] data)
        {
            // Check the unlocked slot count -- expect 0,10,20
            var bagCount = BitConverter.ToUInt32(data, pocket);
            if (bagCount > 20 || bagCount % 10 != 0) // pouch21-39 count
                return false;

            var pocketCount = BitConverter.ToUInt32(data, pocket + 0x18 + pocket);
            if (pocketCount != 20) // pouch0-19 count should be 20.
                return false;

            // Check the item wheel binding -- expect -1 or [0,7]
            // Disallow duplicate binds!
            // Don't bother checking that bind[i] (when ! -1) is not NONE at items[i]. We don't need to check everything!
            var bound = new List<byte>();
            if (!ValidateBindList(data, pocket + 4, bound))
                return false;
            if (!ValidateBindList(data, pocket + 4 + (pocket + 0x18), bound))
                return false;

            return true;
        }

        private static bool ValidateBindList(byte[] data, int bindStart, ICollection<byte> bound)
        {
            for (int i = 0; i < 20; i++)
            {
                var bind = data[bindStart + i];
                if (bind == 0xFF)
                    continue;
                if (bind > 7)
                    return false;
                if (bound.Contains(bind))
                    return false;

                bound.Add(bind);
            }

            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public static class InventoryValidator
    {
        private const int pocket = Item.SIZE * 20;
        private const int size = (pocket + 0x18) * 2;
        private const int shift = -0x18 - (Item.SIZE * 20);

        public static (uint, int) GetOffsetLength(uint slot1) => ((uint)((int)slot1 + shift), size);

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

using NHSE.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class MultiItem
    {
        public const int MaxOrder = 40;
        public ItemArrayEditor<Item> ItemArray { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="items">Items to inject to floor of island</param>
        /// <param name="fillToMax">Whether to fill the array to the full <see cref="MaxOrder"/> amount</param>
        public MultiItem(Item[] items, bool fillToMax = true, bool stackMax = true)
        {
            var itemArray = items;
            if (stackMax)
                StackToMax(itemArray);

            if (items.Length < MaxOrder && fillToMax)
            {
                int itemMultiplier = (int)(1f / ((1f / MaxOrder) * items.Length));
                var newItems = new List<Item>();
                for (int i = 0; i < items.Length; ++i)
                {
                    var multipliedItems = DeepDuplicateItem(items[i], itemMultiplier);
                    newItems.AddRange(multipliedItems);
                }
                itemArray = newItems.ToArray();
            }
            var itemsToAdd = (Item[])itemArray.Clone();
            ItemArray = new ItemArrayEditor<Item>(itemsToAdd);
        }

        public MultiItem()
        {
            ItemArray = new ItemArrayEditor<Item>(System.Array.Empty<Item>());
        }

        public static void StackToMax(Item[] itemSet)
        {
            foreach (var it in itemSet)
                if (ItemInfo.TryGetMaxStackCount(it, out var max))
                    if (max != 1)
                        it.Count = (ushort)(max - 1);
        }

        public static void StackToMax(IReadOnlyCollection<Item> itemSet) => StackToMax(itemSet.ToArray());

        public static Item[] DeepDuplicateItem(Item it, int count)
        {
            Item[] ret = new Item[count];
            for (int i = 0; i < count; ++i)
            {
                ret[i] = new Item();
                ret[i].CopyFrom(it);
            }
            return ret;
        }
    }
}

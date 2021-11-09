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
        public MultiItem(Item[] items, bool catalogue = false, bool fillToMax = true, bool stackMax = true)
        {
            var itemArray = items;
            if (stackMax)
                StackToMax(itemArray);

            if (items.Length < MaxOrder && fillToMax && !catalogue)
            {
                int itemMultiplier = (int)(1f / ((1f / MaxOrder) * items.Length));
                var newItems = new List<Item>(items);
                for (int i = 0; i < items.Length; ++i)
                {
                    // duplicate those without variations
                    // attempt get body variations
                    var currentItem = items[i];
                    var remake = ItemRemakeUtil.GetRemakeIndex(currentItem.ItemId);
                    var associated = GameInfo.Strings.GetAssociatedItems(currentItem.ItemId, out _);
                    getMaxStack(currentItem, out var stackedMax);
                    if (remake > 0 && currentItem.Count == 0) // ItemRemake: only do this if they've asked for the base count of an item!
                    {
                        var info = ItemRemakeInfoData.List[remake];
                        var body = info.GetBodySummary(GameInfo.Strings);
                        var bodyVariations = body.Split(new string[2] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                        if (bodyVariations.Length < 1)
                        {
                            var mItems = DeepDuplicateItem(currentItem, Math.Max(0, itemMultiplier - 1));
                            newItems.AddRange(mItems);
                            continue;
                        }
                        int varCount = bodyVariations.Length;
                        if (!bodyVariations[0].StartsWith("0"))
                            varCount++;
                        var multipliedItems = DeepDuplicateItem(currentItem, varCount);
                        for (ushort j = 1; j < varCount; ++j)
                        {
                            var itemToAdd = multipliedItems[j];
                            itemToAdd.Count = j;
                            newItems.Add(itemToAdd);
                        }
                    }
                    else if (stackedMax < 2 && associated.Count > 1 && currentItem.ItemId != Item.DIYRecipe) // clothing with parenthesised versions
                    {
                        for (int j = 0; j < associated.Count; ++j)
                        {
                            var toAdd = new Item();
                            toAdd.CopyFrom(currentItem);
                            toAdd.ItemId = (ushort)associated[j].Value;
                            newItems.Add(toAdd);
                        }
                    }
                    else if (remake < 0)
                    {
                        var multipliedItems = DeepDuplicateItem(currentItem, Math.Max(0, itemMultiplier-1));
                        newItems.AddRange(multipliedItems);
                    }

                    if (newItems.Count >= MaxOrder) // not the best way to do this, but at worst you'll need an extra line on your map
                        break;
                }

                if (newItems.Count > MaxOrder)
                    itemArray = newItems.Take(MaxOrder).ToArray();
                else
                    itemArray = newItems.ToArray();
            }
            var itemsToAdd = (Item[])itemArray.Clone();

            var len = itemsToAdd.Length;
            if (len < MaxOrder && fillToMax)
            {
                // repeat last item in the original order
                Item toDupe = items[items.Length - 1];
                var dupes = DeepDuplicateItem(toDupe, MaxOrder - len);
                itemsToAdd = itemsToAdd.Concat(dupes).ToArray();

                // add a hole to know where the split is
                if (catalogue)
                    itemsToAdd[len] = new Item(Item.NONE);
            }

            ItemArray = new ItemArrayEditor<Item>(itemsToAdd);
        }

        public MultiItem()
        {
            ItemArray = new ItemArrayEditor<Item>(System.Array.Empty<Item>());
        }

        public static void StackToMax(Item[] itemSet)
        {
            foreach (var it in itemSet)
                if (getMaxStack(it, out int max))
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

        static bool getMaxStack(Item id, out int max)
        {
            if (StackableFlowers.Contains(id.ItemId))
            {
                max = 10;
                return true;
            }

            bool canStack = ItemInfo.TryGetMaxStackCount(id, out var maxStack);
            max = maxStack;
            return canStack;
        }

        public static readonly ushort[] StackableFlowers = { 2304, 2305,
                                                             2867, 2871, 2875, 2979, 2883, 2887, 2891, 2895, 2899, 2903,
                                                             2907, 2911, 2915, 2919, 2923, 2927, 2931, 2935, 2939, 2943,
                                                             2947, 2951, 2955, 2959, 2963,
                                                             2979, 2983, 2987, 2991, 2995, 2999, 
                                                                               3709, 3713, 3717, 3720, 3723, 3727, 3730,
                                                             3734, 3738, 3741, 3744, 3748, 3751, 3754, 3758, 3762, 3765,
                                                             3768,
                                                             5175};
    }
}

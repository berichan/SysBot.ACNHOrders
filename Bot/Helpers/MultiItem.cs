using NHSE.Core;
using System.Collections.Generic;

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
        public MultiItem(Item[] items, bool fillToMax = true)
        {
            var itemsToAdd = (Item[])items.Clone();
            ItemArray = new ItemArrayEditor<Item>(itemsToAdd);
        }

        public MultiItem()
        {
            ItemArray = new ItemArrayEditor<Item>(System.Array.Empty<Item>());
        }
    }
}

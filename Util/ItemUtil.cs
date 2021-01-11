using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public static class ItemUtil
    {
        private static readonly CompareInfo Comparer = CultureInfo.InvariantCulture.CompareInfo;
        private const CompareOptions opt = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreSymbols | CompareOptions.IgnoreWidth;

        public static Item GetItem(string itemName, string lang = "en")
        {
            var strings = GameInfo.GetStrings(lang).ItemDataSource;
            return GetItem(itemName, strings);
        }

        public static Item GetItem(string itemName, IEnumerable<ComboItem> strings)
        {
            if (TryGetItem(itemName, strings, out var id))
                return new Item(id);
            return Item.NO_ITEM;
        }

        public static bool TryGetItem(string itemName, IEnumerable<ComboItem> strings, out ushort value)
        {
            foreach (var item in strings)
            {
                var result = Comparer.Compare(item.Text, 0, itemName, 0, opt);
                if (result != 0)
                    continue;

                value = (ushort)item.Value;
                return true;
            }

            value = 0;
            return false;
        }

        public static IEnumerable<ComboItem> GetItemsMatching(string itemName, IReadOnlyList<ComboItem> strings)
        {
            foreach (var item in strings)
            {
                var result = Comparer.IndexOf(item.Text, itemName, opt);
                if (result < 0)
                    continue;
                yield return item;
            }
        }

        public static string GetItemText(Item item)
        {
            var value = BitConverter.ToUInt64(item.ToBytesClass(), 0);
            var name = GameInfo.Strings.GetItemName(item.ItemId);
            return $"{name}: {value:X16}";
        }

        public static ushort GetID(string text)
        {
            if (!ulong.TryParse(text.Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.CurrentCulture, out var val))
                return Item.NONE;
            return (ushort)val;
        }

        public static string GetItemInfo(ushort itemID)
        {
            var remake = ItemRemakeUtil.GetRemakeIndex(itemID);
            if (remake < 0)
                return string.Empty;

            var info = ItemRemakeInfoData.List[remake];
            return GetItemInfo(info, GameInfo.Strings);
        }

        private static string GetItemInfo(ItemRemakeInfo info, IRemakeString str)
        {
            var sb = new StringBuilder();
            var body = info.GetBodySummary(str);
            if (body.Length > 0)
                sb.AppendLine(body);

            var fabric = info.GetFabricSummary(str);
            if (fabric.Length > 0)
                sb.AppendLine(fabric);

            return sb.ToString();
        }

        public static int GetItemDropOption(this Item item)
        {
            if (Item.DIYRecipe == item.ItemId)
                return 1;
            if (item.IsWrapped)
                return 0;

            var kind = ItemInfo.GetItemKind(item);
            return kind switch
            {
                ItemKind.Kind_DIYRecipe => 1,

                ItemKind.Kind_Flower => 2,
                _ => 1,
            };
        }

        public static bool ShouldWrapItem(this Item item)
        {
            if (Item.DIYRecipe == item.ItemId)
                return false;

            var kind = ItemInfo.GetItemKind(item);
            return kind switch
            {
                ItemKind.Kind_DIYRecipe => false,

                ItemKind.Kind_Flower => false,
                _ => true,
            };
        }

        public static bool IsDroppable(Item item)
        {
            if (item.IsFieldItem)
                return false;
            if (item.IsExtension)
                return false;
            if (item.IsNone)
                return false;
            if (item.SystemParam > 3)
                return false; // buried, dropped, etc

            var kind = ItemInfo.GetItemKind(item);
            return kind switch
            {
                ItemKind.Kind_Insect => false,

                ItemKind.Kind_DummyPresentbox => false,

                ItemKind.Kind_Fish => false,
                ItemKind.Kind_DiveFish => false,
                ItemKind.Kind_FlowerBud => false,
                ItemKind.Kind_Bush => false,
                ItemKind.Kind_Tree => false,

                _ => true,
            };
        }
    }
}

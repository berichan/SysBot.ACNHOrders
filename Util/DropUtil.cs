using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public static class DropUtil
    {
        public static readonly IReadOnlyDictionary<ushort, ushort> InvertedRecipeDictionary =
            RecipeList.Recipes.ToDictionary(z => z.Value, z => z.Key);

        public static IReadOnlyCollection<Item> GetItemsFromUserInput(string request, IConfigItem cfg)
        {
            try
            {
                var split = request.Split(new[] { " ", "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                return GetItems(split, cfg);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch
#pragma warning restore CA1031 // Do not catch general exception types
            {
                var split = request.Split(new[] { ",", "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                return GetItems(split, cfg, GameLanguage.DefaultLanguage);
            }
        }

        public static IReadOnlyCollection<Item> GetDIYsFromUserInput(string request)
        {
            try
            {
                var split = request.Split(new[] { " ", "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                return GetDIYItems(split);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch
#pragma warning restore CA1031 // Do not catch general exception types
            {
                var split = request.Split(new[] { ",", "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                return GetDIYItems(split, GameLanguage.DefaultLanguage);
            }
        }

        public static IReadOnlyCollection<Item> GetDIYItems(IReadOnlyList<string> split)
        {
            var result = new Item[split.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var text = split[i].Trim();
                bool parse = ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var val);
                if (!parse || val > 0x420)
                    throw new Exception($"Item value out of expected range ({text}).");

                if (!RecipeList.Recipes.TryGetValue((ushort)val, out _))
                    throw new Exception($"DIY recipe appears to be invalid ({text}).");

                result[i] = new Item(Item.DIYRecipe) { Count = (ushort)val };
            }
            return result;
        }

        public static IReadOnlyCollection<Item> GetDIYItems(IReadOnlyList<string> split, string lang)
        {
            if (split.Count > 1 && split[0].Length < 3)
            {
                var langIndex = GameLanguage.GetLanguageIndex(split[0]);
                lang = GameLanguage.Language2Char(langIndex);
                split = split.Skip(1).ToArray();
            }

            var result = new Item[split.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var text = split[i].Trim();
                var item = ItemUtil.GetItem(text, lang);
                if (!InvertedRecipeDictionary.TryGetValue(item.ItemId, out var diy))
                    throw new Exception($"DIY recipe appears to be invalid ({text}).");

                result[i] = new Item(Item.DIYRecipe) { Count = diy };
            }
            return result;
        }

        public static IReadOnlyCollection<Item> GetItems(IReadOnlyList<string> split, IConfigItem config, string lang)
        {
            if (split.Count > 1 && split[0].Length < 3)
            {
                var langIndex = GameLanguage.GetLanguageIndex(split[0]);
                lang = GameLanguage.Language2Char(langIndex);
                split = split.Skip(1).ToArray();
            }

            var strings = GameInfo.Strings.itemlistdisplay;
            var result = new Item[split.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var text = split[i].Trim();
                var item = CreateItem(text, i, config, lang);

                if (item.ItemId >= strings.Length)
                    throw new Exception($"Item requested is out of expected range ({item.ItemId:X4} > {strings.Length:X4}).");
                if (string.IsNullOrWhiteSpace(strings[item.ItemId]))
                    throw new Exception($"Item requested does not have a valid name ({item.ItemId:X4}).");

                result[i] = item;
            }
            return result;
        }

        public static IReadOnlyCollection<Item> GetItems(IReadOnlyList<string> split, IConfigItem config)
        {
            var strings = GameInfo.Strings.itemlistdisplay;
            var result = new Item[split.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var text = split[i].Trim();
                var convert = GetBytesFromString(text);
                var item = CreateItem(convert, i, config);

                if (item.ItemId >= strings.Length)
                    throw new Exception($"Item requested is out of expected range ({item.ItemId:X4} > {strings.Length:X4}).");
                if (string.IsNullOrWhiteSpace(strings[item.ItemId]))
                    throw new Exception($"Item requested does not have a valid name ({item.ItemId:X4}).");

                result[i] = item;
            }
            return result;
        }

        private static byte[] GetBytesFromString(string text)
        {
            if (!ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.CurrentCulture, out var val))
                return Item.NONE.ToBytes();
            return BitConverter.GetBytes(val);
        }

        private static Item CreateItem(string name, int i, IConfigItem config, string lang = "en")
        {
            var item = ItemUtil.GetItem(name, lang);
            if (item.IsNone)
                throw new Exception($"Failed to convert item {i}:{name} for Language {lang}.");

            if (!IsSaneItemForDrop(item))
                throw new Exception($"Unsupported item: {i}:{name}");

            if (config.WrapAllItems && item.ShouldWrapItem())
                item.SetWrapping(ItemWrapping.WrappingPaper, config.WrappingPaper, true);

            return item;
        }

        private static Item CreateItem(byte[] convert, int i, IConfigItem config)
        {
            Item item;
            try
            {
                item = convert.ToClass<Item>();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to convert item {i}: {ex.Message}");
            }

            if (!IsSaneItemForDrop(item) || convert.Length != Item.SIZE)
                throw new Exception($"Unsupported item: {i}");

            if (config.WrapAllItems && item.ShouldWrapItem())
                item.SetWrapping(ItemWrapping.WrappingPaper, config.WrappingPaper, true);
            return item;
        }

        private static bool IsSaneItemForDrop(Item item)
        {
            if (!ItemUtil.IsDroppable(item))
                return false;

            // Sanitize Values
            if (item.ItemId == Item.MessageBottle || item.ItemId == Item.MessageBottleEgg)
            {
                item.ItemId = Item.DIYRecipe;
                item.FreeParam = 0;
            }

            return true;
        }
    }
}

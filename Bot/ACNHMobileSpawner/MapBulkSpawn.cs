using NHSE.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using static NHSE.Core.ItemKind;

namespace ACNHMobileSpawner
{
    // Written in C# 6 due to Unity limitations
    public class MapBulkSpawn
    {
        public enum BulkSpawnPreset
        {
            Music,
            DIYRecipesAlphabetical,
            DIYRecipesSequential,
            Fossils,
            GenericMaterials,
            SeasonalMaterials,
            RealArt,
            FakeArt,
            Bugs,
            Fish,
            BugsAndFish,
            InventoryOfApp,
            CustomFile,
        }

        public enum SpawnDirection
        {
            SouthEast,
            SouthWest,
            NorthWest,
            NorthEast
        }
        
        public BulkSpawnPreset CurrentSpawnPreset { get; private set; } = 0;
        public SpawnDirection CurrentSpawnDirection { get; private set; } = 0;
        public int Multiplier => 1;
        public float RectWidthDimension => 1;
        public float RectHeightDimension => 1;
        public bool OverwriteTiles => true;

        private static IReadOnlyList<ushort>? allItems = null;
        public static IReadOnlyList<ushort> GetAllItems()
        {
            if (allItems == null)
            {
                var listItems = GameInfo.Strings.ItemDataSource.ToList();
                var itemsClean = listItems.Where(x => !x.Text.StartsWith("(Item #")).ToList();
                var items = new ushort[itemsClean.Count];
                for (int i = 0; i < itemsClean.Count; ++i)
                {
                    items[i] = (ushort)itemsClean[i].Value;
                }
                allItems = items;
            }

            return allItems;
        }

        private Item[] fileLoadedItems = new Item[1] { new Item(0x09C4) };

        public MapBulkSpawn()
        {

        }

        private int getItemCount()
        {
            return GetItemsOfCurrentPreset().Length;
        }

        private void Flag20LoadedItems()
        {
            foreach (Item i in fileLoadedItems)
                i.SystemParam = 0x20;
        }

        public Item[] GetItemsOfCurrentPreset()
        {
            return GetItemsOfPreset(CurrentSpawnPreset);
        }

        public Item[] GetItemsOfPreset(BulkSpawnPreset preset, byte flag0 = 0x20)
        {
            List<Item> toRet = new List<Item>();
            switch (preset)
            {
                case BulkSpawnPreset.Music:
                    toRet.AddRange(GetItemsOfKind(Kind_Music));
                    break;
                case BulkSpawnPreset.DIYRecipesAlphabetical:
                    toRet.AddRange(GetDIYRecipes());
                    break;
                case BulkSpawnPreset.DIYRecipesSequential:
                    toRet.AddRange(GetDIYRecipes(false));
                    break;
                case BulkSpawnPreset.Fossils:
                    toRet.AddRange(GetItemsOfKind(Kind_Fossil));
                    break;
                case BulkSpawnPreset.GenericMaterials:
                    toRet.AddRange(GetItemsOfKind(Kind_Ore, Kind_CraftMaterial));
                    break;
                case BulkSpawnPreset.SeasonalMaterials:
                    toRet.AddRange(GetItemsOfKind(Kind_Vegetable, Kind_Sakurapetal, Kind_ShellDrift, Kind_TreeSeedling, Kind_CraftMaterial, Kind_Mushroom, Kind_AutumnLeaf, Kind_SnowCrystal));
                    break;
                case BulkSpawnPreset.RealArt:
                    toRet.AddRange(GetItemsOfKind(Kind_Picture, Kind_Sculpture));
                    break;
                case BulkSpawnPreset.FakeArt:
                    toRet.AddRange(GetItemsOfKind(Kind_PictureFake, Kind_SculptureFake));
                    break;
                case BulkSpawnPreset.Bugs:
                    toRet.AddRange(GetItemsOfKind(Kind_Insect));
                    break;
                case BulkSpawnPreset.Fish:
                    toRet.AddRange(GetItemsOfKind(Kind_Fish, Kind_ShellFish, Kind_DiveFish));
                    break;
                case BulkSpawnPreset.BugsAndFish:
                    toRet.AddRange(GetItemsOfKind(Kind_Fish, Kind_ShellFish, Kind_DiveFish));
                    toRet.AddRange(GetItemsOfKind(Kind_Insect));
                    break;
                case BulkSpawnPreset.CustomFile:
                    toRet.AddRange(fileLoadedItems);
                    break;
                default:
                    toRet.Add(new Item(0x09C4)); // tree branch
                    break;

            }

            if (preset != BulkSpawnPreset.CustomFile)
            {
                foreach (Item i in toRet)
                {
                    i.SystemParam = flag0;

                    // try stacking to max
                    var kind = ItemInfo.GetItemKind(i);
                    if (kind != Kind_DIYRecipe && kind != Kind_MessageBottle && kind != Kind_Fossil)
                        if (ItemInfo.TryGetMaxStackCount(i, out var max))
                            i.Count = --max;
                }
            }

            int mul = Multiplier;
            if (mul != 1)
            {
                List<Item> multipliedItemList = new List<Item>();
                foreach (var item in toRet)
                    for (int i = 0; i < mul; ++i)
                        multipliedItemList.Add(item); // references are fine, should be copied from
                toRet = multipliedItemList;
            }

            return toRet.ToArray();
        }

        private Item[] GetItemsOfKind(params ItemKind[] ik)
        {
            var toRet = new List<ushort>();
            foreach (var kind in ik)
            {
                toRet.AddRange(GetAllItems().Where(x => ItemInfo.GetItemKind(x) == kind));
            }

            var asItems = new Item[toRet.Count];
            for (int i = 0; i < toRet.Count; ++i)
                asItems[i] = new Item(toRet[i]);

            return asItems;
        }

        private Item[] GetDIYRecipes(bool alphabetical = true)
        {
            var recipes = RecipeList.Recipes;
            var retRecipes = new List<Item>();
            foreach (var recipe in recipes)
            {
                var itemRecipe = new Item(Item.DIYRecipe);
                itemRecipe.Count = recipe.Key;
                retRecipes.Add(itemRecipe);
            }
            if (alphabetical)
            {
                var ordered = retRecipes.OrderBy(x => getRecipeName(x.Count, recipes));
                retRecipes = ordered.ToList();
            }
            return retRecipes.ToArray();
        }

        private string getRecipeName(ushort count, IReadOnlyDictionary<ushort, ushort> recipes)
        {
            var currentRecipeItem = recipes[count];
            return GameInfo.Strings.itemlistdisplay[currentRecipeItem].ToLower();
        }

        public static void TrimTrailingNoItems(ref Item[] buffer, ushort trimValue)
        {
            int i = buffer.Length;
            while (i > 0 && buffer[--i].ItemId == trimValue)
            {
                ; // no-op by design
            }
            Array.Resize(ref buffer, i + 1);
            return;
        }
    }
}

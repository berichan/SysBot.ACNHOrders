using NHSE.Core;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using System.IO;

namespace ACNHMobileSpawner
{
    public class OffsetData
    {
        public uint Offset;
        public byte[] ToSend;
        public OffsetData(uint os, byte[] data) { Offset = os; ToSend = data; }
    }

    // Written in C# 6 due to Unity limitations. A (much) lighter version of UI_Map.cs and UI_MapTerrain.cs
    public class MapTerrainLite
    {
        public const int ByteSize = MapGrid.MapTileCount32x32 * Item.SIZE;

        public readonly byte[] StartupBytes;
        public readonly Item[] StartupItems;

        public readonly FieldItemLayer ItemLayer;
        public int SpawnX { get; set; } = 0;
        public int SpawnY { get; set; } = 0;

        public MapTerrainLite(byte[] itemBytes)
        {
            if (itemBytes.Length != ByteSize)
                throw new Exception("Field items are of the incorrect size.");
            StartupBytes = itemBytes;
            var items = Item.GetArray(StartupBytes);
            StartupItems = CloneItemArray(items);
            ItemLayer = new FieldItemLayer(items);
        }

        // Remove all tile checking code
        public void Spawn(Item[] newItems, int itemsPerLine = 10, bool forceFlag32 = true)
        {
            int totalXTiles = itemsPerLine * 2;
            int x = SpawnX;
            int y = SpawnY;
            for (int i = 0; i < newItems.Length; ++i)
            {
                x = SpawnX + ((i * 2) % totalXTiles);
                y = SpawnY + ((i / itemsPerLine) * 2);
                var tile = ItemLayer.GetTile(x, y);
                tile.CopyFrom(newItems[i]);
                if (forceFlag32)
                    tile.SystemParam = 0x20;
                ItemLayer.SetExtensionTiles(tile, x, y);
            }
        }

        /// <summary>
        /// Chunks different tiles to be placed back into RAM as <see cref="OffsetData"/> 
        /// </summary>
        /// <param name="chunkSize">The chunk size in bytes, must be divisible by 8 (Item.SIZE)</param>
        /// <param name="mapOffset">The offset in ram for fielditemstart</param>
        /// <returns></returns>
        public OffsetData[] GenerateReturnBytes(int chunkSize = 4096, uint mapOffset = (uint)OffsetHelper.FieldItemStart)
        {
            int acreSizeItems = chunkSize / Item.SIZE;

            // List and chunk
            var listLayer = new List<Item>(ItemLayer.Tiles);
            var listTemplate = new List<Item>(StartupItems);
            var chunksLayer = listLayer.ChunkBy(acreSizeItems);
            var chunksTemplate = listTemplate.ChunkBy(acreSizeItems);

            var dataSendList = new List<OffsetData>();

            for (int i = 0; i < chunksLayer.Count; ++i)
            {
                if (chunksLayer[i].IsDifferent(chunksTemplate[i]))
                    dataSendList.Add(new OffsetData((uint)(mapOffset + (i * chunkSize)), chunksLayer[i].SetArray(Item.SIZE)));
            }

            return dataSendList.ToArray();
        }

        public OffsetData[] GetDifferencePrioritizeStartup(byte[] newMapBytes, int chunkSize = 4096, uint mapOffset = (uint)OffsetHelper.FieldItemStart)
        {
            if (newMapBytes.Length != ByteSize)
                throw new Exception("Field items are of the incorrect size.");
            var listStartData = new List<byte>(StartupBytes);
            var listNewData = new List<byte>(newMapBytes);
            var chunkSD = listStartData.ChunkBy(chunkSize);
            var chunkND = listNewData.ChunkBy(chunkSize);

            var dataSendList = new List<OffsetData>();

            for (int i = 0; i < chunkSD.Count; ++i)
            {
                if (!chunkSD[i].SequenceEqual(chunkND[i]))
                    dataSendList.Add(new OffsetData((uint)(mapOffset + (i * chunkSize)), chunkSD[i].ToArray()));
            }

            return dataSendList.ToArray();
        }

        public static Item[] CloneItemArray(Item[] source)
        {
            Item[] items = new Item[source.Length];
            for (int i = 0; i < source.Length; ++i)
            {
                items[i] = new Item();
                items[i].CopyFrom(source[i]);
            }
            return items;
        }
    }

    public static class ItemListExtensions
    {
        public static List<List<T>> ChunkBy<T>(this List<T> source, int chunkSize)
        {
            return source
                .Select((x, i) => new { Index = i, Value = x })
                .GroupBy(x => x.Index / chunkSize)
                .Select(x => x.Select(v => v.Value).ToList())
                .ToList();
        }

        public static bool IsDifferent(this List<Item> items, List<Item> toCompare)
        {
            for (int i = 0; i < items.Count; ++i)
            {
                if (items[i].IsDifferentTo(toCompare[i]))
                    return true;
            }

            return false;
        }

        public static bool IsDifferentTo(this Item it, Item i) // doesn't look like it, but fastest for map acre comparison for writes
        {
            if (it.ItemId != i.ItemId)
                return true;
            if (it.SystemParam != i.SystemParam)
                return true;
            if (it.AdditionalParam != i.AdditionalParam)
                return true;
            if (it.FreeParam != i.FreeParam)
                return true;

            return false;
        }
    }
}

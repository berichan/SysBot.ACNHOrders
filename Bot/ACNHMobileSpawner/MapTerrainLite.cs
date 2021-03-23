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
        public const int TerrainSize = MapGrid.MapTileCount16x16 * TerrainTile.SIZE;

        public const int AcreWidth = 7 + (2 * 1); // 1 on each side cannot be traversed
        private const int AcreHeight = 6 + (2 * 1); // 1 on each side cannot be traversed
        private const int AcreMax = AcreWidth * AcreHeight;
        private const int AcreSizeAll = AcreMax * 2;
        public const int AcrePlusAdditionalParams = AcreSizeAll + 2 + 4 + 8 + sizeof(uint);

        public readonly byte[] StartupBytes;
        public readonly Item[] StartupItems;

        public readonly byte[] StartupTerrain;
        public readonly byte[] StartupAcreParams;

        public readonly FieldItemLayer ItemLayer;
        public int SpawnX { get; set; } = 0;
        public int SpawnY { get; set; } = 0;

        public MapTerrainLite(byte[] itemBytes, byte[] terrain, byte[] acreplaza)
        {
            if (itemBytes.Length != ByteSize)
                throw new Exception("Field items are of the incorrect size.");
            StartupBytes = itemBytes;
            var items = Item.GetArray(StartupBytes);
            StartupItems = CloneItemArray(items);
            ItemLayer = new FieldItemLayer(items);

            StartupTerrain = terrain;
            StartupAcreParams = acreplaza;
        }

        public MapTerrainLite(byte[] itemBytes) : this(itemBytes, Array.Empty<byte>(), Array.Empty<byte>()) { }

        // Remove all tile checking code
        public void Spawn(Item[] newItems, int itemsPerLine = 10, bool forceFlag32 = true)
        {
            int totalXTiles = itemsPerLine * 2;
            int x = SpawnX;
            int y = SpawnY;
            for (int i = 0; i < newItems.Length; ++i)
            {
                var currItem = newItems[i];
                x = SpawnX + ((i * 2) % totalXTiles);
                y = SpawnY + ((i / itemsPerLine) * 2);
                var tile = ItemLayer.GetTile(x, y);
                if (!currItem.IsNone)
                {
                    tile.CopyFrom(currItem);
                    if (forceFlag32)
                        tile.SystemParam = 0x20;
                    ItemLayer.SetExtensionTiles(tile, x, y);
                }
                else
                {
                    tile.Delete();
                    ItemLayer.DeleteExtensionTiles(tile, x, y);
                }
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

        public OffsetData[] GetDifferencePrioritizeStartup(byte[] newMapBytes, int chunkSize = 4096, bool merge = false, uint mapOffset = (uint)OffsetHelper.FieldItemStart)
        {
            if (merge)
                return GetDifferenceMerge(newMapBytes, mapOffset);
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

        private OffsetData[] GetDifferenceMerge(byte[] newMapBytes, uint mapOffset = (uint)OffsetHelper.FieldItemStart)
        {
            const int chunkSize = 4096;
            if (newMapBytes.Length != ByteSize)
                throw new Exception("Field items are of the incorrect size.");
            var listStartData = new List<byte>(StartupBytes);
            var listNewData = new List<byte>(newMapBytes);
            var chunkSD = listStartData.ChunkBy(chunkSize);
            var chunkND = listNewData.ChunkBy(chunkSize);

            var dataSendList = new List<OffsetData>();

            for (int i = 0; i < chunkSD.Count; ++i)
            {
                var sd = Item.GetArray(chunkSD[i].ToArray());
                var nd = Item.GetArray(chunkND[i].ToArray());
                bool changed = false;
                for (int j = 0; j < sd.Length; ++j)
                {
                    if (sd[j].IsDifferentTo(nd[j]))
                    {
                        if (sd[j].IsNone)
                            sd[j].CopyFrom(nd[j]);

                        changed = true;
                    }
                }

                if (changed)
                {
                    var nBytes = sd.SetArray(Item.SIZE);
                    dataSendList.Add(new OffsetData((uint)(mapOffset + (i * chunkSize)), nBytes));
                }
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

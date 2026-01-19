using ACNHMobileSpawner;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SysBot.ACNHOrders
{
    public class JoiningVillager
    {
        public string VillagerName { get; private set; }
        public uint VillagerID { get; private set; }
        
        public string IslandName { get; private set; }
        public uint IslandID { get; private set; }

        public JoiningVillager(string name, uint villagerId, string islandName, uint islandId)
        {
            VillagerName = name;
            VillagerID = villagerId;
            IslandName = islandName;
            IslandID = islandId;
        }

        public override string ToString()
        {
            return $"{VillagerName} (VID: {VillagerID}) joining from {IslandName} (IID: {IslandID})";
        }
    }

    public static class JoiningVillagerHelper
    {
        public static async Task<JoiningVillager> FetchVillager(ulong villagerNameAddress, ISwitchConnectionAsync connection, CancellationToken token)
        {
            // name
            var vnameBytes = await connection.ReadBytesAbsoluteAsync(villagerNameAddress, 0x14, token);
            var arriverName = Encoding.Unicode.GetString(vnameBytes).TrimEnd('\0');

            var vidBytes = await connection.ReadBytesAbsoluteAsync(villagerNameAddress - 0x4, 0x4, token);
            var arriverId = BitConverter.ToUInt32(vidBytes, 0);

            // island
            var islandNameBytes = await connection.ReadBytesAbsoluteAsync(villagerNameAddress - OffsetHelper.ArriverVillageShift, 0x14, token);
            var islandName = Encoding.Unicode.GetString(islandNameBytes).TrimEnd('\0');

            var islandIdBytes = await connection.ReadBytesAbsoluteAsync(villagerNameAddress - OffsetHelper.ArriverVillageShift - 0x4, 0x4, token);
            var islandId = BitConverter.ToUInt32(islandIdBytes, 0);

            return new JoiningVillager(arriverName, arriverId, islandName, islandId);
        }

        public static async Task ClearVillager(ulong villagerNameAddress, ISwitchConnectionAsync connection, CancellationToken token)
        {
            // clear name
            var emptyName = new byte[0x14];
            await connection.WriteBytesAbsoluteAsync(emptyName, villagerNameAddress, token);
            var emoptyId = new byte[0x4];
            await connection.WriteBytesAbsoluteAsync(emoptyId, villagerNameAddress - 0x4, token);

            // clear island
            await connection.WriteBytesAbsoluteAsync(emptyName, villagerNameAddress - OffsetHelper.ArriverVillageShift, token);
            await connection.WriteBytesAbsoluteAsync(emoptyId, villagerNameAddress - OffsetHelper.ArriverVillageShift - 0x4, token);
        }
    }
}

using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACNHMobileSpawner;
using NHSE.Core;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public static class OffsetUtil
    {
        public static async Task<uint> GetCurrentPlayerOffset(this CrossBot bot, uint rootInventoryOffset, uint playerSize, CancellationToken token)
        {
            var names = await FetchPlayerNames(bot.Connection, rootInventoryOffset, playerSize, token).ConfigureAwait(false);
            LogUtil.LogInfo($"Found the following players on your island: {string.Join(", ", names)}", bot.Config.IP);
            return rootInventoryOffset + (playerSize * ((uint)names.Length - 1));
        }

        private static async Task<string[]> FetchPlayerNames(IConsoleConnectionAsync connection, uint rootInventoryOffset, uint playerSize, CancellationToken token)
        {
            List<string> toRet = new List<string>();
            for (int i = 0; i < 8; ++i)
            {
                ulong address = OffsetHelper.getPlayerIdAddress(rootInventoryOffset) - 0xB8 + 0x20 + (playerSize * (ulong)i);
                byte[] pName = await connection.ReadBytesAsync((uint)address, 20, token).ConfigureAwait(false);
                if (!isZeroArray(pName))
                {
                    string name = StringUtil.GetString(pName, 0, 10);
                    toRet.Add(name);
                }
            }

            return toRet.ToArray();
        }

        private static bool isZeroArray(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; ++i)
                if (bytes[i] != 0)
                    return false;
            return true;
        }
    }
}

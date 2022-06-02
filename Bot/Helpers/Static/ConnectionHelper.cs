using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public static class ConnectionHelper
    {
        public const int MapChunkCount = 64;

        public static async Task WriteBytesLargeAsync(this IConsoleConnectionAsync connection, byte[] data, uint offset, int chunkSize, CancellationToken token)
        {
            int byteCount = data.Length;
            for (int i = 0; i < byteCount; i += chunkSize)
                await connection.WriteBytesAsync(SubArray(data, i, chunkSize), offset + (uint)i, token).ConfigureAwait(false);
        }

        public static async Task<byte[]> ReadBytesLargeAsync(this IConsoleConnectionAsync connection, uint offset, int length, int chunkSize, CancellationToken token)
        {
            List<byte> read = new List<byte>();
            for (int i = 0; i < length; i += chunkSize)
                read.AddRange(await connection.ReadBytesAsync(offset + (uint)i, Math.Min(chunkSize, length - i), token).ConfigureAwait(false));
            return read.ToArray();
        }

        public static async Task<string> GetVersionAsync(this ISwitchConnectionAsync connection, CancellationToken token)
        {
            var gvbytes = Encoding.ASCII.GetBytes("getVersion\r\n");
            byte[] socketReturn = await connection.ReadRaw(gvbytes, 9, token).ConfigureAwait(false);
            string version = Encoding.UTF8.GetString(socketReturn).TrimEnd('\0').TrimEnd('\n');
            return version;
        }

        private static T[] SubArray<T>(T[] data, int index, int length)
        {
            if (index + length > data.Length)
                length = data.Length - index;
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        // freeze
        public static async Task SetFreezePauseState(this ISwitchConnectionAsync connection, bool pause, CancellationToken token)
        {
            var cmd = Encoding.ASCII.GetBytes(pause ? "freezePause\r\n" : "freezeUnpause\r\n");
            await connection.SendRaw(cmd, token).ConfigureAwait(false);
        }

        public static async Task FreezeValues(this ISwitchConnectionAsync connection, uint offset, byte[] data, int chunkCount, CancellationToken token, bool unfreeze = false)
        {
            var chunkSize = data.Length / chunkCount;
            uint[] offsets = GetUnsafeOffsetsByChunkCount(offset, (uint)data.Length, (uint)chunkCount);
            var chunks = new List<byte[]>();

            for (int i = 0; i < chunkCount; ++i)
            {
                var toSend = SubArray(data, i * chunkSize, chunkSize);
                var cmd = unfreeze ? Encoding.ASCII.GetBytes($"unFreeze 0x{offsets[i]:X8}\r\n") : Encoding.ASCII.GetBytes($"freeze 0x{offsets[i]:X8} 0x{string.Concat(toSend.Select(z => $"{z:X2}"))}\r\n");
                await connection.SendRaw(cmd, token).ConfigureAwait(false);
            }
        }

        private static uint[] GetOffsets(uint startOffset, uint startData, uint size, uint count)
        {
            var offsets = new uint[count];
            for (uint i = 0; i < count; ++i)
                offsets[i] = (startOffset + startData) + (size * i);
            return offsets;
        }

        private static uint[] GetUnsafeOffsetsByChunkCount(uint startOffset, uint size, uint chunkCount)
        {
            var offsets = new uint[chunkCount];
            var chunkSize = size / chunkCount;
            for (uint i = 0; i < chunkCount; ++i)
                offsets[i] = startOffset + (i * chunkSize);
            return offsets;
        }
    }
}

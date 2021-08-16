using System;
using System.Threading;
using System.Threading.Tasks;
using SysBot.Base;

namespace ACNHMobileSpawner
{
    public static class UnsafeOffsets
    {
        private static readonly byte Magic1 = 0;
        private static readonly byte Magic2 = 0;
        public static bool CanMagic { get => Magic1 != 0 && Magic2 != 0; }
        private static ulong CurrentOnlineEncryptionConstant { get; set; } = 0;

        private static float GetOnlineFloatFromBytes(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private static byte DecryptOnlineSignedByte(byte encbyte)
        {
            throw new NotImplementedException();
        }

        private static async Task EnsureEncryptionConstant(ISwitchConnectionAsync connection, CancellationToken token)
        {
            var data = await connection.ReadBytesAsync((uint)SessionEncryptionAddress, 8, token).ConfigureAwait(false);
            CurrentOnlineEncryptionConstant = BitConverter.ToUInt64(data, 0);
        }

        public static async Task<float[]> GetDecryptedFloatsAt(ISwitchConnectionAsync connection, CancellationToken token, long[] jumps, int count)
        {
            await EnsureEncryptionConstant(connection, token);
            throw new NotImplementedException();
        }

        public static async Task SetAndEncryptFloats(ISwitchConnectionAsync connection, CancellationToken token, long[] jumps, float[] values)
        {
            await EnsureEncryptionConstant(connection, token);
            throw new NotImplementedException();
        }

        public static float Lerp(float firstFloat, float secondFloat, float by) => firstFloat * (1 - by) + secondFloat * by;

        public static readonly long[] OnlinePCJumps = Array.Empty<long>();
        public static readonly long[] OnlineMDCJumps = Array.Empty<long>();
        public static ulong SessionEncryptionAddress = 0;
        public static float[] IslandSpecificAreaBounds = new float[4] { 0, 0, 0, 0 };
    }
}

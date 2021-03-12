using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public enum OverworldState
    {
        Null,
        Overworld,
        Loading,
        UserArriveLeaving,
        Unknown
    }
    // Red's dodo retrieval and movement code. All credit to Red in the PKHeX support discord for the original version of the dodo-get function
    public class DodoPositionHelper
    {
        private const string DodoPattern = @"^[A-Z0-9]*$";
        private int ButtonClickTime => 0_900 + Config.DialogueButtonPressExtraDelay;

        private readonly ISwitchConnectionAsync Connection;
        private readonly CrossBot BotRunner;
        private readonly CrossBotConfig Config;
        private readonly Regex DodoRegex = new Regex(DodoPattern);

        public string DodoCode { get; set; } = "No code set yet."; 
        public byte[] InitialPlayerX { get; set; } = new byte[2];
        public byte[] InitialPlayerY { get; set; } = new byte[2];

        public DodoPositionHelper(CrossBot bot)
        {
            BotRunner = bot;
            Connection = BotRunner.SwitchConnection;
            Config = BotRunner.Config;
        }

        public async Task<ulong> FollowMainPointer(long[] jumps, bool canSolveOnSysmodule, CancellationToken token) //include the last jump here
        {
            // 1.7+ sys-botbase can solve entire pointer 
            if (canSolveOnSysmodule)
            {
                var jumpsWithoutLast = jumps.Take(jumps.Length - 1);

                byte[] command = Encoding.UTF8.GetBytes($"pointer{string.Concat(jumpsWithoutLast.Select(z => $" {z}"))}\r\n");

                byte[] socketReturn = await Connection.ReadRaw(command, sizeof(ulong) * 2 + 1, token).ConfigureAwait(false);
                var bytes = Base.Decoder.ConvertHexByteStringToBytes(socketReturn);
                bytes = bytes.Reverse().ToArray();

                var offset = (ulong)((long)BitConverter.ToUInt64(bytes, 0) + jumps[jumps.Length - 1]);
                return offset;
            }

            // solve pointer manually
            var ofs = (ulong)jumps[0]; // won't work with negative first jump
            var address = BitConverter.ToUInt64(await Connection.ReadBytesMainAsync(ofs, 0x8, token).ConfigureAwait(false), 0);
            for (int i = 1; i < jumps.Length - 1; ++i)
            {
                await Task.Delay(0_008, token).ConfigureAwait(false); // 1/4 frame
                var jump = jumps[i];
                if (jump > 0)
                    address += (ulong)jump;
                else
                    address -= (ulong)Math.Abs(jump);

                byte[] bytes = await Connection.ReadBytesAbsoluteAsync(address, 0x8, token).ConfigureAwait(false);
                address = BitConverter.ToUInt64(bytes, 0);
            }
            return address + (ulong)jumps[jumps.Length - 1];
        }

        public async Task CloseGate(uint Offset, CancellationToken token)
        {
            // Navigate through dialog with Dodo to close the gate, then inject empty dodo bytes
            await Task.Delay(0_500, token).ConfigureAwait(false);

            await BotRunner.Click(SwitchButton.A, 3_000, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            for (int i = 0; i < 5; ++i)
                await BotRunner.Click(SwitchButton.B, 1_000, token).ConfigureAwait(false);

            await Connection.WriteBytesAsync(new byte[5], Offset, token).ConfigureAwait(false);
            DodoCode = string.Empty;
        }

        public async Task GetDodoCode(ulong CoordinateAddress, uint Offset, bool isRetry, CancellationToken token)
        {
            if (Config.LegacyDodoCodeRetrieval)
            {
                await GetDodoCodeLegacy(CoordinateAddress, Offset, isRetry, token);
                return;
            }

            // Navigate through dialog with Dodo to open gates and to get Dodo code.
            await Task.Delay(0_500, token).ConfigureAwait(false);
            if (!isRetry)
                await BotRunner.ClickConversation(SwitchButton.A, 1_000, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, 2_000, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DDOWN, 0_500, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DDOWN, 0_500, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, 18_000 + Config.ExtraTimeConnectionWait, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await Task.Delay(0_100 + Config.DialogueButtonPressExtraDelay, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.UpdateBlocker(true, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await BotRunner.ClickConversation(SwitchButton.A, ButtonClickTime, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);

            // Clear incase opening the gate took too long
            for (int i = 0; i < 4; ++i)
                await BotRunner.ClickConversation(SwitchButton.B, 1_000, token).ConfigureAwait(false);
            await BotRunner.UpdateBlocker(false, token).ConfigureAwait(false);

            // Obtain Dodo code from offset and store it.	
            byte[] bytes = await Connection.ReadBytesAsync(Offset, 0x5, token).ConfigureAwait(false);
            DodoCode = Encoding.UTF8.GetString(bytes, 0, 5);
            LogUtil.LogInfo($"Retrieved Dodo code: {DodoCode}.", Config.IP);

            // Wait for loading screen.	
            await Task.Delay(2_000, token).ConfigureAwait(false);
        }

        public async Task<OverworldState> GetOverworldState(long[] jumps, bool canFollowPointers, CancellationToken token)
        {
            ulong coord = await FollowMainPointer(jumps, canFollowPointers, token).ConfigureAwait(false);
            return await GetOverworldState(coord, token).ConfigureAwait(false);
        }

        public async Task<OverworldState> GetOverworldState(ulong CoordinateAddress, CancellationToken token)
        {
            var x = BitConverter.ToUInt32(await Connection.ReadBytesAbsoluteAsync(CoordinateAddress + 0x20, 0x4, token).ConfigureAwait(false), 0);
            //LogUtil.LogInfo($"CurrentVal: {x:X8}", Config.IP);
            return GetOverworldState(x);
        }

        public static OverworldState GetOverworldState(uint val)
        {
            if ($"{val:X8}".EndsWith("C906"))
                return OverworldState.Loading;
            return val switch
            {
                0x00000000 => OverworldState.Null,
                0xC0066666 => OverworldState.Overworld,
                0xBE200000 => OverworldState.UserArriveLeaving,
                _          => OverworldState.Unknown
            };
        }

        public bool IsDodoValid(string dodoCode) => DodoRegex.IsMatch(dodoCode);

        // In case player keeps opening up gates on local play
        public async Task GetDodoCodeLegacy(ulong CoordinateAddress, uint Offset, bool isRetry, CancellationToken token)
        {
            // Navigate through dialog with Dodo to open gates and to get Dodo code.
            await Task.Delay(0_500, token).ConfigureAwait(false);
            var Hold = SwitchCommand.Hold(SwitchButton.L);
            await Connection.SendAsync(Hold, token).ConfigureAwait(false);
            await Task.Delay(0_700, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 4_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_000, token).ConfigureAwait(false);
            if (!isRetry)
                await BotRunner.Click(SwitchButton.A, 2_100, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DDOWN, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DDOWN, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 20_000 + Config.ExtraTimeConnectionWait, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_500, token).ConfigureAwait(false);
            await BotRunner.UpdateBlocker(true, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 3_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_000, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            var Release = SwitchCommand.Release(SwitchButton.L);
            await Connection.SendAsync(Release, token).ConfigureAwait(false);

            // Clear incase opening the gate took too long
            for (int i = 0; i < 6; ++i)
                await BotRunner.Click(SwitchButton.B, 1_000, token).ConfigureAwait(false);
            await BotRunner.UpdateBlocker(false, token).ConfigureAwait(false);

            // Obtain Dodo code from offset and store it.	
            byte[] bytes = await Connection.ReadBytesAsync(Offset, 0x5, token).ConfigureAwait(false);
            DodoCode = Encoding.UTF8.GetString(bytes, 0, 5);
            LogUtil.LogInfo($"Retrieved Dodo code: {DodoCode}.", Config.IP);

            // Wait for loading screen.	
            await Task.Delay(2_000, token).ConfigureAwait(false);
        }
    }
}

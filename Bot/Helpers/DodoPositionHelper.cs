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

        public DodoPositionHelper(CrossBot bot)
        {
            BotRunner = bot;
            Connection = BotRunner.SwitchConnection;
            Config = BotRunner.Config;
        }

        public async Task<ulong> FollowMainPointer(long[] jumps, CancellationToken token) //include the last jump here
        {
            var jumpsWithoutLast = jumps.Take(jumps.Length - 1);

            byte[] command = Encoding.UTF8.GetBytes($"pointer{string.Concat(jumpsWithoutLast.Select(z => $" {z}"))}\r\n");

            byte[] socketReturn = await Connection.ReadRaw(command, sizeof(ulong) * 2 + 1, token).ConfigureAwait(false);
            var bytes = Base.Decoder.ConvertHexByteStringToBytes(socketReturn);
            bytes = bytes.Reverse().ToArray();

            var offset = (ulong)((long)BitConverter.ToUInt64(bytes, 0) + jumps[jumps.Length - 1]);
            return offset;
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

            if (Globals.Bot.Config.AttemptMitigateDialogueWarping)
                await AttemptCheckForEndOfConversation(10, token).ConfigureAwait(false);

            await Connection.WriteBytesAsync(new byte[5], Offset, token).ConfigureAwait(false);
            DodoCode = string.Empty;
        }

        byte[] freezeBytes = Encoding.ASCII.GetBytes($"freeze 0x{ACNHMobileSpawner.OffsetHelper.TextSpeedAddress:X8} 0x03\r\n");
        byte[] unFreezeBytes = Encoding.ASCII.GetBytes($"unFreeze 0x{ACNHMobileSpawner.OffsetHelper.TextSpeedAddress:X8}\r\n");

        public async Task GetDodoCode(uint Offset, bool isRetry, CancellationToken token)
        {
            if (Config.ExperimentalFreezeDodoCodeRetrieval)
            {
                await GetDodoCodeGigafast(Offset, isRetry, token);
                return;
            }

            if (Config.LegacyDodoCodeRetrieval)
            {
                await GetDodoCodeLegacy(Offset, isRetry, token);
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

            if (Globals.Bot.Config.AttemptMitigateDialogueWarping)
                await AttemptCheckForEndOfConversation(10, token).ConfigureAwait(false);

            // Obtain Dodo code from offset and store it.	
            byte[] bytes = await Connection.ReadBytesAsync(Offset, 0x5, token).ConfigureAwait(false);
            DodoCode = Encoding.UTF8.GetString(bytes, 0, 5);
            LogUtil.LogInfo($"Retrieved Dodo code: {DodoCode}.", Config.IP);

            // Wait for loading screen.	
            await Task.Delay(2_000, token).ConfigureAwait(false);
        }

        public async Task<OverworldState> GetOverworldState(long[] jumps, CancellationToken token)
        {
            ulong coord = await FollowMainPointer(jumps, token).ConfigureAwait(false);
            return await GetOverworldState(coord, token).ConfigureAwait(false);
        }

        public async Task<OverworldState> GetOverworldState(ulong CoordinateAddress, CancellationToken token)
        {
            var x = BitConverter.ToUInt32(await Connection.ReadBytesAbsoluteAsync(CoordinateAddress + 0x20, 0x4, token).ConfigureAwait(false), 0);
            //LogUtil.LogInfo($"CurrentVal: {x:X8}", Config.IP);
            return GetOverworldState(x);
        }

        public static OverworldState GetOverworldState(uint val) => val switch
        {
            0x00000000 => OverworldState.Null,
            0xC0066666 => OverworldState.Overworld,
            0xBE200000 => OverworldState.UserArriveLeaving,
            _ when (val & 0xFFFF) == 0xC906 => OverworldState.Loading,
            _ when (val & 0xFFFF) == 0xAC10 => OverworldState.Loading,
            _ when (val & 0xFFFF) == 0x6B48 => OverworldState.Loading,
            _ => OverworldState.Unknown,
        };

        public bool IsDodoValid(string dodoCode) => DodoRegex.IsMatch(dodoCode);

        // In case player keeps opening up gates on local play
        public async Task GetDodoCodeLegacy(uint Offset, bool isRetry, CancellationToken token)
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

            if (Globals.Bot.Config.AttemptMitigateDialogueWarping)
                await AttemptCheckForEndOfConversation(10, token).ConfigureAwait(false);

            // Obtain Dodo code from offset and store it.	
            byte[] bytes = await Connection.ReadBytesAsync(Offset, 0x5, token).ConfigureAwait(false);
            DodoCode = Encoding.UTF8.GetString(bytes, 0, 5);
            LogUtil.LogInfo($"Retrieved Dodo code: {DodoCode}.", Config.IP);

            // Wait for loading screen.	
            await Task.Delay(2_000, token).ConfigureAwait(false);
        }

        public async Task GetDodoCodeGigafast(uint Offset, bool isRetry, CancellationToken token)
        {
            await BotRunner.SwitchConnection.SendRaw(freezeBytes, token).ConfigureAwait(false);

            // Navigate through dialog with Dodo to open gates and to get Dodo code.
            await Task.Delay(0_500, token).ConfigureAwait(false);

            var encodedBytesSequence = Encoding.ASCII.GetBytes("clickSeq " +
                $"A,W{(isRetry ? 2_000 : 3_100)}," +
                "A,W900," +
                "DD,W400," +
                "A,W1000," +
                "A,W550," +
                "DD,W400," +
                "A,W600," +
                "A,W550," +
                "A,W550," +
                "A,W550," +
                $"A,W{17_000 + Config.ExtraTimeConnectionWait}," +
                "A,W550," +
                "DU,W400," +
                "DU,W400," +
                "A,W800," +
                "A,W550," +
                "DU,W400," +
                "A,W550," +
                "A,W550," +
                "A,W550," +
                "A,W550," +
                "A,W550," +
                "A,W550," +
                "A,W550," +
                "\r\n");

            await BotRunner.UpdateBlocker(true, token).ConfigureAwait(false);

            var bytesRes = await BotRunner.SwitchConnection.ReadRaw(encodedBytesSequence, 6, token).ConfigureAwait(false);
            if (!Encoding.Default.GetString(bytesRes).ToLower().StartsWith("done"))
                LogUtil.LogInfo("FATAL ERROR", Config.IP);

            // Clear incase opening the gate took too long
            for (int i = 0; i < 9; ++i)
                await BotRunner.Click(SwitchButton.B, 0_500, token).ConfigureAwait(false);
            await BotRunner.UpdateBlocker(false, token).ConfigureAwait(false);

            await BotRunner.SwitchConnection.SendRaw(unFreezeBytes, token).ConfigureAwait(false);

            if (Globals.Bot.Config.AttemptMitigateDialogueWarping)
                await AttemptCheckForEndOfConversation(10, token).ConfigureAwait(false);

            // Obtain Dodo code from offset and store it.	
            byte[] bytes = await Connection.ReadBytesAsync(Offset, 0x5, token).ConfigureAwait(false);
            DodoCode = Encoding.UTF8.GetString(bytes, 0, 5);
            LogUtil.LogInfo($"Retrieved Dodo code: {DodoCode}.", Config.IP);

            // Wait for loading screen.	
            await Task.Delay(2_000, token).ConfigureAwait(false);
        }

        private async Task AttemptCheckForEndOfConversation(int maxChecks, CancellationToken token)
        {
            await Connection.WriteBytesAsync(new byte[1], (uint)ACNHMobileSpawner.OffsetHelper.TextSpeedAddress, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.B, 1_000, token).ConfigureAwait(false);

            for (int i = 0; i < maxChecks-1; ++i)
            {
                await BotRunner.Click(SwitchButton.B, 1_000, token).ConfigureAwait(false);
                var currentInstantTextState = await Connection.ReadBytesAsync((uint)ACNHMobileSpawner.OffsetHelper.TextSpeedAddress, 1, token).ConfigureAwait(false);
                if (currentInstantTextState[0] == 0)
                    break;
            }
        }
    }
}

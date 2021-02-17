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

        private readonly SwitchConnectionAsync Connection;
        private readonly CrossBot BotRunner;
        private readonly CrossBotConfig Config;
        private readonly Regex DodoRegex = new Regex(DodoPattern);

        public string DodoCode { get; set; } = "No code set yet."; 

        public DodoPositionHelper(CrossBot bot)
        {
            BotRunner = bot;
            Connection = BotRunner.Connection;
            Config = BotRunner.Config;
        }

        public async Task<ulong> GetCoordinateAddress(string pointer, CancellationToken token)
        {
            // Regex pattern to get operators and offsets from pointer expression.	
            string pattern = @"(\+|\-)([A-Fa-f0-9]+)";
            Regex regex = new Regex(pattern);
            Match match = regex.Match(pointer);

            // Get first offset from pointer expression and read address at that offset from main start.	
            var ofs = Convert.ToUInt64(match.Groups[2].Value, 16);
            var address = BitConverter.ToUInt64(await Connection.ReadBytesMainAsync(ofs, 0x8, token).ConfigureAwait(false), 0);
            match = match.NextMatch();

            // Matches the rest of the operators and offsets in the pointer expression.	
            while (match.Success)
            {
                // Get operator and offset from match.	
                string opp = match.Groups[1].Value;
                ofs = Convert.ToUInt64(match.Groups[2].Value, 16);

                // Add or subtract the offset from the current stored address based on operator in front of offset.	
                switch (opp)
                {
                    case "+":
                        address += ofs;
                        break;
                    case "-":
                        address -= ofs;
                        break;
                }

                // Attempt another match and if successful read bytes at address and store the new address.	
                match = match.NextMatch();
                if (match.Success)
                {
                    byte[] bytes = await Connection.ReadBytesAbsoluteAsync(address, 0x8, token).ConfigureAwait(false);
                    address = BitConverter.ToUInt64(bytes, 0);
                }
            }

            return address;
        }

        public async Task CloseGate(uint Offset, CancellationToken token)
        {
            // Navigate through dialog with Dodo to close the gate, then inject empty dodo bytes
            await Task.Delay(0_500, token).ConfigureAwait(false);
            var Hold = SwitchCommand.Hold(SwitchButton.L);
            await Connection.SendAsync(Hold, token).ConfigureAwait(false);
            await Task.Delay(0_700, token).ConfigureAwait(false);

            await BotRunner.Click(SwitchButton.A, 3_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 3_000, token).ConfigureAwait(false);
            for (int i = 0; i < 5; ++i)
                await BotRunner.Click(SwitchButton.B, 1_000, token).ConfigureAwait(false);

            await Task.Delay(0_500, token).ConfigureAwait(false);
            var Release = SwitchCommand.Release(SwitchButton.L);
            await Connection.SendAsync(Release, token).ConfigureAwait(false);

            await Connection.WriteBytesAsync(new byte[5], Offset, token).ConfigureAwait(false);
            DodoCode = string.Empty;
        }

        public async Task GetDodoCode(ulong CoordinateAddress, uint Offset, bool isRetry, CancellationToken token)
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
            await BotRunner.Click(SwitchButton.A, 20_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.DUP, 0_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 2_500, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
            await BotRunner.Click(SwitchButton.A, 1_500, token).ConfigureAwait(false);
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

            // Obtain Dodo code from offset and store it.	
            byte[] bytes = await Connection.ReadBytesAsync(Offset, 0x5, token).ConfigureAwait(false);
            DodoCode = Encoding.UTF8.GetString(bytes, 0, 5);
            LogUtil.LogInfo($"Retrieved Dodo code: {DodoCode}.", Config.IP);

            // Wait for loading screen.	
            while (await GetOverworldState(CoordinateAddress, token).ConfigureAwait(false) != OverworldState.Overworld)
                await Task.Delay(0_500, token).ConfigureAwait(false);
        }

        public async Task<OverworldState> GetOverworldState(string pointer, CancellationToken token)
        {
            ulong coord = await GetCoordinateAddress(pointer, token).ConfigureAwait(false);
            return await GetOverworldState(coord, token).ConfigureAwait(false);
        }

        public async Task<OverworldState> GetOverworldState(ulong CoordinateAddress, CancellationToken token)
        {
            var x = BitConverter.ToUInt32(await Connection.ReadBytesAbsoluteAsync(CoordinateAddress + 0x1E, 0x4, token).ConfigureAwait(false), 0);
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
    }
}

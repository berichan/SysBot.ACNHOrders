using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACNHMobileSpawner;
using System.Threading;

namespace SysBot.ACNHOrders
{
    public class VisitorListHelper
    {
        private const int VisitorNameSize = 0x14;
        private const int VisitorListSize = 8;

        private readonly ISwitchConnectionAsync Connection;
        private readonly CrossBot BotRunner;
        private readonly CrossBotConfig Config;

        private string[] Visitors = new string[VisitorListSize];
        public string VisitorFormattedString { get; private set; } = "Names not loaded.";

        public VisitorListHelper(CrossBot bot)
        {
            BotRunner = bot;
            Connection = BotRunner.SwitchConnection;
            Config = BotRunner.Config;
        }

        public async Task UpdateNames(CancellationToken token)
        {
            VisitorFormattedString = "The following visitors are on the island:\n";
            for (uint i = 0; i < VisitorListSize; ++i)
            {
                ulong offset = OffsetHelper.OnlineSessionVisitorAddress - (i * OffsetHelper.OnlineSessionVisitorSize);
                var bytes = await Connection.ReadBytesAsync((uint)offset, VisitorNameSize, token).ConfigureAwait(false);
                Visitors[i] = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                VisitorFormattedString += $"#{i + 1}: {(string.IsNullOrWhiteSpace(Visitors[i]) ? "Available slot" : Visitors[i])}\n";
            }
        }
    }
}

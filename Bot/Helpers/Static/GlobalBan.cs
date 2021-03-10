using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class Penalty
    {
        public string ID = string.Empty;
        public uint PenaltyCount = 0;

        public Penalty(string id, uint pcount)
        {
            ID = id;
            PenaltyCount = pcount;
        }

        public override string ToString() => ID;
    }

    public static class GlobalBan
    {
        private const string FilePath = "banlist.txt";

        private static int PenaltyCountBan = 0;
        private static readonly List<Penalty> PenaltyList = new();

        private static readonly object MapAccessor = new();

        public static void UpdateConfiguration(CrossBotConfig config)
        {
            PenaltyCountBan = config.OrderConfig.PenaltyBanCount;

            lock (MapAccessor)
            {
                loadBanList();
            }
        }

        public static bool Penalize(string id)
        {
            if (PenaltyCountBan < 1)
                return false;
            lock(MapAccessor)
            {
                var pen = PenaltyList.Find(x => x.ID == id);

                if (pen != null)
                {
                    pen.PenaltyCount++;
                }
                else
                {
                    pen = new Penalty(id, 1);
                    PenaltyList.Add(pen);
                }
                saveBanList();
                return pen.PenaltyCount >= PenaltyCountBan;
            }
        }

        public static bool IsBanned(string id)
        {
            if (PenaltyCountBan < 1)
                return false;
            lock (MapAccessor)
            {
                var pen = PenaltyList.Find(x => x.ID == id);

                if (pen != null)
                    return pen.PenaltyCount >= PenaltyCountBan;
            }

            return false;
        }

        public static void UnBan(string id)
        {
            lock (MapAccessor)
            {
                var pen = PenaltyList.Find(x => x.ID == id);

                if (pen == null)
                    return;

                PenaltyList.Remove(pen);
                saveBanList();
            }
        }

        public static void Ban(string id)
        {
            lock (MapAccessor)
            {
                var pen = PenaltyList.Find(x => x.ID == id);

                if (pen != null)
                {
                    pen.PenaltyCount = (uint)PenaltyCountBan;
                }
                else
                    PenaltyList.Add(new Penalty(id, (uint)PenaltyCountBan));

                saveBanList();
            }
        }

        // only call within a lock
        private static void saveBanList()
        {
            var allBans = PenaltyList.Where(x => x.PenaltyCount >= PenaltyCountBan).ToArray();
            if (allBans.Length < 1)
                return;

            var banIDs = new string[allBans.Length];
            for (int i = 0; i < banIDs.Length; ++i)
                banIDs[i] = allBans[i].ToString();

            var toWrite = string.Join("\r\n", banIDs);
            File.WriteAllText(FilePath, toWrite);
        }

        private static void loadBanList()
        {
            if (!File.Exists(FilePath))
            {
                var f = File.Create(FilePath);
                f.Close();
            }

            var bans = File.ReadAllLines(FilePath);

            foreach (var b in bans)
                if (PenaltyList.Find(x => x.ID == b) == null)
                    PenaltyList.Add(new Penalty(b, (uint)PenaltyCountBan));
        }
    }
}

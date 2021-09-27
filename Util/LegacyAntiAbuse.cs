using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public class LegacyIdentifier
    {
        public readonly string VillagerName;
        public readonly string TownName;
        public readonly string Identity;

        public LegacyIdentifier(string vName, string tName, string id)
        {
            VillagerName = vName;
            TownName = tName;
            Identity = id;
        }

        public override string ToString()
        {
            return $"{VillagerName},{TownName},{Identity}";
        }

        public static LegacyIdentifier? FromString(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return null;

            var splits = s.Split(',');
            if (splits.Length != 3)
                return null;

            return new LegacyIdentifier(splits[0], splits[1], splits[2]);
        }
    }

    public class LegacyAntiAbuse
    {
        private const string BanListUri = "https://raw.githubusercontent.com/berichan/SysBot.ACNHOrders/main/Resources/AbuseList.txt";

        private const string PathInfo = "userinfo.txt";
        private const string PathBans = "globalban.txt";

        public List<LegacyIdentifier> UserInfoList { get; private set; } = new();
        public List<LegacyIdentifier> GlobalBanList { get; private set; } = new();

        public static LegacyAntiAbuse CurrentInstance = new();

        public LegacyAntiAbuse()
        {
            if (!File.Exists(PathInfo))
            {
                var str = File.Create(PathInfo);
                str.Close();
            }

            LoadAllUserInfo();
        }

        private void SaveAllUserInfo()
        {
            string[] toSave = new string[UserInfoList.Count];
            for (int i = 0; i < UserInfoList.Count; ++i)
                toSave[i] = $"{UserInfoList[i]}\r\n";
            File.WriteAllLines(PathInfo, toSave);
        }

        private void LoadAllUserInfo()
        {
            UserInfoList.Clear();
            var txt = File.ReadAllText(PathInfo);
            var infos = txt.Split(new string[1] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var inf in infos)
            {
                var ident = LegacyIdentifier.FromString(inf);
                if (ident != null)
                    UserInfoList.Add(ident);
            }
        }

        private void UpdateGlobalBanList()
        {
            void LoadBanList()
            {
                GlobalBanList.Clear();
                var bytes = File.ReadAllBytes(PathBans);
                string bans = Encoding.UTF8.GetString(bytes);
                var infos = bans.Split(new string[3] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var inf in infos)
                {
                    var ident = LegacyIdentifier.FromString(inf);
                    if (ident != null)
                        GlobalBanList.Add(ident);
                }
            }

            void DownloadAndSetFile()
            {
                var bytes = NetUtil.DownloadFromUrlSync(BanListUri);
                File.WriteAllBytes(PathBans, bytes);
                LoadBanList();
            }

            if (!File.Exists(PathBans))
            {
                DownloadAndSetFile();
                return;
            }

            if (File.GetCreationTime(PathBans).Date != DateTime.Today)
            {
                DownloadAndSetFile();
                return;
            }

            if (GlobalBanList.Count < 1)
                LoadBanList();
        }

        public bool IsGlobalBanned(ulong id)
        {
            var idString = id.ToString();
            var found = GlobalBanList.FirstOrDefault(x => x.Identity.EndsWith(idString));
            return found != null;
        }

        /// <summary>
        /// Test arrival of user
        /// </summary>
        /// <returns>true:safe, false:abuser</returns>
        public bool LogUser(string user, string town, string id)
        {
            var exists = UserInfoList.FirstOrDefault(x => x.VillagerName == user && x.TownName == town && x.Identity != id);
            if (exists != default && exists.Identity != id)
            {
                LogUtil.LogInfo((Globals.Bot.Config.OrderConfig.PingOnAbuseDetection ? $"Pinging <@{Globals.Self.Owner}>: " : string.Empty) + $"{user} from {town} ({id}) exists with at least one previous identity: {exists.Identity}", Globals.Bot.Config.IP);
            }

            try { UpdateGlobalBanList(); } catch (Exception e) { LogUtil.LogInfo($"Unable to load banlist: {e.Message}", Globals.Bot.Config.IP); }
            
            var banned = GlobalBanList.FirstOrDefault(x => x.VillagerName == user && x.TownName == town);
            return banned == null;
        }

        public bool Remove(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;
            var exists = UserInfoList.FirstOrDefault(x => x.Identity.StartsWith(id));
            if (exists == default)
                return false;

            UserInfoList.Remove(exists);
            SaveAllUserInfo();
            return true;
        }
    }
}

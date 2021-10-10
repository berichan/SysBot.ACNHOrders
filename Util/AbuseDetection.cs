using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class HashNIDIdentifier<T>
    {
        public T HashIdentifier { get; set; } // must be serializable. uint: townid for acnh
        public ulong NIDIdentifier { get; set; }
        public string Identity { get; set; } // such as discord id

        public string PlaintextName { get; set; }

        public HashNIDIdentifier(T hashid, ulong nid, string userid, string name)
        {
            HashIdentifier = hashid;
            NIDIdentifier = nid;
            Identity = userid;
            PlaintextName = name;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public HashNIDIdentifier()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
        }

        public override string ToString() => JsonSerializer.Serialize(this, typeof(HashNIDIdentifier<T>));

        public static HashNIDIdentifier<T>? FromString(string s) => (HashNIDIdentifier<T>?)JsonSerializer.Deserialize(s, typeof(HashNIDIdentifier<T>));
    }

    public class AbuseDetection<T>
    {
        private const string BanListUri = "https://raw.githubusercontent.com/berichan/SysBot.ACNHOrders/main/Resources/NewAbuseList.txt";

        private const string PathInfo = "newuserinfo.txt";
        private const string PathBans = "newglobalban.txt";

        public List<HashNIDIdentifier<T>> UserInfoList { get; private set; } = new();
        public List<HashNIDIdentifier<T>> GlobalBanList { get; private set; } = new();

        public AbuseDetection()
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
            var infos = txt.Split(new string[3] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var inf in infos)
            {
                var ident = HashNIDIdentifier<T>.FromString(inf);
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
                    var ident = HashNIDIdentifier<T>.FromString(inf);
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

        public bool IsGlobalBanned(T hashid, ulong nid, string id) => GlobalBanList.Where(x => x.HashIdentifier != null && (x.HashIdentifier.Equals(hashid) || x.NIDIdentifier.Equals(nid) || x.Identity.Equals(id))).Any();

        public bool LogUser(T hashid, ulong nid, string id, string plaintext)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                var exists = UserInfoList.FirstOrDefault(x => x.HashIdentifier != null && x.HashIdentifier.Equals(hashid) && x.NIDIdentifier.Equals(nid) && x.Identity.Equals(id));
                if (exists == null)
                {
                    UserInfoList.Add(new HashNIDIdentifier<T>(hashid, nid, id, plaintext));
                    SaveAllUserInfo();
                }

                var altExists = UserInfoList.FirstOrDefault(x => x.HashIdentifier != null && (x.HashIdentifier.Equals(hashid) || x.NIDIdentifier.Equals(nid)) && !x.Identity.Equals(id));
                if (altExists != default && altExists.Identity != id)
                {
                    LogUtil.LogInfo((Globals.Bot.Config.OrderConfig.PingOnAbuseDetection ? $"Pinging <@{Globals.Self.Owner}>: " : string.Empty) + $"{plaintext} ({id}) exists with at least one previous identity: {altExists.Identity} ({altExists.PlaintextName})", Globals.Bot.Config.IP);
                }
            }

            try { UpdateGlobalBanList(); } catch (Exception e) { LogUtil.LogInfo($"Unable to load banlist: {e.Message}", Globals.Bot.Config.IP); }

            var banned = GlobalBanList.FirstOrDefault(x => x.HashIdentifier != null && (x.HashIdentifier.Equals(hashid) || x.NIDIdentifier.Equals(nid) || x.Identity.Equals(id)));
            return banned == null;
        }

        public bool Remove(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
                return false;
            var exists = UserInfoList.FirstOrDefault(x => x.Identity.StartsWith(identity));
            if (exists == default)
                return false;

            UserInfoList.Remove(exists);
            SaveAllUserInfo();
            return true;
        }
    }

    public class NewAntiAbuse : AbuseDetection<uint>
    {
        private static NewAntiAbuse? instance = null;
        public static NewAntiAbuse Instance
        {
            get
            {
                if (instance == null)
                    instance = new();
                return instance;
            }
        }

        public NewAntiAbuse():base(){}
    } 
}

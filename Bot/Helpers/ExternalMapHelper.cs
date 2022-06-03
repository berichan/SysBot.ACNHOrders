using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class ExternalMapHelper
    {
        private readonly string RootPathNHL;

        private Dictionary<string, byte[]> LoadedNHLs;

        private readonly bool CycleMap;
        private readonly int CycleTime;

        private DateTime LastCycleTime;
        private int LastCycledIndex = 0;

        public ExternalMapHelper(CrossBotConfig cfg)
        {
            RootPathNHL = cfg.FieldLayerNHLDirectory;
            LoadedNHLs = new Dictionary<string, byte[]>();

            if (!Directory.Exists(RootPathNHL))
                Directory.CreateDirectory(RootPathNHL);

            var files = Directory.EnumerateFiles(RootPathNHL);

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                if (info.Length == ACNHMobileSpawner.MapTerrainLite.ByteSize)
                    LoadedNHLs.Add(info.Name, File.ReadAllBytes(file));
            }

            CycleMap = cfg.DodoModeConfig.CycleNHLs;
            CycleTime = cfg.DodoModeConfig.CycleNHLMinutes;
            LastCycleTime = DateTime.Now;
        }

        public byte[]? GetNHL(string filename)
        {
            if (!filename.ToLower().EndsWith(".nhl"))
                filename += ".nhl";
            if (LoadedNHLs.ContainsKey(filename))
                return LoadedNHLs[filename];

            filename = Path.Combine(RootPathNHL, filename);
            if (File.Exists(filename))
            {
                var bytes = File.ReadAllBytes(filename);
                if (bytes.Length == ACNHMobileSpawner.MapTerrainLite.ByteSize)
                {
                    LoadedNHLs.Add(filename, bytes);
                    return bytes;
                }
            }

            return null;
        }

        public bool CheckForCycle(out MapOverrideRequest? request)
        {
            request = null;
            if (!CycleMap || LoadedNHLs.Count == 0)
                return false;

            var now = DateTime.Now;
            if ((now - LastCycleTime).TotalMinutes >= CycleTime)
            {
                LastCycleTime = now;
                LastCycledIndex = (LastCycledIndex + 1) % LoadedNHLs.Count;
                request = new MapOverrideRequest(nameof(ExternalMapHelper), LoadedNHLs.ElementAt(LastCycledIndex).Value);
                return true;
            }

            return false;
        }
    }
}

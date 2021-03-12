using NHSE.Core;
using System.IO;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public static class PresetLoader
    {
        public static Item[]? GetPreset(string nhiPath)
        {
            if (!File.Exists(nhiPath))
            {
                LogUtil.LogInfo($"{nhiPath} does not exist.", nameof(PresetLoader));
                return null;
            }

            var fileBytes = File.ReadAllBytes(nhiPath);
            if (fileBytes.Length > (Item.SIZE * 40) || fileBytes.Length == 0 || fileBytes.Length % 8 != 0)
            {
                LogUtil.LogInfo($"{nhiPath} is an invalid size for an NHI file.", nameof(PresetLoader));
                return null;
            }

            return Item.GetArray(fileBytes);
        }

        public static Item[]? GetPreset(OrderBotConfig cfg, string itemName, bool nhiOnly = true)
        {
            if (nhiOnly && !itemName.EndsWith(".nhi"))
                itemName += ".nhi";

            var path = Path.Combine(cfg.NHIPresetsDirectory, itemName);
            return GetPreset(path);
        }

        public static string[] GetPresets(OrderBotConfig cfg)
        {
            var files = Directory.GetFiles(cfg.NHIPresetsDirectory);
            var presets = new string[files.Length];
            for(int i = 0; i < files.Length; ++i)
                presets[i] = Path.GetFileNameWithoutExtension(files[i]);

            return presets;
        }
    }
}

using NHSE.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}

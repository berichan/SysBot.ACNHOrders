using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SysBot.ACNHOrders
{
    internal static class OrderPresetSelection
    {
        internal static string Id(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFileName(path))));
        internal static string? Resolve(IEnumerable<string> paths, string? id) => paths.FirstOrDefault(path => Id(path) == id);
    }
}

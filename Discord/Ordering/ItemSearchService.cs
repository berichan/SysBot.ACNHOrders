using System;
using System.Collections.Generic;
using System.Linq;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public static class ItemSearchService
    {
        public const int PageSize = 10;
        public static ComboItem[] Search(string query, string language = "en")
        {
            query = query.Trim();
            if (query.Length < 2 || !OrderPreparation.Languages.Contains(language)) return Array.Empty<ComboItem>();
            return OrderPreparation.GetStrings(language).ItemDataSource
                .Where(item => item.Value != Item.NONE && item.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Text.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 : item.Text.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(item => LevenshteinDistance.Compute(item.Text, query))
                .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<(int Value, string Name)> BodyVariants(Item item)
        {
            var index = ItemRemakeUtil.GetRemakeIndex(item.ItemId);
            if (index < 0) return Array.Empty<(int, string)>();
            var info = ItemRemakeInfoData.List[index];
            var lines = info.GetBodySummary(GameInfo.Strings).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var results = new List<(int, string)>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && char.IsDigit(trimmed[0]))
                {
                    int value = trimmed[0] - '0';
                    if (value <= 7) results.Add((value, trimmed.Trim()));
                }
            }
            return results;
        }

        public static IReadOnlyList<(int Value, string Name)> FabricVariants(Item item)
        {
            var index = ItemRemakeUtil.GetRemakeIndex(item.ItemId);
            if (index < 0) return Array.Empty<(int, string)>();
            var info = ItemRemakeInfoData.List[index];
            var results = new List<(int, string)>();
            for (int i = 0; i <= 7; i++)
            {
                var name = info.GetFabricDescription(i);
                if (!string.IsNullOrWhiteSpace(name) && name != "Invalid") results.Add((i, $"{i}={name}"));
            }
            return results;
        }
    }
}

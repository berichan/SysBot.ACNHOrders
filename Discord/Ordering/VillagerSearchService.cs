using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders
{
    public sealed record VillagerSearchResult(string Id, string Name);

    public static class VillagerSearchService
    {
        private static readonly ConcurrentDictionary<string, VillagerSearchResult[]> Catalogues = new();

        public static bool IsOrderable(string id) => !string.IsNullOrWhiteSpace(id)
            && VillagerResources.IsVillagerDataKnown(id.Trim().ToLowerInvariant())
            && !VillagerOrderParser.IsUnadoptable(id);

        internal static string Normalize(string text)
        {
            var builder = new StringBuilder();
            bool latin = false;
            foreach (var ch in text.Trim().Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                {
                    if (latin) continue;
                }
                else latin = ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00C0' and <= '\u024F';
                builder.Append(ch);
            }
            return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }

        public static VillagerSearchResult[] Search(string query, string language = "en")
        {
            if (!OrderPreparation.Languages.Contains(language)) return Array.Empty<VillagerSearchResult>();
            var catalogue = Catalogues.GetOrAdd(language, lang => OrderPreparation.GetStrings(lang).VillagerMap
                .Where(entry => IsOrderable(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => new VillagerSearchResult(entry.Key, entry.Value)).ToArray());
            var term = Normalize(query);
            return catalogue.Select(entry => (Entry: entry, Text: Normalize(entry.Name), Id: Normalize(entry.Id)))
                .Where(entry => term.Length == 0 || entry.Text.Contains(term, StringComparison.Ordinal) || entry.Id.Equals(term, StringComparison.Ordinal))
                .OrderBy(entry => entry.Text == term || entry.Id == term ? 0 : entry.Text.StartsWith(term, StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(entry => entry.Text, StringComparer.Ordinal).ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .Select(entry => entry.Entry).ToArray();
        }

        public static bool TryResolve(string text, string language, out string? id, out string error)
        {
            id = null;
            error = string.Empty;
            if (!OrderPreparation.Languages.Contains(language))
            { error = "Choose a supported language in Options."; return false; }
            if (!OrderPreparation.TryValidateVillager(text, out var input, out error)) return false;
            if (input == null) { error = "Choose a villager first."; return false; }
            var candidate = input.ToLowerInvariant();
            if (!IsOrderable(candidate))
            {
                var term = Normalize(input);
                var ids = OrderPreparation.GetStrings(language).VillagerMap.Where(entry => Normalize(entry.Value) == term).Select(entry => entry.Key).Distinct().ToArray();
                // English names from older commands and saved orders remain usable in every language.
                if (ids.Length == 0 && language != "en")
                    ids = OrderPreparation.GetStrings("en").VillagerMap.Where(entry => Normalize(entry.Value) == term).Select(entry => entry.Key).Distinct().ToArray();
                if (ids.Length > 1) { error = "More than one villager has that name. Use Find villager to choose one."; return false; }
                if (ids.Length == 0)
                {
                    error = VillagerOrderParser.IsUnadoptable(candidate) || VillagerResources.IsVillagerDataKnown(candidate)
                        ? "That villager cannot be ordered or adopted." : "That villager was not found. Use Find villager to choose one.";
                    return false;
                }
                candidate = ids[0];
            }
            if (!IsOrderable(candidate)) { error = "That villager cannot be ordered or adopted."; return false; }
            id = candidate;
            return true;
        }

        public static string Name(string id, string language)
        {
            if (OrderPreparation.Languages.Contains(language) && OrderPreparation.GetStrings(language).VillagerMap.TryGetValue(id, out var localized)) return localized;
            return OrderPreparation.GetStrings("en").VillagerMap.TryGetValue(id, out var english) ? english : id;
        }

        internal static string DisplaySelection(OrderDraft draft) => string.IsNullOrWhiteSpace(draft.Villager) ? "None selected"
            : TryResolve(draft.Villager, draft.Language, out var id, out _) ? Name(id!, draft.Language) : draft.Villager;

        internal static string? CanonicalSelection(string? text, string language) => string.IsNullOrWhiteSpace(text) ? null
            : TryResolve(text, language, out var id, out _) ? id : text.Trim();
    }
}

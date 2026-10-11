using System.Linq;

namespace SysBot.ACNHOrders
{
    internal static class VillagerOrderSelection
    {
        internal static bool CanSearch(CrossBotConfig config, out string error)
        {
            error = !config.AllowVillagerInjection ? "Villager orders are disabled by the host."
                : !config.AllowLookup ? "Villager search is disabled by the host. You can still enter an orderable villager's name or ID in Options." : string.Empty;
            return error.Length == 0;
        }

        internal static bool TryPick(OrderDraft draft, string id, CrossBotConfig config, out string error)
        {
            if (!CanSearch(config, out error)) return false;
            if (!VillagerSearchService.IsOrderable(id)) { error = "That villager cannot be ordered. Search again to choose an orderable villager."; return false; }
            draft.PendingVillager = id.Trim().ToLowerInvariant();
            return true;
        }

        internal static bool TryAdd(OrderDraft draft, CrossBotConfig config, out string error)
        {
            if (!CanSearch(config, out error)) return false;
            if (draft.PendingVillager == null) { error = "Choose a villager from the search results first."; return false; }
            if (!VillagerSearchService.IsOrderable(draft.PendingVillager)) { error = "That villager can no longer be ordered. Search again to choose another."; return false; }
            Set(draft, draft.PendingVillager);
            return true;
        }

        internal static void Set(OrderDraft draft, string? id)
        {
            draft.Villager = id;
            draft.PendingVillager = null;
            draft.Errors = draft.Errors.Where(error => error != OrderPreparation.VillagerLengthError).ToArray();
            if (draft.Errors.Length == 0) draft.Input = OrderPreparation.FormatInput(draft.Items);
        }

        internal static bool TryChangeLanguage(OrderDraft draft, string language, CrossBotConfig config, out string error)
        {
            if (!CanSearch(config, out error)) return false;
            if (!OrderPreparation.Languages.Contains(language)) { error = "Choose a supported language."; return false; }
            draft.Villager = VillagerSearchService.CanonicalSelection(draft.Villager, draft.Language);
            draft.Language = language;
            draft.VillagerSearch = string.Empty;
            return true;
        }

        internal static bool TryResolveOption(OrderDraft draft, string? text, string language, out string? id, out string error)
        {
            if (!OrderPreparation.TryValidateVillager(text, out id, out error) || id == null) return error.Length == 0;
            bool unchanged = draft.Villager != null && VillagerSearchService.Normalize(id) == VillagerSearchService.Normalize(VillagerSearchService.DisplaySelection(draft));
            return VillagerSearchService.TryResolve(unchanged ? draft.Villager! : id, unchanged ? draft.Language : language, out id, out error);
        }
    }
}

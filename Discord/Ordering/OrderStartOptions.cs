using System.Linq;

namespace SysBot.ACNHOrders
{
    internal static class OrderStartOptions
    {
        internal static bool TryApply(OrderDraft draft, string? villager, string? language, out string error)
        {
            error = string.Empty;
            var selectedLanguage = string.IsNullOrWhiteSpace(language) ? draft.Language : language.Trim().ToLowerInvariant();
            if (!OrderPreparation.Languages.Contains(selectedLanguage))
            { error = "Choose a supported language: " + string.Join(", ", OrderPreparation.Languages); return false; }
            if (!OrderPreparation.TryValidateVillager(villager, out var validated, out error)) return false;
            var selectedVillager = villager == null
                ? VillagerSearchService.CanonicalSelection(draft.Villager, draft.Language)
                : VillagerSearchService.CanonicalSelection(validated, selectedLanguage);
            draft.Language = selectedLanguage;
            draft.Villager = selectedVillager;
            return true;
        }
    }
}

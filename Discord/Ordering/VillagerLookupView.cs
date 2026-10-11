using System;
using System.Collections.Generic;
using System.Linq;
using Discord;

namespace SysBot.ACNHOrders
{
    internal sealed record VillagerLookupPage(Embed Embed, MessageComponent Components, int Page);

    internal static class VillagerLookupView
    {
        private static readonly Dictionary<string, string> Languages = new()
        {
            ["en"] = "English", ["jp"] = "日本語", ["fr"] = "Français", ["de"] = "Deutsch",
            ["es"] = "Español", ["it"] = "Italiano", ["ko"] = "한국어", ["chs"] = "简体中文", ["cht"] = "繁體中文"
        };
        private static string Short(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

        internal static Modal BuildSearchModal(string customId, string query) => new ModalBuilder().WithTitle("Find villager")
            .WithCustomId(customId).AddTextInput("Name (leave blank to browse all)", "query", required: false, maxLength: 100,
                value: string.IsNullOrEmpty(query) ? null : Short(query, 100)).Build();

        private static SelectMenuBuilder LanguageMenu(OrderDraft draft, string customId)
        {
            var menu = new SelectMenuBuilder().WithCustomId(customId).WithPlaceholder("Choose a language");
            foreach (var language in Languages) menu.AddOption(language.Value, language.Key, isDefault: draft.Language == language.Key);
            return menu;
        }

        internal static VillagerLookupPage Results(OrderDraft draft, int page, Func<string, int, string> id, Func<string, int, string> selectId)
        {
            var matches = VillagerSearchService.Search(draft.VillagerSearch, draft.Language);
            page = Math.Clamp(page, 0, Math.Max(0, (matches.Length - 1) / ItemSearchService.PageSize));
            var language = Languages.TryGetValue(draft.Language, out var name) ? name : draft.Language;
            var description = matches.Length == 0 ? "No orderable villagers found. Try a shorter name, change the language, or leave the search blank to browse all."
                : $"{matches.Length} orderable villagers, page {page + 1}/{(matches.Length + 9) / 10}.\nChoose a villager below. You can review the selection before adding it to your order.";
            description += $"\nLanguage: **{language}**. Selected villager: **{VillagerSearchService.DisplaySelection(draft)}**.";
            var embed = new EmbedBuilder().WithTitle(draft.VillagerSearch.Length == 0 ? "Find villager" : "Find villager: " + Short(draft.VillagerSearch, 80))
                .WithDescription(description).WithColor(Color.Blue).Build();
            var components = new ComponentBuilder();
            if (matches.Length > 0)
            {
                var menu = new SelectMenuBuilder().WithCustomId(selectId("villager-pick", page)).WithPlaceholder("Choose an orderable villager");
                foreach (var villager in matches.Skip(page * 10).Take(10))
                    menu.AddOption(Short(villager.Name, 100), villager.Id, description: "ID: " + villager.Id);
                components.WithSelectMenu(menu, 0);
            }
            components.WithSelectMenu(LanguageMenu(draft, selectId("villager-language", page)), 1)
                .WithButton("Search again", id("villager-search", page), ButtonStyle.Primary, row: 2)
                .WithButton("Your order", id("order", 0), row: 2);
            if (page > 0) components.WithButton("Previous", id("villager-results", page - 1), row: 2);
            if ((page + 1) * 10 < matches.Length) components.WithButton("Next", id("villager-results", page + 1), row: 2);
            return new(embed, components.Build(), page);
        }

        internal static VillagerLookupPage Selection(OrderDraft draft, Func<string, int, string> id, Func<string, int, string> selectId)
        {
            var name = VillagerSearchService.Name(draft.PendingVillager!, draft.Language);
            var description = "Press **Add to order** to save this villager with your item list. You need an **empty housing plot** to adopt them.\nItems are optional. Review and confirm your order to join the queue.";
            if (draft.Villager != null) description += $"\nThis replaces your current villager: **{VillagerSearchService.DisplaySelection(draft)}**.";
            var embed = new EmbedBuilder().WithTitle(name).WithDescription(description).WithColor(Color.Blue).Build();
            var components = new ComponentBuilder().WithSelectMenu(LanguageMenu(draft, selectId("villager-language", 0)), 0)
                .WithButton("Add to order", id("villager-add", 0), ButtonStyle.Success, row: 1)
                .WithButton("Back to results", id("villager-results", 0), row: 1)
                .WithButton("Your order", id("order", 0), row: 1).Build();
            return new(embed, components, 0);
        }
    }
}

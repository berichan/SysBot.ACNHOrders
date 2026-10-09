using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public sealed class ItemLookupAutocomplete : AutocompleteHandler
    {
        public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
            IAutocompleteInteraction interaction, IParameterInfo parameter, IServiceProvider services)
        {
            if (!Globals.Bot.Config.AllowLookup) return Task.FromResult(AutocompletionResult.FromSuccess());
            var language = interaction.Data.Options.FirstOrDefault(option => option.Name == "language")?.Value?.ToString() ?? "en";
            var query = interaction.Data.Current.Value?.ToString() ?? "";
            var suggestions = ItemSearchService.Search(query, language).Take(25)
                .Where(item => item.Text.Length <= 100)
                .Select(item => new AutocompleteResult(item.Text, item.Text));
            return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
        }
    }
}

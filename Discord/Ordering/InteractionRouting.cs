using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Discord;

namespace SysBot.ACNHOrders
{
    public static class InteractionRouting
    {
        public static bool OwnsCustomId(string customId, string? suffix, IEnumerable<string> patterns)
            => FindPattern(customId, suffix, patterns) != null;

        public static string? FindPattern(string customId, string? suffix, IEnumerable<string> patterns)
        {
            var names = patterns.ToHashSet(StringComparer.Ordinal);
            // Modules expose both base and suffixed handlers. Match only the base contracts.
            var baseNames = names.Where(name => !name.EndsWith(":*", StringComparison.Ordinal) || !names.Contains(name[..^2]));
            if (!string.IsNullOrEmpty(suffix))
            {
                var ending = ":" + suffix;
                if (!customId.EndsWith(ending, StringComparison.Ordinal)) return null;
                customId = customId[..^ending.Length];
            }
            var matched = baseNames.FirstOrDefault(pattern => Regex.IsMatch(customId, "\\A" + Regex.Escape(pattern).Replace("\\*", "[^:]+") + "\\z"));
            return matched == null ? null : string.IsNullOrEmpty(suffix) ? matched : matched + ":*";
        }

        public static void BindMatches(IInteractionContext context, string customId, string pattern)
        {
            var match = Regex.Match(customId, "\\A" + Regex.Escape(pattern).Replace("\\*", "([^:]+)") + "\\z");
            if (!match.Success) throw new InvalidOperationException("Interaction route does not match the selected handler.");
            // Direct metadata execution needs the captures normally set by InteractionService's search pipeline.
            if (context is IRouteMatchContainer container)
                container.SetSegmentMatches(match.Groups.Cast<Group>().Skip(1).Select(group => new InteractionRouteMatch(group.Value)));
        }
    }

    internal sealed record InteractionRouteMatch(string Value) : IRouteSegmentMatch;
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders
{
    public enum OrderFillMode { Standard, Exact, Catalogue }

    public sealed record ParsedOrderInput(Item[] Items, string[] Errors, string? Villager, string Language, OrderFillMode? Mode);
    public sealed record PreparedOrder(MultiItem Delivery, Item[] RequestedItems, Item[] VisibleItems, VillagerRequest? Villager, OrderFillMode Mode);

    /// <summary>Used by order commands and panel buttons. Preview and confirmation use the same delivery array.</summary>
    public static class OrderPreparation
    {
        public static readonly string[] Languages = { "en", "jp", "fr", "de", "es", "it", "ko", "chs", "cht" };
        public const int MaxVillagerLength = 100;
        public static string VillagerLengthError => $"Villager names or IDs must be {MaxVillagerLength} characters or fewer. Shorten the value in Paste / edit list or Options.";
        public static GameStrings GetStrings(string language) => GameInfo.GetStrings(language switch
        { "chs" => "zhs", "cht" => "zht", _ => language });

        public static string FormatInput(IEnumerable<Item> items) => string.Join("\n", items.Select(item => $"0x{item.RawValue:X16}"));

        public static bool TryValidateVillager(string? text, out string? villager, out string error)
        {
            villager = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            error = string.Empty;
            if (villager?.Length > MaxVillagerLength)
            {
                villager = null;
                error = VillagerLengthError;
                return false;
            }
            return true;
        }

        public static string FillInstructions(PreparedOrder order) => order.VisibleItems.Length == 0 && order.Villager != null
            ? "Villager only: there are no items to collect. You need an empty housing plot to adopt the villager."
            : (order.Mode switch
        {
            OrderFillMode.Exact => "Exact: keeps your selected items and variants. Other pickup slots stay empty.",
            OrderFillMode.Catalogue when !order.Delivery.ItemArray.Items.Any(item => item.IsNone) =>
                "Catalogue: all 40 slots are requested items. Collect all items. There is no separator or extra filler.",
            OrderFillMode.Catalogue => "Catalogue: collect the items before the separator. Leave the extra copies beyond it.",
            _ => "Standard: fills 40 pickup slots with available variants."
        }) + "\nAll stackable items use full stacks in every mode.";

        public static Item[] FullStacks(IEnumerable<Item> source)
        {
            var items = Clone(source);
            MultiItem.StackToMax(items);
            return items;
        }

        public static Item[] Clone(IEnumerable<Item> items) => items.Select(item =>
        {
            var copy = new Item();
            copy.CopyFrom(item);
            return copy;
        }).ToArray();

        public static ParsedOrderInput Parse(string input, DropBotConfig config, string prefix = "$", string language = "en")
        {
            language = language.Trim().ToLowerInvariant();
            var text = input.Trim();
            foreach (var candidate in new[] { prefix, "$", "!", "/" }.Where(p => !string.IsNullOrEmpty(p)).Distinct())
                if (text.StartsWith(candidate, StringComparison.Ordinal)) { text = text[candidate.Length..].TrimStart(); break; }

            OrderFillMode? mode = null;
            var command = Regex.Match(text, @"^(ordercat|order)(?:_[\w-]+)?(?:\s+|$)", RegexOptions.IgnoreCase);
            if (command.Success)
            {
                mode = command.Groups[1].Value.Equals("ordercat", StringComparison.OrdinalIgnoreCase) ? OrderFillMode.Catalogue : OrderFillMode.Standard;
                text = text[command.Length..].Trim();
            }
            if (text.StartsWith("items:", StringComparison.OrdinalIgnoreCase)) text = text[6..].TrimStart();
            string? villager = null;
            var errors = new List<string>();
            var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Legacy commands allow Villager: at the start or directly after an item or comma.
            // Values can span lines, and each option ends at the next option or end of input.
            text = Regex.Replace(text, @"(villager|(?<![^\s,])language):\s*(.*?)(?=(?:villager|(?<![^\s,])language):|$)", match =>
            {
                var option = match.Groups[1].Value;
                var value = match.Groups[2].Value.Trim().Trim(',').Trim();
                if (!options.Add(option)) errors.Add($"Use only one {option.ToLowerInvariant()}: option per order.");
                if (option.Equals("villager", StringComparison.OrdinalIgnoreCase))
                {
                    villager = value;
                    if (value.Length == 0) errors.Add("Enter a villager name or ID after villager:.");
                }
                else language = value.ToLowerInvariant();
                return "";
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var segments = text.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (segments.Count > 1 && Languages.Contains(segments[0].ToLowerInvariant()))
            {
                language = segments[0].ToLowerInvariant();
                segments.RemoveAt(0);
            }
            if (!TryValidateVillager(villager, out villager, out var villagerError)) errors.Add(villagerError);
            var items = new List<Item>();
            if (!Languages.Contains(language))
            {
                errors.Add($"Choose a supported language: {string.Join(", ", Languages)}.");
                return new(Array.Empty<Item>(), errors.ToArray(), villager, language, mode);
            }
            var strings = GetStrings(language).ItemDataSource;
            foreach (var segment in segments)
            {
                // Names such as "bed" must be resolved before trying hexadecimal input.
                var named = ItemParser.GetItem(segment, strings);
                if (!named.IsNone) { items.Add(named); continue; }
                var tokens = segment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.All(token => Regex.IsMatch(token, @"\A(?:0x)?[0-9a-fA-F]{1,16}\z")))
                {
                    foreach (var token in tokens)
                    {
                        var hex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
                        try
                        {
                            var parsed = ItemParser.GetItemsFromUserInput(hex, config, ItemDestination.FieldItemDropped).ToArray();
                            if (parsed.Length != 1 || parsed[0].IsNone || !IsKnown(parsed[0])) errors.Add($"Unknown item: {token}");
                            else items.Add(parsed[0]);
                        }
                        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or IndexOutOfRangeException)
                        { errors.Add($"Invalid item code: {token}"); }
                    }
                }
                else errors.Add($"Could not find \"{segment}\". Use Find items, or separate full item names with commas.");
            }
            if (items.Count == 0 && villager == null && errors.Count == 0) errors.Add("Add at least one item or choose a villager.");
            if (items.Count > MultiItem.MaxOrder) errors.Add($"You entered {items.Count} items. The limit is {MultiItem.MaxOrder}. Remove some before confirming.");
            return new(items.ToArray(), errors.ToArray(), villager, language, mode);
        }

        public static bool IsKnown(Item item) => GameInfo.Strings.ItemDataSource.Any(entry => entry.Value == item.ItemId);

        public static bool TryPrepare(IEnumerable<Item> source, OrderFillMode mode, CrossBotConfig config,
            string username, string? villagerName, out PreparedOrder? prepared, out string error, string language = "en")
        {
            prepared = null;
            error = string.Empty;
            if (!TryValidateVillager(villagerName, out villagerName, out error)) return false;
            var items = Clone(source);
            var hasItems = items.Any(item => !item.IsNone);
            if (!hasItems && villagerName == null) { error = "Add at least one item or choose a villager before confirming."; return false; }
            if (items.Length > MultiItem.MaxOrder) { error = $"The limit is {MultiItem.MaxOrder} items. Remove some before confirming."; return false; }
            if (!InternalItemTool.CurrentInstance.IsSaneAfterCorrection(items, config.DropConfig))
            { error = "Remove these unsafe items: " + string.Join(", ", InternalItemTool.CurrentInstance.GetUnsafeItemNames(items)); return false; }
            if (items.Any(item => !item.IsNone && !IsKnown(item)))
            { error = "This order contains an unknown item ID. Edit the list or import a valid inventory file."; return false; }

            VillagerRequest? villager = null;
            if (!string.IsNullOrWhiteSpace(villagerName))
            {
                if (!config.AllowVillagerInjection) { error = "Villager orders are disabled by the host."; return false; }
                if (!VillagerSearchService.TryResolve(villagerName, language, out var internalName, out error)) return false;
                villager = new VillagerRequest(username, VillagerResources.GetVillager(internalName!), 0, VillagerSearchService.Name(internalName!, language));
            }

            MultiItem delivery;
            Item[] visible;
            if (!hasItems)
            {
                // MultiItem's fill modes require a source item. Use empty pickup slots for villager-only visits.
                delivery = new MultiItem(Enumerable.Range(0, MultiItem.MaxOrder).Select(_ => new Item(Item.NONE)).ToArray(), true, false, true);
                visible = Array.Empty<Item>();
            }
            else if (mode == OrderFillMode.Exact)
            {
                var padded = Clone(items).Concat(Enumerable.Range(items.Length, MultiItem.MaxOrder - items.Length).Select(_ => new Item(Item.NONE))).ToArray();
                delivery = new MultiItem(padded, true, false, true);
                visible = Clone(items.Where(item => !item.IsNone));
            }
            else
            {
                delivery = new MultiItem(Clone(items), mode == OrderFillMode.Catalogue, true, true);
                visible = mode == OrderFillMode.Catalogue
                    ? Clone(delivery.ItemArray.Items.TakeWhile(item => !item.IsNone))
                    : Clone(delivery.ItemArray.Items.Where(item => !item.IsNone));
            }
            if (!InternalItemTool.CurrentInstance.IsSaneAfterCorrection(delivery.ItemArray.Items.ToArray(), config.DropConfig))
            { error = "This mode added an unsafe item. Choose Exact or edit the list before confirming."; return false; }
            // Corrections must be reflected in both preview and delivery.
            visible = mode == OrderFillMode.Catalogue ? Clone(delivery.ItemArray.Items.TakeWhile(item => !item.IsNone))
                : Clone(delivery.ItemArray.Items.Where(item => !item.IsNone));
            prepared = new(delivery, items, visible, villager, mode);
            return true;
        }

        public static string Describe(Item item)
        {
            var name = GameInfo.Strings.GetItemName(item);
            var remake = ItemRemakeUtil.GetRemakeIndex(item.ItemId);
            if (remake >= 0)
            {
                var body = ItemSearchService.BodyVariants(item).FirstOrDefault(variant => variant.Value == item.BodyType);
                if (body.Name != null) name += $" ({body.Name[(body.Name.IndexOf('=') + 1)..]})";
                if (item.PatternChoice != 0)
                    name += $" ({ItemRemakeInfoData.List[remake].GetFabricDescription(item.PatternChoice)})";
            }
            if (ItemInfo.TryGetMaxStackCount(item, out var maximum) && maximum > 1) name += $" ×{item.Count + 1}";
            return name;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    /// <summary>Displays item selection, order review, and queue status for Discord commands and buttons.</summary>
    internal sealed class OrderExperience
    {
        private readonly SocketInteractionContext _context;
        private readonly CrossBotConfig _config;
        private ulong Guild => _context.Guild?.Id ?? 0;
        private ulong User => _context.User.Id;
        private OrderDraft Draft => OrderDraftStore.Shared.Get(Guild, User);
        private OrderDraft QuickDraft => OrderDraftStore.Quick.Get(Guild, User);
        public OrderExperience(SocketInteractionContext context) { _context = context; _config = Globals.Bot.Config; }

        private string Id(string action, OrderDraft? draft = null, int page = 0) => Globals.Self.GetInteractionCustomId(
            $"shop:{action}:{draft?.Token ?? "home"}:{(draft == null ? page.ToString(CultureInfo.InvariantCulture) : $"{draft.Revision}-{page}")}");
        private string SelectId(string action, OrderDraft draft, int page = 0) => Globals.Self.GetInteractionCustomId(
            $"shop-select:{action}:{draft.Token}:{draft.Revision}-{page}");

        private Task Reply(string text = "", Embed? embed = null, MessageComponent? components = null)
        {
            if (_context.Interaction.HasResponded)
                return _context.Interaction.ModifyOriginalResponseAsync(properties =>
                { properties.Content = text; properties.Embed = embed; properties.Components = components ?? new ComponentBuilder().Build(); properties.AllowedMentions = AllowedMentions.None; });
            // Update the same private message as the member edits their order. Public panel clicks always create a private response.
            if (_context.Interaction is SocketMessageComponent component && component.Message.Flags is MessageFlags flags && (flags & MessageFlags.Ephemeral) != 0)
                return component.UpdateAsync(properties =>
                { properties.Content = text; properties.Embed = embed; properties.Components = components ?? new ComponentBuilder().Build(); properties.AllowedMentions = AllowedMentions.None; });
            return _context.Interaction.RespondAsync(text, embed: embed, components: components, ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        private Task Defer() => PrivateInteractionResponse.DeferAsync(_context.Interaction);
        private void Changed(OrderDraft draft) => OrderDraftStore.Shared.Changed(Guild, User, draft);
        private static string Short(string text, int length = 100) => text.Length <= length ? text : text[..(length - 1)] + "…";
        private EmbedBuilder Embed(string title, string description) => new EmbedBuilder().WithTitle(title).WithDescription(description).WithColor(Color.Blue);

        public async Task StartOrder(string? input = null, string? villager = null, string? language = null, OrderFillMode? mode = null)
        {
            await Defer().ConfigureAwait(false);
            var draft = Draft;
            await draft.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrWhiteSpace(input))
                {
                    ApplyInput(draft, input, language);
                    if (villager != null)
                    {
                        if (!OrderPreparation.TryValidateVillager(villager, out var validated, out var error)) draft.Errors = draft.Errors.Append(error).ToArray();
                        draft.Villager = validated;
                    }
                    if (mode.HasValue) draft.Mode = mode.Value;
                    Changed(draft);
                    await Review(draft).ConfigureAwait(false);
                }
                else
                {
                    if (mode.HasValue && draft.Mode != mode.Value) { draft.Mode = mode.Value; Changed(draft); }
                    await ShowDraft(draft).ConfigureAwait(false);
                }
            }
            finally { draft.Gate.Release(); }
        }

        private void ApplyInput(OrderDraft draft, string input, string? language = null)
        {
            var parsed = OrderPreparation.Parse(input, _config.DropConfig, _config.Prefix, language ?? draft.Language);
            draft.Input = input;
            draft.Items = OrderPreparation.FullStacks(parsed.Items);
            draft.Errors = parsed.Errors;
            draft.Language = OrderPreparation.Languages.Contains(parsed.Language) ? parsed.Language : "en";
            if (parsed.Villager != null) draft.Villager = parsed.Villager;
            if (parsed.Mode.HasValue) draft.Mode = parsed.Mode.Value;
        }

        public async Task ImportFile(IAttachment file)
        {
            await Defer().ConfigureAwait(false);
            if (!file.Filename.EndsWith(".nhi", StringComparison.OrdinalIgnoreCase) || file.Size % Item.SIZE != 0)
            { await Reply("Choose a valid .nhi inventory file with up to 40 items."); return; }
            var result = await NetUtil.DownloadNHIAsync(file).ConfigureAwait(false);
            if (!result.Success || result.Data == null) { await Reply("That file could not be read. Choose a valid .nhi inventory file."); return; }
            await SetItems(result.Data, OrderFillMode.Exact).ConfigureAwait(false);
        }

        public async Task ImportPreset(string name, string? villager = null)
        {
            await Defer().ConfigureAwait(false);
            var file = Presets().FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (file == null) { await Reply("That preset was not found. Open Place order (guided) → Presets to choose one."); return; }
            var items = PresetLoader.GetPreset(file);
            if (items == null) { await Reply("The host needs to repair that preset file. Your item list was saved."); return; }
            await SetItems(items, OrderFillMode.Exact, villager).ConfigureAwait(false);
        }

        private async Task SetItems(Item[] items, OrderFillMode mode, string? villager = null)
        {
            if (!OrderPreparation.TryValidateVillager(villager, out villager, out var error)) { await Reply(error); return; }
            var draft = Draft;
            await draft.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                draft.Items = OrderPreparation.FullStacks(items.Where(item => !item.IsNone));
                draft.Input = OrderPreparation.FormatInput(draft.Items);
                draft.Errors = Array.Empty<string>();
                draft.Mode = mode;
                draft.Villager = villager;
                Changed(draft);
                await Review(draft).ConfigureAwait(false);
            }
            finally { draft.Gate.Release(); }
        }

        private string[] Presets()
        {
            Directory.CreateDirectory(_config.OrderConfig.NHIPresetsDirectory);
            return Directory.GetFiles(_config.OrderConfig.NHIPresetsDirectory, "*.nhi").OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public async Task Search(string query, string language = "en")
        {
            await Defer().ConfigureAwait(false);
            if (!_config.AllowLookup) { await Reply("Item search is disabled by the host. You can still paste an item list."); return; }
            if (!OrderPreparation.Languages.Contains(language)) { await Reply("Choose a supported language: " + string.Join(", ", OrderPreparation.Languages)); return; }
            var draft = Draft;
            await draft.Gate.WaitAsync().ConfigureAwait(false);
            try { draft.Search = query.Trim(); draft.Language = language; Changed(draft); await Results(draft, 0).ConfigureAwait(false); }
            finally { draft.Gate.Release(); }
        }

        public async Task Handle(string action, string token, string value, IReadOnlyCollection<string>? selections = null)
        {
            if (action == "quick") { await OpenQuick().ConfigureAwait(false); return; }
            if (action == "my") { await MyOrder().ConfigureAwait(false); return; }
            if (action == "help") { await Help().ConfigureAwait(false); return; }
            if (action == "cancel" || action == "cancel-confirm") { await Cancel(action == "cancel-confirm", token, value).ConfigureAwait(false); return; }
            if (action == "dm")
            {
                await Defer().ConfigureAwait(false);
                try { await _context.User.SendMessageAsync("DM test successful. Your order has not been submitted. Return to the order panel to confirm it."); await Reply("DMs are working. Review your order and confirm when ready.", components: new ComponentBuilder().WithButton("Review order", Id("review", Draft)).Build()); }
                catch (Exception ex) { SysBot.Base.LogUtil.LogError($"DM test failed for {User}: {ex.Message}", nameof(OrderExperience)); await Reply("I could not send a DM. Enable direct messages from members of this server, then try Test DMs again. Your item list was saved."); }
                return;
            }
            if (action == "file") { await Reply($"Use `/{Globals.Self.GetSlashCommandName("order-nhi")}` and choose your .nhi file in the **file** option. You can review the items before confirming."); return; }

            bool opensModal = action is "search" or "paste" or "options" or "quantity";
            if (!opensModal) await Defer().ConfigureAwait(false);
            var draft = Draft;
            if (!await OrderInteractionGate.EnterAsync(draft.Gate, opensModal, Defer,
                () => _context.Interaction.RespondAsync("Your previous action is still finishing. Try again in a moment.", ephemeral: true, allowedMentions: AllowedMentions.None)).ConfigureAwait(false)) return;
            try
            {
                int page = 0;
                if (token != "home")
                {
                    var parts = value.Split('-');
                    if (!int.TryParse(parts[0], out var revision) || !OrderDraftStore.Shared.Matches(draft, token, revision))
                    {
                        if (action == "confirm" && (Globals.Hub.Orders.GetByUserId(User) != null || OrderStatusStore.Shared.IsActive(User)))
                            await MyOrder("Your order has already been accepted.");
                        else await ShowDraft(draft, notice: "Your item list has changed. Use the buttons below to continue.");
                        return;
                    }
                    if (parts.Length > 1) int.TryParse(parts[1], out page);
                }
                switch (action)
                {
                    case "order": await ShowDraft(draft, page); break;
                    case "search": await OpenSearch(draft); break;
                    case "results": await Results(draft, page); break;
                    case "paste": await OpenPaste(draft); break;
                    case "options": await OpenOptions(draft); break;
                    case "review": await Review(draft, page); break;
                    case "confirm": await Confirm(draft); break;
                    case "clear":
                        draft.Items = Array.Empty<Item>(); draft.Input = ""; draft.Errors = Array.Empty<string>(); draft.Villager = null;
                        Changed(draft); await ShowDraft(draft); break;
                    case "mode":
                        if (Enum.TryParse<OrderFillMode>(selections?.FirstOrDefault(), out var mode) && Enum.IsDefined(mode))
                        { draft.Mode = mode; Changed(draft); }
                        await Review(draft); break;
                    case "pick":
                        if (ushort.TryParse(selections?.FirstOrDefault(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                        { draft.PendingItem = OrderPreparation.FullStacks(new[] { new Item(id) })[0]; draft.PendingQuantity = 1; Changed(draft); await Pick(draft); }
                        else await Results(draft, page);
                        break;
                    case "variant":
                        if (draft.PendingItem != null && int.TryParse(selections?.FirstOrDefault(), out var body) && ItemSearchService.BodyVariants(draft.PendingItem).Any(variant => variant.Value == body))
                        { draft.PendingItem.BodyType = body; draft.Mode = OrderFillMode.Exact; Changed(draft); await Pick(draft); }
                        else await Results(draft, 0);
                        break;
                    case "amount": await Pick(draft); break;
                    case "quantity":
                        if (draft.PendingItem == null) { await Results(draft, 0); break; }
                        if (draft.Items.Length >= MultiItem.MaxOrder) { await ShowDraft(draft, notice: "Your order already has 40 items. Remove an item first."); break; }
                        await _context.Interaction.RespondWithModalAsync(LookupOrderSelection.BuildQuantityModal(
                            Globals.Self.GetInteractionCustomId($"shop-quantity:{draft.Token}:{draft.Revision}"), draft));
                        break;
                    case "fabric":
                        if (draft.PendingItem != null && int.TryParse(selections?.FirstOrDefault(), out var fabric) && ItemSearchService.FabricVariants(draft.PendingItem).Any(variant => variant.Value == fabric))
                        { draft.PendingItem.PatternChoice = fabric; draft.Mode = OrderFillMode.Exact; Changed(draft); await Pick(draft); }
                        else await Results(draft, 0);
                        break;
                    case "add":
                        if (draft.Errors.Length > 0) { await ShowDraft(draft, notice: "Fix the pasted list before adding or removing items. Open Paste / edit list to correct it."); break; }
                        if (draft.PendingItem == null) { await Results(draft, 0); break; }
                        if (!LookupOrderSelection.TryAdd(draft, out var added, out var addError)) { await Pick(draft, addError); break; }
                        Changed(draft); await ShowDraft(draft, notice: $"Added {added} {(added == 1 ? "item" : "items")}. Find more items or review your order."); break;
                    case "remove":
                        if (draft.Errors.Length > 0) { await ShowDraft(draft, notice: "Fix the pasted list before adding or removing items. Open Paste / edit list to correct it."); break; }
                        if (int.TryParse(selections?.FirstOrDefault(), out var index) && index >= 0 && index < draft.Items.Length)
                        { draft.Items = draft.Items.Where((_, i) => i != index).ToArray(); draft.Input = OrderPreparation.FormatInput(draft.Items); Changed(draft); }
                        await ShowDraft(draft, page); break;
                    case "presets": await ShowPresets(draft, page); break;
                    case "preset":
                        var files = Presets();
                        var selectedFile = OrderPresetSelection.Resolve(files, selections?.FirstOrDefault());
                        if (selectedFile != null)
                        {
                            var items = PresetLoader.GetPreset(selectedFile);
                            if (items == null) { await ShowDraft(draft, notice: "The host needs to repair that preset. Your item list was saved."); break; }
                            draft.Items = OrderPreparation.FullStacks(items.Where(item => !item.IsNone)); draft.Mode = OrderFillMode.Exact;
                            draft.Errors = Array.Empty<string>(); draft.Input = OrderPreparation.FormatInput(draft.Items);
                            Changed(draft); await Review(draft);
                        }
                        else await ShowDraft(draft, notice: "That preset is no longer available. Open Presets to choose one again.");
                        break;
                    case "again": await LoadLast(draft); break;
                    default: await ShowDraft(draft); break;
                }
            }
            finally { draft.Gate.Release(); }
        }

        private async Task ShowDraft(OrderDraft draft, int page = 0, string? notice = null)
        {
            page = Math.Clamp(page, 0, Math.Max(0, (draft.Items.Length - 1) / ItemSearchService.PageSize));
            var lines = draft.Items.Skip(page * ItemSearchService.PageSize).Take(ItemSearchService.PageSize)
                .Select((item, index) => $"{page * ItemSearchService.PageSize + index + 1}. {OrderPreparation.Describe(item)}");
            var description = draft.Items.Length == 0 ? "Find items by name, paste a list, or choose a host preset. Your item list is saved. Review it when you are ready to order." : string.Join("\n", lines);
            if (draft.Errors.Length > 0) description += "\n\n**Please check these items:**\n" + Short(string.Join("\n", draft.Errors), 1200);
            if (notice != null) description = notice + "\n\n" + description;
            var embed = Embed($"Your items: {draft.Items.Length}/40", Short(description, 3800))
                .WithFooter(Short($"Mode: {draft.Mode}, Language: {draft.Language}, Villager: {draft.Villager ?? "none"}", 2048));
            var components = new ComponentBuilder()
                .WithButton("Find items", Id("search", draft), ButtonStyle.Primary, disabled: !_config.AllowLookup)
                .WithButton("Paste / edit list", Id("paste", draft))
                .WithButton("Review order", Id("review", draft), ButtonStyle.Success, disabled: draft.Items.Length == 0)
                .WithButton("Options", Id("options", draft))
                .WithButton("Clear list", Id("clear", draft), ButtonStyle.Danger, disabled: draft.Items.Length == 0 && draft.Errors.Length == 0);
            components.WithButton("Presets", Id("presets", draft), row: 1)
                .WithButton("Order again", Id("again", draft), row: 1)
                .WithButton("Upload .nhi file", Id("file"), row: 1)
                .WithButton("My order", Id("my"), row: 1);
            if (draft.Items.Length > 0)
            {
                var remove = new SelectMenuBuilder().WithCustomId(SelectId("remove", draft, page)).WithPlaceholder("Remove an item from your list");
                for (int i = page * 10; i < Math.Min(draft.Items.Length, page * 10 + 10); i++) remove.AddOption(Short($"{i + 1}. {OrderPreparation.Describe(draft.Items[i])}"), i.ToString());
                components.WithSelectMenu(remove, 2);
            }
            if (page > 0) components.WithButton("Previous items", Id("order", draft, page - 1), row: 3);
            if ((page + 1) * 10 < draft.Items.Length) components.WithButton("More items", Id("order", draft, page + 1), row: 3);
            await Reply(embed: embed.Build(), components: components.Build());
        }

        private Task OpenSearch(OrderDraft draft)
        {
            if (!_config.AllowLookup) return Reply("Item search is disabled by the host.");
            var modal = new ModalBuilder().WithTitle("Find items").WithCustomId(Globals.Self.GetInteractionCustomId($"shop-search:{draft.Token}:{draft.Revision}"))
                .AddTextInput("Item name", "query", placeholder: "e.g. lucky cat, nook miles ticket", minLength: 2, maxLength: 100);
            return _context.Interaction.RespondWithModalAsync(modal.Build());
        }

        private Task OpenPaste(OrderDraft draft)
        {
            var modal = new ModalBuilder().WithTitle("Paste or edit your order").WithCustomId(Globals.Self.GetInteractionCustomId($"shop-paste:{draft.Token}:{draft.Revision}"))
                .AddTextInput("Items, or your old order command", "items", TextInputStyle.Paragraph,
                    placeholder: "lucky cat, nook miles ticket OR $order 0083 16A2", maxLength: 4000,
                    value: Short(draft.EditableInput, 4000));
            return _context.Interaction.RespondWithModalAsync(modal.Build());
        }

        private async Task OpenQuick()
        {
            var draft = QuickDraft;
            if (!await OrderInteractionGate.EnterAsync(draft.Gate, true, Defer,
                () => _context.Interaction.RespondAsync("Your quick order is still being submitted. Wait for its result before trying again.", ephemeral: true, allowedMentions: AllowedMentions.None)).ConfigureAwait(false)) return;
            try
            {
                var modal = BuildQuickModal(Globals.Self.GetInteractionCustomId($"shop-quick:{draft.Token}:{draft.Revision}"), draft.Input);
                await _context.Interaction.RespondWithModalAsync(modal).ConfigureAwait(false);
            }
            finally { draft.Gate.Release(); }
        }

        internal static Modal BuildQuickModal(string customId, string input) => new ModalBuilder().WithTitle("Place order (quick)")
            .WithCustomId(customId)
            .AddTextInput("Paste order or ordercat command", "command", TextInputStyle.Paragraph,
                minLength: 1, maxLength: 4000, required: true,
                // Omit the initial value for an empty draft; Discord.Net applies MinLength to supplied values.
                value: input.Length == 0 ? null : Short(input, 4000)).Build();

        public async Task QuickModal(string token, int revision, string input)
        {
            await Defer().ConfigureAwait(false);
            var draft = QuickDraft;
            await draft.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!OrderDraftStore.Quick.Matches(draft, token, revision))
                {
                    if (Globals.Hub.Orders.GetByUserId(User) != null || OrderStatusStore.Shared.IsActive(User))
                        await MyOrder("You already have an accepted order.");
                    else await QuickFailure("That quick-order form is outdated. Open Quick order again to submit a new form.", inputSaved: false);
                    return;
                }
                QuickOrderSubmission.ApplyInput(draft, input, _config);
                OrderDraftStore.Quick.Changed(Guild, User, draft);
                var result = await QuickOrderSubmission.SubmitAsync(draft, _config, _context.User.Username,
                    prepared => QueueHelper.AttemptToQueueRequestDetailedAsync(prepared.RequestedItems,
                        _context.User, _context.Channel, prepared.Villager, prepared.Mode == OrderFillMode.Catalogue,
                        _config.OrderConfig.MaxQueueCount, prepared.Delivery)).ConfigureAwait(false);
                if (!result.Accepted) { await QuickFailure(result.Message); return; }
                try { OrderHistory.Save(Guild, User, draft); }
                catch (Exception ex) { SysBot.Base.LogUtil.LogError($"Could not save accepted quick order history for {User}: {ex.Message}", nameof(OrderExperience)); }
                draft.Items = Array.Empty<Item>(); draft.Input = ""; draft.Errors = Array.Empty<string>(); draft.Villager = null;
                try { OrderDraftStore.Quick.Changed(Guild, User, draft); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { SysBot.Base.LogUtil.LogError($"Could not clear saved quick order for {User}: {ex.Message}", nameof(OrderExperience)); }
                await MyOrder(result.Message + $"\nMode: {draft.Mode}. All stackable items use full stacks.").ConfigureAwait(false);
            }
            finally { draft.Gate.Release(); }
        }

        private Task QuickFailure(string message, bool inputSaved = true) => Reply("No new order was queued.\n\n" + Short(message, 1600)
            + (inputSaved ? "\n\nYour pasted command was saved. Use Retry quick order to edit it or submit it again." : "\n\nUse Retry quick order to open the latest form."),
            components: new ComponentBuilder().WithButton("Retry quick order", Id("quick"), ButtonStyle.Primary)
                .WithButton("Place order (guided)", Id("order"))
                .WithButton("My order", Id("my")).Build());

        private Task OpenOptions(OrderDraft draft)
        {
            var modal = new ModalBuilder().WithTitle("Order options").WithCustomId(Globals.Self.GetInteractionCustomId($"shop-options:{draft.Token}:{draft.Revision}"))
                .AddTextInput("Language code", "language", placeholder: string.Join(", ", OrderPreparation.Languages), maxLength: 10, value: Short(draft.Language, 10))
                .AddTextInput("Villager name or ID (optional)", "villager", required: false, maxLength: OrderPreparation.MaxVillagerLength, value: Short(draft.Villager ?? "", OrderPreparation.MaxVillagerLength));
            return _context.Interaction.RespondWithModalAsync(modal.Build());
        }

        public async Task Modal(string kind, string token, int revision, string text, string? other = null)
        {
            await Defer().ConfigureAwait(false);
            var draft = Draft;
            await draft.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!OrderDraftStore.Shared.Matches(draft, token, revision)) { await ShowDraft(draft, notice: "Your item list changed while this form was open. Open the form again to continue."); return; }
                switch (kind)
                {
                    case "search":
                        if (!_config.AllowLookup) { await Reply("Item search is disabled by the host."); break; }
                        draft.Search = text.Trim(); Changed(draft); await Results(draft, 0); break;
                    case "paste": ApplyInput(draft, text); Changed(draft); await Review(draft); break;
                    case "options":
                        var language = text.Trim().ToLowerInvariant();
                        if (!OrderPreparation.Languages.Contains(language)) { await ShowDraft(draft, notice: "That language is not available. Open Options and choose: " + string.Join(", ", OrderPreparation.Languages)); break; }
                        if (!OrderPreparation.TryValidateVillager(other, out var villager, out var villagerError)) { await ShowDraft(draft, notice: villagerError); break; }
                        if (draft.Errors.Length > 0) ApplyInput(draft, draft.Input, language);
                        draft.Errors = draft.Errors.Where(error => error != OrderPreparation.VillagerLengthError).ToArray();
                        if (draft.Errors.Length == 0) draft.Input = OrderPreparation.FormatInput(draft.Items);
                        draft.Language = language; draft.Villager = villager; Changed(draft); await ShowDraft(draft); break;
                    case "amount":
                        // Old quantity forms remain routable, but cannot override the full-stack policy.
                        if (draft.PendingItem != null)
                        { draft.PendingItem = OrderPreparation.FullStacks(new[] { draft.PendingItem })[0]; Changed(draft); await Pick(draft); }
                        else await Reply("All orders use full stacks. Use Find items to choose an item again.");
                        break;
                    case "quantity":
                        if (LookupOrderSelection.TrySetQuantity(draft, text, out var quantityError))
                        { Changed(draft); await Pick(draft); }
                        else await Pick(draft, quantityError);
                        break;
                }
            }
            finally { draft.Gate.Release(); }
        }

        private async Task Results(OrderDraft draft, int page)
        {
            if (!_config.AllowLookup) { await Reply("Item search is disabled by the host."); return; }
            var matches = ItemSearchService.Search(draft.Search, draft.Language);
            page = Math.Clamp(page, 0, Math.Max(0, (matches.Length - 1) / 10));
            var embed = Embed($"Find items: {Short(draft.Search, 80)}", matches.Length == 0
                ? "No matching items. Try a shorter name (at least two characters)." : $"{matches.Length} matches, page {page + 1}/{(matches.Length + 9) / 10}\nChoose an item below to add it to your order.");
            var components = new ComponentBuilder();
            if (matches.Length > 0)
            {
                var menu = new SelectMenuBuilder().WithCustomId(SelectId("pick", draft, page)).WithPlaceholder("Choose an item");
                foreach (var item in matches.Skip(page * 10).Take(10)) menu.AddOption(Short(item.Text), item.Value.ToString("X4"));
                components.WithSelectMenu(menu, 0);
            }
            components.WithButton("Search again", Id("search", draft), ButtonStyle.Primary, row: 1)
                .WithButton("Your items", Id("order", draft), row: 1);
            if (page > 0) components.WithButton("Previous", Id("results", draft, page - 1), row: 1);
            if ((page + 1) * 10 < matches.Length) components.WithButton("Next", Id("results", draft, page + 1), row: 1);
            await Reply(embed: embed.Build(), components: components.Build());
        }

        private async Task Pick(OrderDraft draft, string? notice = null)
        {
            if (draft.PendingItem == null) { await Results(draft, 0); return; }
            var item = OrderPreparation.FullStacks(new[] { draft.PendingItem })[0];
            var components = new ComponentBuilder();
            var variants = ItemSearchService.BodyVariants(item);
            if (variants.Count > 1)
            {
                var menu = new SelectMenuBuilder().WithCustomId(SelectId("variant", draft)).WithPlaceholder("Choose a variant");
                foreach (var variant in variants) menu.AddOption(Short(variant.Name), variant.Value.ToString(), isDefault: item.BodyType == variant.Value);
                components.WithSelectMenu(menu, 0);
            }
            var fabrics = ItemSearchService.FabricVariants(item);
            if (fabrics.Count > 1)
            {
                var menu = new SelectMenuBuilder().WithCustomId(SelectId("fabric", draft)).WithPlaceholder("Choose a fabric variant");
                foreach (var fabric in fabrics) menu.AddOption(Short(fabric.Name), fabric.Value.ToString(), isDefault: item.PatternChoice == fabric.Value);
                components.WithSelectMenu(menu, 1);
            }
            bool full = draft.Items.Length >= MultiItem.MaxOrder;
            components.WithButton($"Quantity: {draft.PendingQuantity}", Id("quantity", draft), row: 2, disabled: full)
                .WithButton("Add to order", Id("add", draft), ButtonStyle.Success, row: 2, disabled: full)
                .WithButton("Back to results", Id("results", draft), row: 2);
            var description = $"Quantity: **{draft.PendingQuantity}**. Each copy uses one order slot. **{Math.Max(0, MultiItem.MaxOrder - draft.Items.Length)} slots available.**\nChoose a quantity and any variants, then press **Add to order**. All stackable items use full stacks.\nChoosing a quantity selects **Exact** to keep the requested number of items. **Standard** fills 40 slots. Review your order before confirming.";
            if (notice != null) description = notice + "\n\n" + description;
            if (full) description = "Your order already has 40 items. Remove an item from Your items before adding more.\n\n" + description;
            await Reply(embed: Embed(OrderPreparation.Describe(item), description).Build(), components: components.Build());
        }

        private async Task Review(OrderDraft draft, int page = 0)
        {
            if (draft.Errors.Length > 0) { await ShowDraft(draft, notice: "Check the items below before confirming your order."); return; }
            if (!OrderPreparation.TryPrepare(draft.Items, draft.Mode, _config, _context.User.Username, draft.Villager, out var prepared, out var error))
            { await ShowDraft(draft, notice: error); return; }
            draft.Preview = prepared;
            draft.PreviewRevision = draft.Revision;
            var groups = prepared!.VisibleItems.GroupBy(ItemParser.GetItemText).Select(group => $"{group.Count()} {(group.Count() == 1 ? "slot" : "slots")}: {OrderPreparation.Describe(group.First())}").ToArray();
            page = Math.Clamp(page, 0, Math.Max(0, (groups.Length - 1) / 10));
            var behavior = OrderPreparation.FillInstructions(prepared);
            var embed = Embed("Review your order", behavior + "\n\n" + string.Join("\n", groups.Skip(page * 10).Take(10)))
                .WithFooter(Short($"{prepared.VisibleItems.Length} pickup slots, page {page + 1}/{(groups.Length + 9) / 10}, Villager: {draft.Villager ?? "none"}", 2048));
            var mode = new SelectMenuBuilder().WithCustomId(SelectId("mode", draft)).WithPlaceholder("Choose how your order is filled")
                .AddOption("Standard: fill 40 slots and include variants", nameof(OrderFillMode.Standard), isDefault: draft.Mode == OrderFillMode.Standard)
                .AddOption("Exact: keep selected items and variants", nameof(OrderFillMode.Exact), isDefault: draft.Mode == OrderFillMode.Exact)
                .AddOption("Catalogue: collect requested items", nameof(OrderFillMode.Catalogue), isDefault: draft.Mode == OrderFillMode.Catalogue);
            var components = new ComponentBuilder().WithSelectMenu(mode, 0)
                .WithButton("Confirm order", Id("confirm", draft), ButtonStyle.Success, row: 1)
                .WithButton("Keep editing", Id("order", draft), row: 1)
                .WithButton("Test DMs", Id("dm"), row: 1);
            if (page > 0) components.WithButton("Previous", Id("review", draft, page - 1), row: 2);
            if ((page + 1) * 10 < groups.Length) components.WithButton("Next", Id("review", draft, page + 1), row: 2);
            await Reply(embed: embed.Build(), components: components.Build());
        }

        private async Task Confirm(OrderDraft draft)
        {
            if (draft.Preview == null || draft.PreviewRevision != draft.Revision)
            { await Review(draft); return; }
            await Defer().ConfigureAwait(false);
            // Recheck policy at confirmation, but submit the exact array shown in the preview.
            if (!OrderPreparation.TryPrepare(draft.Items, draft.Mode, _config, _context.User.Username, draft.Villager, out _, out var error))
            { await ShowDraft(draft, notice: error); return; }
            var result = await QueueHelper.AttemptToQueueRequestDetailedAsync(draft.Preview.RequestedItems,
                _context.User, _context.Channel, draft.Preview.Villager, draft.Mode == OrderFillMode.Catalogue,
                _config.OrderConfig.MaxQueueCount, draft.Preview.Delivery).ConfigureAwait(false);
            if (!result.Accepted) { await ShowDraft(draft, notice: result.Message + "\nYour item list was saved. Review your order to try again."); return; }
            try { OrderHistory.Save(Guild, User, draft); }
            catch (Exception ex) { SysBot.Base.LogUtil.LogError($"Could not save accepted order history for {User}: {ex.Message}", nameof(OrderExperience)); }
            draft.Items = Array.Empty<Item>(); draft.Input = ""; draft.Errors = Array.Empty<string>(); draft.Villager = null;
            try { Changed(draft); }
            catch (IOException ex) { SysBot.Base.LogUtil.LogError($"Could not clear saved draft for {User}: {ex.Message}", nameof(OrderExperience)); }
            await MyOrder(result.Message).ConfigureAwait(false);
        }

        private async Task ShowPresets(OrderDraft draft, int page)
        {
            var files = Presets();
            page = Math.Clamp(page, 0, Math.Max(0, (files.Length - 1) / 10));
            var components = new ComponentBuilder();
            if (files.Length > 0)
            {
                var menu = new SelectMenuBuilder().WithCustomId(SelectId("preset", draft, page)).WithPlaceholder("Replace your item list with a preset");
                for (int i = page * 10; i < Math.Min(files.Length, page * 10 + 10); i++) menu.AddOption(Short(Path.GetFileNameWithoutExtension(files[i])), OrderPresetSelection.Id(files[i]));
                components.WithSelectMenu(menu, 0);
            }
            components.WithButton("Your items", Id("order", draft), row: 1);
            if (page > 0) components.WithButton("Previous", Id("presets", draft, page - 1), row: 1);
            if ((page + 1) * 10 < files.Length) components.WithButton("Next", Id("presets", draft, page + 1), row: 1);
            await Reply(embed: Embed("Host presets", files.Length == 0 ? "The host has not added any presets yet." : "Choose a preset to replace your item list. You can review and edit it before confirming.").Build(), components: components.Build());
        }

        private async Task LoadLast(OrderDraft draft)
        {
            if (!OrderHistory.TryLoad(Guild, User, out var saved, out var legacyItems, out var legacyText, out var mode))
            { await ShowDraft(draft, notice: "No previous order was found. Create one first."); return; }
            if (saved != null)
            {
                draft.Items = Item.GetArray(saved.Items); draft.Input = OrderPreparation.FormatInput(draft.Items); draft.Errors = Array.Empty<string>();
                draft.Language = saved.Language; draft.Villager = saved.Villager; draft.Mode = saved.Mode;
            }
            else if (legacyItems != null)
            { draft.Items = legacyItems; draft.Input = OrderPreparation.FormatInput(legacyItems); draft.Errors = Array.Empty<string>(); draft.Mode = OrderFillMode.Exact; draft.Villager = null; }
            else { ApplyInput(draft, legacyText!); draft.Mode = mode; }
            draft.Items = OrderPreparation.FullStacks(draft.Items);
            if (draft.Errors.Length == 0) draft.Input = OrderPreparation.FormatInput(draft.Items);
            Changed(draft);
            await Review(draft);
        }

        public async Task MyOrder(string? notice = null)
        {
            var position = Globals.Hub.Orders.GetPosition(User);
            var status = OrderStatusStore.Shared.Get(User);
            string description;
            if (OrderStatusStore.Shared.IsActive(User) && status != null)
            {
                description = status.Stage switch
                {
                    OrderStage.Ready => $"**Ready to visit.** Talk to Orville and use Dodo code **{status.Dodo}**.\n{status.Message}",
                    OrderStage.Visiting => "**You are visiting the island.** Collect your items, then leave through the airport.",
                    OrderStage.Completed => "**Order completed.** The island is finishing cleanup.",
                    OrderStage.Cancelled or OrderStage.Failed => $"**Order {status.Stage.ToString().ToLowerInvariant()}.** {status.Message}",
                    _ => "**Preparing your order.** Empty your inventory and wait at Orville's Dodo code entry screen."
                };
                if (status.Deadline.HasValue && status.Stage is OrderStage.Ready or OrderStage.Visiting)
                    description += $"\nAbout {Math.Max(0, (int)(status.Deadline.Value - DateTimeOffset.UtcNow).TotalSeconds)} seconds remaining. Press Refresh to check again.";
            }
            else if (position > 0)
                description = $"**Waiting: position {position}.**\n" + (position == 1 ? "You are next. Your order starts when the island is ready." : $"Approximate wait: {QueueExtensions.GetETA(position)}. Game restarts and arrival times can change this estimate.")
                    + (_config.AcceptingCommands ? "" : "\nThe host has paused processing. Your place is held.");
            else if (status != null && status.Stage is OrderStage.Completed or OrderStage.Cancelled or OrderStage.Failed)
                description = $"**Order {status.Stage.ToString().ToLowerInvariant()}.** {status.Message}";
            else description = "You have no active order. Choose Guided or Quick order to get started.";
            if (notice != null) description = Short(notice, 1000) + "\n\n" + description;
            var components = new ComponentBuilder().WithButton("Refresh", Id("my"), ButtonStyle.Primary)
                .WithButton("Place order (guided)", Id("order"))
                .WithButton("Place order (quick)", Id("quick"))
                .WithButton("Order again", Id("again"));
            var queued = Globals.Hub.Orders.GetByUserId(User);
            if (queued != null && status != null && status.OrderId == queued.OrderID)
                components.WithButton("Cancel order", StatusId("cancel", status), ButtonStyle.Danger);
            await Reply(embed: Embed($"My order: {Globals.Bot.TownName}", Short(description, 3800)).Build(), components: components.Build());
        }

        public Task RequestCancel()
        {
            var order = Globals.Hub.Orders.GetByUserId(User);
            var status = OrderStatusStore.Shared.Get(User);
            return order == null || status == null ? MyOrder("You have no waiting order to cancel.") : Cancel(false, status.ControlToken, order.OrderID.ToString());
        }

        private string StatusId(string action, OrderStatus status) => Globals.Self.GetInteractionCustomId($"shop:{action}:{status.ControlToken}:{status.OrderId}");

        private async Task Cancel(bool confirmed, string token, string value)
        {
            var order = Globals.Hub.Orders.GetByUserId(User);
            var status = OrderStatusStore.Shared.Get(User);
            if (order == null || status == null || status.ControlToken != token || status.OrderId != order.OrderID || order.OrderID.ToString() != value)
            { await MyOrder("That queued order is no longer available to cancel."); return; }
            if (!confirmed)
            {
                await Reply("Cancel your waiting order? You will lose this queue position.", components: new ComponentBuilder()
                    .WithButton("Yes, cancel order", StatusId("cancel-confirm", status), ButtonStyle.Danger)
                    .WithButton("Keep my place", Id("my"), ButtonStyle.Primary).Build());
                return;
            }
            if (Globals.Hub.Orders.RemoveByUserId(User, order.OrderID))
            {
                OrderStatusStore.Shared.Set(User, order.OrderID, OrderStage.Cancelled, "You cancelled your waiting order.");
                await MyOrder("Your waiting order was cancelled. You can order again whenever you are ready.");
            }
            else await MyOrder("Your order has started. It can no longer be cancelled from the waiting queue.");
        }

        public Task Help() => Reply(embed: Embed("Ordering guide", "**Place order (guided):** find items or paste a list, review what you will receive, then press Confirm order to join the queue. Choose Standard, Exact, or Catalogue on the review screen.\n\n**Place order (quick):** paste an order or ordercat command and press Submit to join the queue directly. order selects Standard, ordercat selects Catalogue. A plain item list uses Standard. Invalid lists are saved for you to edit and retry.\n\nAll stackable items use full stacks. Enable DMs from this server before submitting. **My order** shows your position and progress. Check DMs when your order starts.\n\nEmpty your inventory, arrive promptly using the private Dodo code, collect your items, and leave through the airport.\n\nYour lists are saved if the bot restarts. Waiting orders are cleared, so you may need to join the queue again.").Build(),
            components: new ComponentBuilder().WithButton("Place order (guided)", Id("order"), ButtonStyle.Primary)
                .WithButton("Place order (quick)", Id("quick"), ButtonStyle.Primary).WithButton("My order", Id("my")).Build());
    }
}

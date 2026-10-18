using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    internal sealed record NhiImportResult(Item[]? Items, string Error);

    internal static class NhiOrderImport
    {
        private static readonly HttpClient Client = new();
        private const int MaxBytes = MultiItem.MaxOrder * Item.SIZE;
        private const string InvalidFile = "Choose one .nhi inventory file containing 1 to 40 item slots.";

        internal static Modal BuildModal(string customId) => new ModalBuilder().WithTitle("Upload .nhi order")
            .WithCustomId(customId).AddFileUpload("NHI inventory file", "file", minValues: 1, maxValues: 1,
                description: "Choose one .nhi file. This replaces your item list. Review and confirm before joining the queue.").Build();

        internal static bool Matches(OrderDraft draft, string token, int revision) => OrderDraftStore.Shared.Matches(draft, token, revision);

        internal static async Task<NhiImportResult> ReadAsync(IAttachment? file, Func<string, Task<Stream>>? openStream = null)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.Filename) || !file.Filename.EndsWith(".nhi", StringComparison.OrdinalIgnoreCase)
                || file.Size < Item.SIZE || file.Size > MaxBytes || file.Size % Item.SIZE != 0
                || !Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return new(null, InvalidFile);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var stream = await (openStream ?? (url => Client.GetStreamAsync(url, timeout.Token)))(file.Url).ConfigureAwait(false);
                // Read one extra byte to detect oversized content without trusting attachment metadata.
                var bytes = new byte[MaxBytes + 1];
                var length = 0;
                while (length < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(length), timeout.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    length += read;
                }
                if (length != file.Size || length < Item.SIZE || length > MaxBytes || length % Item.SIZE != 0)
                    return new(null, InvalidFile);
                return new(Item.GetArray(bytes[..length]), string.Empty);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or ArgumentException)
            { return new(null, "That file could not be read. Press Upload .nhi file and choose the file again."); }
        }

        internal static bool TryApply(OrderDraft draft, string token, int revision, Item[] items, CrossBotConfig config, out string error)
        {
            error = "Your item list changed. Open the upload form again to replace the current list.";
            if (!Matches(draft, token, revision)) return false;
            var candidates = OrderPreparation.FullStacks(items.Where(item => !item.IsNone));
            if (candidates.Length == 0)
            { error = "That inventory has no items. Choose an .nhi file containing at least one item."; return false; }
            // Validate only the uploaded items here. The selected villager is checked in the order review.
            if (!OrderPreparation.TryPrepare(candidates, OrderFillMode.Exact, config, "", null, out var prepared, out error, draft.Language)) return false;
            draft.Items = prepared!.RequestedItems;
            draft.Input = OrderPreparation.FormatInput(draft.Items);
            draft.Errors = Array.Empty<string>();
            draft.Mode = OrderFillMode.Exact;
            draft.PendingItem = null;
            draft.PendingQuantity = 1;
            return true;
        }
    }
}

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    internal static class OrderHistory
    {
        public static void Save(ulong guild, ulong user, OrderDraft draft, string directory = "OrderData")
        {
            Directory.CreateDirectory(directory);
            var saved = new SavedOrderDraft(draft.Token, draft.Revision, OrderDraftStore.SerializeItems(draft.Items), draft.Input,
                Array.Empty<string>(), draft.Language, draft.Villager, draft.Mode);
            var path = Path.Combine(directory, $"last-{guild}-{user}.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(saved));
            File.Move(path + ".tmp", path, true);
        }

        public static bool TryLoad(ulong guild, ulong user, out SavedOrderDraft? saved, out Item[]? items, out string? text, out OrderFillMode mode,
            string directory = "OrderData", string? legacyDirectory = null)
        {
            saved = null; items = null; text = null; mode = OrderFillMode.Standard;
            var path = Path.Combine(directory, $"last-{guild}-{user}.json");
            if (File.Exists(path))
                return OrderDraftStore.TryReadSaved(path, out saved);
            path = Path.Combine(legacyDirectory ?? OrderInteractionModule.LastOrderDirectory, user.ToString());
            try
            {
                if (!File.Exists(path)) return false;
                var bytes = File.ReadAllBytes(path);
                var legacyText = Encoding.UTF8.GetString(bytes);
                // ORDERCAT must be tested before its ORDER prefix.
                if (legacyText.StartsWith(OrderInteractionModule.OrderCatMarker))
                { text = legacyText[OrderInteractionModule.OrderCatMarker.Length..]; mode = OrderFillMode.Catalogue; }
                else if (legacyText.StartsWith(OrderInteractionModule.OrderMarker)) text = legacyText[OrderInteractionModule.OrderMarker.Length..];
                else if (bytes.Length > 0 && bytes.Length <= Item.SIZE * MultiItem.MaxOrder && bytes.Length % Item.SIZE == 0)
                { items = Item.GetArray(bytes); mode = OrderFillMode.Exact; }
                else return false;
                return true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                SysBot.Base.LogUtil.LogError($"Could not read order history {path}: {ex.Message}", nameof(OrderHistory));
                return false;
            }
        }
    }
}

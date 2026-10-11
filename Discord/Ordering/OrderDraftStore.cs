using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public sealed class OrderDraft
    {
        public string Token { get; set; } = Guid.NewGuid().ToString("N");
        public int Revision { get; set; }
        public Item[] Items { get; set; } = Array.Empty<Item>();
        public string Input { get; set; } = string.Empty;
        public string[] Errors { get; set; } = Array.Empty<string>();
        public string Language { get; set; } = "en";
        public string? Villager { get; set; }
        public OrderFillMode Mode { get; set; } = OrderFillMode.Standard;
        public string Search { get; set; } = string.Empty;
        public string VillagerSearch { get; set; } = string.Empty;
        public string? PendingVillager { get; set; }
        public Item? PendingItem { get; set; }
        public int PendingQuantity { get; set; } = 1;
        public PreparedOrder? Preview { get; set; }
        public int PreviewRevision { get; set; } = -1;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        internal string EditableInput => Errors.Length > 0 ? Input : OrderPreparation.FormatInput(Items);
    }

    public sealed record SavedOrderDraft(string Token, int Revision, byte[] Items, string Input, string[] Errors,
        string Language, string? Villager, OrderFillMode Mode);

    /// <summary>One private draft per guild/user in this bot process; revisions reject old controls.</summary>
    public sealed class OrderDraftStore
    {
        public static OrderDraftStore Shared { get; } = new("OrderData");
        public static OrderDraftStore Quick { get; } = new(Path.Combine("OrderData", "Quick"));
        private readonly ConcurrentDictionary<(ulong Guild, ulong User), OrderDraft> _drafts = new();
        private readonly string? _directory;
        public OrderDraftStore(string? directory = null) => _directory = directory;

        public OrderDraft Get(ulong guild, ulong user) => _drafts.GetOrAdd((guild, user), key => Load(key.Guild, key.User));

        public bool Matches(OrderDraft draft, string token, int revision) => draft.Token == token && draft.Revision == revision;

        public void Changed(ulong guild, ulong user, OrderDraft draft)
        {
            draft.Revision++;
            draft.Preview = null;
            draft.PreviewRevision = -1;
            Save(guild, user, draft);
        }

        public void Save(ulong guild, ulong user, OrderDraft draft)
        {
            if (_directory == null) return;
            Directory.CreateDirectory(_directory);
            var saved = new SavedOrderDraft(draft.Token, draft.Revision, SerializeItems(draft.Items),
                draft.Input, draft.Errors, draft.Language, draft.Villager, draft.Mode);
            var path = Path.Combine(_directory, $"draft-{guild}-{user}.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(saved));
            File.Move(path + ".tmp", path, true);
        }

        private OrderDraft Load(ulong guild, ulong user)
        {
            if (_directory == null) return new();
            var path = Path.Combine(_directory, $"draft-{guild}-{user}.json");
            if (!TryReadSaved(path, out var saved)) return new();
            return new() { Token = saved!.Token, Revision = saved.Revision, Items = OrderPreparation.FullStacks(Item.GetArray(saved.Items)),
                Input = saved.Input, Errors = saved.Errors, Language = saved.Language, Villager = saved.Villager, Mode = saved.Mode };
        }

        internal static byte[] SerializeItems(Item[] items) => items.Length == 0
            ? Array.Empty<byte>() // NHSE's editor determines item size from the first element.
            : new ItemArrayEditor<Item>(items).Write();

        internal static bool IsValid(SavedOrderDraft? saved) => saved != null
            && Guid.TryParseExact(saved.Token, "N", out _) && saved.Revision >= 0
            && saved.Items != null && saved.Items.Length % Item.SIZE == 0
            && saved.Input != null && saved.Errors != null && saved.Errors.All(error => error != null)
            && OrderPreparation.Languages.Contains(saved.Language) && Enum.IsDefined(saved.Mode)
            && OrderPreparation.TryValidateVillager(saved.Villager, out _, out _);

        internal static bool TryReadSaved(string path, out SavedOrderDraft? saved)
        {
            saved = null;
            try
            {
                if (!File.Exists(path)) return false;
                var candidate = JsonSerializer.Deserialize<SavedOrderDraft>(File.ReadAllText(path));
                if (!IsValid(candidate))
                {
                    SysBot.Base.LogUtil.LogError($"Saved order data is incomplete or invalid: {path}", nameof(OrderDraftStore));
                    return false;
                }
                saved = candidate;
                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
            {
                SysBot.Base.LogUtil.LogError($"Could not read saved order data {path}: {ex.Message}", nameof(OrderDraftStore));
                return false;
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using NHSE.Core;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public sealed class NhiBindingCapture { public IAttachment File { get; set; } }
    public class NhiBindingProbeModule : InteractionModuleBase<IInteractionContext>
    {
        public NhiBindingCapture Capture { get; set; }
        [ModalInteraction("shop-nhi:*:*")]
        public Task Upload(string token, int revision, OrderNhiModal modal)
        { Capture.File = modal.File; return Task.CompletedTask; }
        [ModalInteraction("shop-nhi:*:*:*")]
        public Task SuffixedUpload(string token, int revision, string suffix, OrderNhiModal modal) => Upload(token, revision, modal);
    }

    public sealed class NhiUploadTests
    {
        private static IAttachment File(string name, int size) => InterfaceStub.Create<IAttachment>(
            ("Id", 123UL), ("Filename", name), ("Size", size), ("Url", "https://cdn.discordapp.com/attachments/test/order.nhi"));

        [Fact]
        public void UploadModalContainsOneRequiredFilePickerAndNoCommandOrUrlField()
        {
            var modal = NhiOrderImport.BuildModal("shop-nhi:token:3:island");
            Assert.Equal("Upload .nhi order", modal.Title);
            var label = Assert.IsType<LabelComponent>(Assert.Single(modal.Component.Components));
            var picker = Assert.IsType<FileUploadComponent>(label.Component);
            Assert.Equal("file", picker.CustomId);
            Assert.Equal(1, picker.MinValues);
            Assert.Equal(1, picker.MaxValues);
            Assert.True(picker.IsRequired);
            Assert.Contains("replaces", label.Description);
        }

        [Theory]
        [InlineData("order.nhi", 1)]
        [InlineData("ORDER.NHI", 40)]
        public async Task ValidUploadsUseTheExistingNhiEncodingAndKeepRecipeIds(string name, int slots)
        {
            var items = Enumerable.Range(0, slots).Select(_ => RecipeSearchService.Create(15)).ToArray();
            var bytes = new ItemArrayEditor<Item>(items).Write();
            var result = await NhiOrderImport.ReadAsync(File(name, bytes.Length), _ => Task.FromResult<Stream>(new MemoryStream(bytes)));
            Assert.Empty(result.Error);
            Assert.Equal(slots, result.Items.Length);
            Assert.All(result.Items, item => Assert.Equal(items[0].RawValue, item.RawValue));
        }

        [Theory]
        [InlineData("order.txt", 8)]
        [InlineData("order.nhi.exe", 8)]
        [InlineData("order.nhi", 0)]
        [InlineData("order.nhi", 1)]
        [InlineData("order.nhi", 7)]
        [InlineData("order.nhi", 321)]
        [InlineData("order.nhi", 328)]
        public async Task InvalidMetadataIsRejectedBeforeDownloading(string name, int size)
        {
            var result = await NhiOrderImport.ReadAsync(File(name, size), _ => throw new Exception("Invalid attachment must not be downloaded"));
            Assert.Null(result.Items);
            Assert.Contains("1 to 40", result.Error);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(9)]
        [InlineData(16)]
        [InlineData(321)]
        public async Task TruncatedMismatchedAndOversizedContentsCannotBeImported(int actualSize)
        {
            var result = await NhiOrderImport.ReadAsync(File("order.nhi", Item.SIZE), _ => Task.FromResult<Stream>(new MemoryStream(new byte[actualSize])));
            Assert.Null(result.Items);
            Assert.NotEmpty(result.Error);
        }

        private sealed class ChunkedStream : MemoryStream
        {
            public int TotalRead { get; private set; }
            public bool WasDisposed { get; private set; }
            public ChunkedStream(byte[] buffer) : base(buffer) { }
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var count = await base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
                TotalRead += count;
                return count;
            }
            protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
        }

        [Fact]
        public async Task DownloadReadsPartialChunksAndStopsAtTheInventoryLimit()
        {
            var bytes = new ItemArrayEditor<Item>(new[] { RecipeSearchService.Create(15) }).Write();
            var valid = new ChunkedStream(bytes);
            var result = await NhiOrderImport.ReadAsync(File("order.nhi", bytes.Length), _ => Task.FromResult<Stream>(valid));
            Assert.NotNull(result.Items);
            Assert.True(valid.WasDisposed);
            var large = new ChunkedStream(new byte[8192]);
            result = await NhiOrderImport.ReadAsync(File("order.nhi", Item.SIZE * MultiItem.MaxOrder), _ => Task.FromResult<Stream>(large));
            Assert.Null(result.Items);
            Assert.Equal(Item.SIZE * MultiItem.MaxOrder + 1, large.TotalRead);
            Assert.True(large.WasDisposed);
        }

        [Fact]
        public async Task DownloadFailuresAndMissingAttachmentsReturnAnEditableError()
        {
            var missing = await NhiOrderImport.ReadAsync(null, _ => throw new Exception("No attachment"));
            Assert.Null(missing.Items);
            var failed = await NhiOrderImport.ReadAsync(File("order.nhi", Item.SIZE), _ => throw new HttpRequestException("Expired attachment"));
            Assert.Null(failed.Items);
            Assert.Contains("again", failed.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a URL")]
        [InlineData("/relative/order.nhi")]
        [InlineData("file:///C:/order.nhi")]
        [InlineData("http://cdn.discordapp.com/order.nhi")]
        public async Task InvalidDownloadSourcesFailWithoutStartingARequest(string url)
        {
            var attachment = InterfaceStub.Create<IAttachment>(("Filename", "order.nhi"), ("Size", Item.SIZE), ("Url", url));
            var result = await NhiOrderImport.ReadAsync(attachment, _ => throw new Exception("Invalid source must not be downloaded"));
            Assert.Null(result.Items);
            Assert.NotEmpty(result.Error);
        }

        [Fact]
        public void ImportsReplaceItemsUseExactAndKeepLanguageAndVillagerWithoutQueuing()
        {
            var recipe = RecipeSearchService.Create(15);
            var draft = new OrderDraft { Items = new[] { new Item(131) }, Villager = "ost10", Language = "jp", PendingItem = new Item(131),
                PendingQuantity = 3, Errors = new[] { "Previous input error" }, Mode = OrderFillMode.Catalogue };
            Assert.True(NhiOrderImport.TryApply(draft, draft.Token, draft.Revision, new[] { recipe, new Item(Item.NONE) }, new CrossBotConfig(), out _));
            Assert.Equal(recipe.RawValue, Assert.Single(draft.Items).RawValue);
            Assert.Equal(OrderFillMode.Exact, draft.Mode);
            Assert.Equal("ost10", draft.Villager);
            Assert.Equal("jp", draft.Language);
            Assert.Empty(draft.Errors);
            Assert.Null(draft.PendingItem);
            Assert.Equal(1, draft.PendingQuantity);
            Assert.Equal(OrderPreparation.FormatInput(draft.Items), draft.Input);
            Assert.Null(draft.Preview);
            Assert.True(OrderPreparation.TryPrepare(draft.Items, draft.Mode, new CrossBotConfig { AllowVillagerInjection = true }, "tester", draft.Villager, out var prepared, out var error, draft.Language), error);
            Assert.Equal(recipe.RawValue, Assert.Single(prepared.VisibleItems).RawValue);
        }

        [Fact]
        public async Task AFormFromAnotherUserOrEditsDuringTheDownloadCannotReplaceTheDraft()
        {
            var draft = new OrderDraft { Items = new[] { new Item(131) } };
            var original = draft.Items;
            var revision = draft.Revision;
            Assert.False(NhiOrderImport.TryApply(draft, Guid.NewGuid().ToString("N"), revision, new[] { RecipeSearchService.Create(15) }, new CrossBotConfig(), out _));
            Assert.Same(original, draft.Items);
            var bytes = new ItemArrayEditor<Item>(new[] { RecipeSearchService.Create(15) }).Write();
            var result = await NhiOrderImport.ReadAsync(File("order.nhi", bytes.Length), _ =>
            {
                new OrderDraftStore().Changed(1, 2, draft);
                return Task.FromResult<Stream>(new MemoryStream(bytes));
            });
            Assert.False(NhiOrderImport.TryApply(draft, draft.Token, revision, result.Items, new CrossBotConfig(), out _));
            Assert.Same(original, draft.Items);
        }

        [Theory]
        [InlineData(0xFFFE, 0)]
        [InlineData(0x16A2, 0)]
        [InlineData(0x16A2, 65535)]
        [InlineData(0xFFFD, 0)]
        public void EmptyInventoriesUnknownRecipesAndUnknownItemsCannotReplaceTheDraft(int itemId, int count)
        {
            var draft = new OrderDraft { Items = new[] { new Item(131) }, Mode = OrderFillMode.Standard };
            var original = draft.Items;
            var upload = new[] { new Item((ushort)itemId) { Count = (ushort)count } };
            Assert.False(NhiOrderImport.TryApply(draft, draft.Token, draft.Revision, upload, new CrossBotConfig(), out var error));
            Assert.NotEmpty(error);
            Assert.Same(original, draft.Items);
            Assert.Equal(OrderFillMode.Standard, draft.Mode);
        }

        [Theory]
        [InlineData(0x02F6, 0x0A40)]
        [InlineData(0x02F7, 0x3106)]
        [InlineData(0x02F8, 0x3107)]
        public void CorrectedInternalItemsAreSavedAsFullStacksMatchingTheReview(int inputId, int expectedId)
        {
            var draft = new OrderDraft();
            var config = new CrossBotConfig();
            Assert.True(NhiOrderImport.TryApply(draft, draft.Token, draft.Revision, new[] { new Item((ushort)inputId) }, config, out var error), error);
            var saved = Assert.Single(draft.Items);
            Assert.Equal((ushort)expectedId, saved.ItemId);
            Assert.True(OrderPreparation.TryPrepare(draft.Items, draft.Mode, config, "tester", null, out var prepared, out error), error);
            Assert.Equal(saved.RawValue, Assert.Single(prepared.VisibleItems).RawValue);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("island")]
        public async Task DiscordModalBindingResolvesTheUploadedAttachmentWithIslandRouting(string suffix)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new NhiBindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<NhiBindingProbeModule>(services);
            var id = "shop-nhi:private-token:12" + (suffix == null ? "" : ":" + suffix);
            var attachment = File("order.nhi", Item.SIZE);
            var field = InterfaceStub.Create<IComponentInteractionData>(("CustomId", "file"), ("Type", ComponentType.FileUpload), ("Values", new[] { "123" }));
            // SocketModalData flattens label components before exposing IModalInteractionData.Components.
            var data = InterfaceStub.Create<IModalInteractionData>(("CustomId", id), ("Components", new[] { field }), ("Attachments", new[] { attachment }));
            var user = InterfaceStub.Create<IUser>(("Id", 1UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", user));
            var context = new InteractionContext(client, interaction);
            var pattern = InteractionRouting.FindPattern(id, suffix, service.ModalCommands.Select(command => command.Name));
            Assert.NotNull(pattern);
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Same(attachment, capture.File);
        }
    }
}

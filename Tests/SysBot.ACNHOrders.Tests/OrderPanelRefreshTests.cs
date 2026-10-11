using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public class PanelApiStub : DispatchProxy
    {
        public Func<MethodInfo, object[], object> Call { get; set; }
        protected override object Invoke(MethodInfo method, object[] args) => Call(method, args);
        internal static T Create<T>(Func<MethodInfo, object[], object> call) where T : class
        {
            var stub = Create<T, PanelApiStub>();
            ((PanelApiStub)(object)stub).Call = call;
            return stub;
        }
    }

    public sealed class OrderPanelRefreshTests
    {
        private static readonly OrderPanelState State = new("Lovely", true, 3, 50, true, OrderStage.Preparing);
        private static readonly OrderPanelLocation Panel = new(10, 20, 30);

        [Fact]
        public async Task RefreshTargetsTheSavedMessageWhenThereAreNewerMessagesBelowIt()
        {
            var logs = new List<string>();
            var embed = OrderPanelService.BuildEmbed(State);
            var components = OrderPanelService.BuildComponents(true, id => id);
            int edits = 0;
            var message = PanelApiStub.Create<IUserMessage>((method, args) =>
            {
                Assert.Equal("ModifyAsync", method.Name);
                var properties = new MessageProperties();
                ((Action<MessageProperties>)args[0])(properties);
                Assert.Equal(embed, properties.Embed.Value);
                Assert.Equal(components, properties.Components.Value);
                Assert.Equal(AllowedMentions.None, properties.AllowedMentions.Value);
                edits++;
                return Task.CompletedTask;
            });
            var channel = PanelApiStub.Create<ITextChannel>((method, args) =>
            {
                // Fetch the saved ID directly. Do not scan recent messages or use the last post.
                Assert.Equal("GetMessageAsync", method.Name);
                Assert.Equal(30UL, args[0]);
                Assert.Equal(CacheMode.AllowDownload, args[1]);
                Assert.True(((RequestOptions)args[2]).CancelToken.CanBeCanceled);
                return Task.FromResult<IMessage>(message);
            });
            Assert.True(await OrderPanelService.RefreshPanelAsync(Panel, (id, options) =>
            {
                Assert.Equal(20UL, id);
                return Task.FromResult(channel);
            }, embed, components, logs.Add, default));
            Assert.Equal(1, edits);
            Assert.Empty(logs);
        }

        [Theory]
        [InlineData("channel")]
        [InlineData("message")]
        [InlineData("permission")]
        public async Task MissingPanelsAndApiFailuresAreLoggedAndReportedAsFailedRefreshes(string failure)
        {
            var logs = new List<string>();
            var channel = PanelApiStub.Create<ITextChannel>((method, args) => failure == "permission"
                ? throw new InvalidOperationException("Missing permissions") : Task.FromResult<IMessage>(null));
            Assert.False(await OrderPanelService.RefreshPanelAsync(Panel,
                (_, _) => Task.FromResult(failure == "channel" ? null : channel),
                OrderPanelService.BuildEmbed(State), OrderPanelService.BuildComponents(true, id => id), logs.Add, default));
            Assert.Single(logs);
            Assert.Contains("20", logs[0]);
            if (failure != "channel") Assert.Contains("30", logs[0]);
        }

        [Fact]
        public async Task FailedRefreshCanRecoverWithoutAChangeToTheQueueCount()
        {
            var logs = new List<string>();
            var embed = OrderPanelService.BuildEmbed(State);
            var components = OrderPanelService.BuildComponents(true, id => id);
            var message = PanelApiStub.Create<IUserMessage>((_, _) => Task.CompletedTask);
            var channel = PanelApiStub.Create<ITextChannel>((_, _) => Task.FromResult<IMessage>(message));
            Assert.False(await OrderPanelService.RefreshPanelAsync(Panel, (_, _) => Task.FromResult<ITextChannel>(null), embed, components, logs.Add, default));
            Assert.True(await OrderPanelService.RefreshPanelAsync(Panel, (_, _) => Task.FromResult(channel), embed, components, logs.Add, default));
            Assert.Single(logs);
        }

        [Fact]
        public async Task ApplicationShutdownCancelsRefreshInsteadOfReportingAnApiError()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var logs = new List<string>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OrderPanelService.RefreshPanelAsync(Panel,
                (_, options) => Task.FromCanceled<ITextChannel>(options.CancelToken),
                OrderPanelService.BuildEmbed(State), OrderPanelService.BuildComponents(true, id => id), logs.Add, cancellation.Token));
            Assert.Empty(logs);
        }

        [Fact]
        public void RestartReadsTheSameChannelAndMessageIdsFromTheSavedFile()
        {
            var directory = Path.Combine(Path.GetTempPath(), "acnh-panel-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "panels.json");
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(new[] { Panel }));
                var logs = new List<string>();
                Assert.Equal(Panel, Assert.Single(OrderPanelService.ReadLocations(path, logs.Add)));
                Assert.Equal(Panel, Assert.Single(OrderPanelService.ReadLocations(path, logs.Add)));
                Assert.Empty(logs);
                File.WriteAllText(path, "[null]");
                Assert.Empty(OrderPanelService.ReadLocations(path, logs.Add));
                Assert.Single(logs);
                Assert.Contains(Path.GetFullPath(path), logs[0]);
            }
            finally { File.Delete(path); Directory.Delete(directory); }
        }

        [Fact]
        public void MissingRegistryReportsItsExactPathAndSetupInstructions()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "panels.json");
            var logs = new List<string>();
            Assert.Empty(OrderPanelService.ReadLocations(path, logs.Add));
            Assert.Contains(Path.GetFullPath(path), Assert.Single(logs));
            Assert.Contains("/setup-embed", logs[0]);
        }

        [Theory]
        [InlineData(OrderStage.Preparing, "Preparing island")]
        [InlineData(OrderStage.Ready, "Waiting for arrival")]
        [InlineData(OrderStage.Visiting, "Visitor on island")]
        public void PublicPanelShowsWaitingAndActiveOrderSeparatelyWithoutPrivateDetails(OrderStage stage, string description)
        {
            var store = new OrderStatusStore();
            Assert.Null(store.ActiveStage);
            store.Set(123, 456, stage, "Private instructions", "AB123");
            var status = State with { Waiting = 0, ActiveStage = store.ActiveStage };
            var embed = OrderPanelService.BuildEmbed(status);
            Assert.Contains("0 people waiting", embed.Description);
            Assert.Contains(description, embed.Description);
            Assert.DoesNotContain("AB123", embed.Description);
            Assert.DoesNotContain("Private instructions", embed.Description);
            Assert.NotEqual(status, status with { ActiveStage = null });
            store.Release(123);
            Assert.Null(store.ActiveStage);
            Assert.Contains("Current order: **None**", OrderPanelService.BuildEmbed(status with { ActiveStage = null }).Description);
        }
    }
}

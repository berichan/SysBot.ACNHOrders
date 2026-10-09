using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using SysBot.Base;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public sealed class ControlPanelTests
    {
        [Fact]
        public void OnlySudoAndOwnerCanBypassPanelRestrictions()
        {
            var config = new CrossBotConfig { Channels = { 10 }, Users = { 20 }, Sudo = { 30 }, IgnoreAllPermissions = true };
            Assert.True(ControlPanelAccess.CanBypassRestrictions(config, 30, 40, true));
            Assert.True(ControlPanelAccess.CanBypassRestrictions(config, 40, 40, true));
            Assert.False(ControlPanelAccess.CanBypassRestrictions(config, 20, 40, true));
            Assert.False(ControlPanelAccess.CanBypassRestrictions(config, 30, 40, false));
            Assert.False(ControlPanelAccess.CanUse(config, 50, 40));
        }

        [Fact]
        public void ConfirmationIsBoundToUserActionAndExpiryAndCannotBeReplayed()
        {
            var store = new ControlConfirmationStore();
            var now = DateTimeOffset.UtcNow;
            var token = store.Create(1, "restart", now);
            Assert.False(store.Consume(2, "restart", token, now));
            Assert.False(store.Consume(1, "stop", token, now));
            Assert.True(store.Consume(1, "restart", token, now));
            Assert.False(store.Consume(1, "restart", token, now));
            var expired = store.Create(1, "stop", now);
            Assert.False(store.Consume(1, "stop", expired, now.AddMinutes(6)));
            var replaced = store.Create(1, "stop", now);
            store.Create(1, "detach", now);
            Assert.False(store.Consume(1, "stop", replaced, now));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("island")]
        public async Task AllPanelHandlersRequireSudoAndRouteToOnlyTheirOwnSuffix(string suffix)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client);
            await service.AddModuleAsync<ControlPanelInteractionModule>(null);
            var module = service.Modules.Single();
            Assert.Contains(module.Preconditions, p => p is RequireControlPanelSudoAttribute);
            Assert.Equal(2, service.SlashCommands.Count);
            foreach (var baseId in new[] { "control:status", "control:start", "control-confirm:restart:token" })
            {
                var id = baseId + (suffix == null ? "" : ":" + suffix);
                var patterns = service.ComponentCommands.Select(c => c.Name).ToArray();
                Assert.True(InteractionRouting.OwnsCustomId(id, suffix, patterns));
                Assert.False(InteractionRouting.OwnsCustomId(id + ":other", suffix, patterns));
            }
        }

        [Fact]
        public void DisconnectedPanelShowsSeparateConnectionsAndDisablesConsoleCommands()
        {
            var status = new ControlPanelStatus("Island", ConsoleBotStage.Faulted, false, ConnectionState.Connected,
                false, false, 3, false, null, null, false, false);
            var embed = ControlPanelView.BuildEmbed(status);
            var fields = string.Join("\n", embed.Fields.Select(field => field.Value));
            Assert.Contains("Discord: **Connected**", fields);
            Assert.Contains("Switch: **Disconnected**", fields);
            Assert.NotNull(embed.Timestamp);
            Assert.DoesNotContain("Dodo code", fields);
            var buttons = ControlPanelView.BuildComponents(status, id => id).Components
                .OfType<ActionRowComponent>().SelectMany(row => row.Components).OfType<ButtonComponent>().ToArray();
            Assert.True(buttons.Single(b => b.CustomId == "control:screen-on").IsDisabled);
            Assert.True(buttons.Single(b => b.CustomId == "control:resume").IsDisabled);
            Assert.False(buttons.Single(b => b.CustomId == "control:start").IsDisabled);
            Assert.False(buttons.Single(b => b.CustomId == "control:queue").IsDisabled);
        }

        [Fact]
        public async Task ConsoleFailureCanBeStartedAgainWithoutEndingControllerLifetime()
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int runs = 0, disconnects = 0, errors = 0;
            ConsoleBotController controller = null!;
            controller = new(true, async token =>
            {
                if (Interlocked.Increment(ref runs) == 1) throw new InvalidOperationException("Disconnected");
                controller.MarkRunning();
                await Task.Delay(Timeout.Infinite, token);
            }, () => Interlocked.Increment(ref disconnects), () => { }, () => false, _ => errors++);
            var lifetime = controller.RunAsync(cancel.Token);
            await Until(() => controller.Stage == ConsoleBotStage.Faulted);
            Assert.False(controller.CanAcceptOrders);
            Assert.False(lifetime.IsCompleted);
            Assert.Equal(1, errors);
            Assert.Equal(1, disconnects);
            controller.Start();
            controller.Start();
            await Until(() => controller.Stage == ConsoleBotStage.Running);
            Assert.Equal(2, runs);
            cancel.Cancel();
            await lifetime;
        }

        [Fact]
        public async Task RestartWaitsForActiveOrderAndNeverStartsTwoConsoleLoops()
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            bool active = true;
            int concurrent = 0, runs = 0, maxConcurrent = 0;
            ConsoleBotController controller = null!;
            controller = new(true, async token =>
            {
                runs++;
                maxConcurrent = Math.Max(maxConcurrent, Interlocked.Increment(ref concurrent));
                controller.MarkRunning();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { Interlocked.Decrement(ref concurrent); }
            }, () => { }, () => { }, () => Volatile.Read(ref active), _ => { });
            var lifetime = controller.RunAsync(cancel.Token);
            await Until(() => controller.Stage == ConsoleBotStage.Running);
            controller.Stop(restart: true);
            Assert.False(controller.CanAcceptOrders);
            Assert.False(controller.TryStartOrder(() => throw new Exception("Must not dequeue")));
            await Task.Delay(400);
            Assert.Equal(1, runs);
            Assert.Equal(ConsoleBotStage.Stopping, controller.Stage);
            Volatile.Write(ref active, false);
            await Until(() => runs == 2 && controller.Stage == ConsoleBotStage.Running);
            Assert.True(controller.CanAcceptOrders);
            Assert.Equal(1, maxConcurrent);
            cancel.Cancel();
            await lifetime;
        }

        [Fact]
        public async Task StopBeforeSupervisorStartsDoesNotLeaveItStuckStopping()
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int runs = 0;
            var controller = new ConsoleBotController(true, token =>
            {
                Interlocked.Increment(ref runs);
                return Task.Delay(Timeout.Infinite, token);
            }, () => { }, () => { }, () => false, _ => { });
            controller.Stop();
            var lifetime = controller.RunAsync(cancel.Token);
            await Until(() => controller.Stage == ConsoleBotStage.Stopped);
            Assert.Equal(0, runs);
            controller.Start();
            await Until(() => Volatile.Read(ref runs) == 1);
            cancel.Cancel();
            await lifetime;
        }

        [Fact]
        public async Task GracefulStopWithTheRealSocketCanStartAgainWithoutAFalseFailure()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var connection = new SwitchSocketAsync(new SwitchConnectionConfig
            { IP = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port });
            var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            ConsoleBotController controller = null;
            controller = new(true, async token =>
            {
                connection.Connect();
                controller.MarkRunning();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                // Matches RoutineExecutor.RunAsync, which disconnects after MainLoop returns.
                connection.Disconnect();
            }, connection.DisconnectIfConnected, () => { }, () => false, errors.Enqueue);
            var lifetime = controller.RunAsync(cancel.Token);
            try
            {
                using var first = await listener.AcceptSocketAsync(cancel.Token);
                await Until(() => controller.Stage == ConsoleBotStage.Running);
                controller.Stop();
                await Until(() => controller.Stage is ConsoleBotStage.Stopped or ConsoleBotStage.Faulted);
                Assert.Equal(ConsoleBotStage.Stopped, controller.Stage);
                Assert.False(connection.Connected);
                Assert.Empty(errors);
                controller.Start();
                using var second = await listener.AcceptSocketAsync(cancel.Token);
                await Until(() => controller.Stage == ConsoleBotStage.Running);
                Assert.True(connection.Connected);
                controller.Stop();
                await Until(() => controller.Stage is ConsoleBotStage.Stopped or ConsoleBotStage.Faulted);
                Assert.Equal(ConsoleBotStage.Stopped, controller.Stage);
                Assert.Empty(errors);
            }
            finally
            {
                cancel.Cancel();
                await lifetime;
            }
            Assert.Empty(errors);
        }

        private static async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!condition()) await Task.Delay(20, timeout.Token);
        }
    }
}

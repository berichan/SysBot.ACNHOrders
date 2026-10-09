using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public sealed class BindingCapture
    {
        public string[] Route { get; set; } = Array.Empty<string>();
        public string[] Values { get; set; } = Array.Empty<string>();
    }

    public sealed class BindingProbeModal : IModal
    {
        public string Title => "Binding test";
        [ModalTextInput("query")] public string Query { get; set; } = string.Empty;
    }

    public sealed class BindingProbeModule : InteractionModuleBase<IInteractionContext>
    {
        public BindingCapture Capture { get; set; }
        [ComponentInteraction("probe:*:*:*")]
        public Task Button(string action, string token, string revision)
        { Capture.Route = new[] { action, token, revision }; return Task.CompletedTask; }
        [ComponentInteraction("probe:*:*:*:*")]
        public Task SuffixedButton(string action, string token, string revision, string suffix)
        { Capture.Route = new[] { action, token, revision, suffix }; return Task.CompletedTask; }
        [ComponentInteraction("probe-select:*:*:*")]
        public Task Select(string action, string token, string revision, string[] values)
        { Capture.Route = new[] { action, token, revision }; Capture.Values = values; return Task.CompletedTask; }
        [ComponentInteraction("probe-select:*:*:*:*")]
        public Task SuffixedSelect(string action, string token, string revision, string suffix, string[] values)
        { Capture.Route = new[] { action, token, revision, suffix }; Capture.Values = values; return Task.CompletedTask; }
        [ModalInteraction("probe-modal:*:*")]
        public Task Modal(string token, int revision, BindingProbeModal modal)
        { Capture.Route = new[] { token, revision.ToString() }; Capture.Values = new[] { modal.Query }; return Task.CompletedTask; }
        [ModalInteraction("probe-modal:*:*:*")]
        public Task SuffixedModal(string token, int revision, string suffix, BindingProbeModal modal)
        { Capture.Route = new[] { token, revision.ToString(), suffix }; Capture.Values = new[] { modal.Query }; return Task.CompletedTask; }
    }

    public class InterfaceStub : DispatchProxy
    {
        public Dictionary<string, object> Properties { get; } = new();
        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (Properties.TryGetValue(method.Name, out var value)) return value;
            if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
            return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
        public static T Create<T>(params (string Name, object Value)[] properties) where T : class
        {
            var stub = DispatchProxy.Create<T, InterfaceStub>();
            var proxy = (InterfaceStub)(object)stub;
            foreach (var property in properties) proxy.Properties["get_" + property.Name] = property.Value;
            return stub;
        }
    }

    public sealed class InteractionBindingTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("island")]
        public async Task RealModalHandlerBindsRevisionAndTextFields(string suffix)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<BindingProbeModule>(services);
            var id = "probe-modal:private-token:12" + (suffix == null ? "" : ":" + suffix);
            var field = InterfaceStub.Create<IComponentInteractionData>(("CustomId", "query"), ("Value", "lucky cat"), ("Type", ComponentType.TextInput));
            var data = InterfaceStub.Create<IModalInteractionData>(("CustomId", id), ("Components", new[] { field }));
            var user = InterfaceStub.Create<IUser>(("Id", 123UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", user));
            var context = new InteractionContext(client, interaction);
            var pattern = InteractionRouting.FindPattern(id, suffix, service.ModalCommands.Select(command => command.Name));
            Assert.NotNull(pattern);
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(suffix == null ? new[] { "private-token", "12" } : new[] { "private-token", "12", "island" }, capture.Route);
            Assert.Equal(new[] { "lucky cat" }, capture.Values);
        }

        [Theory]
        [InlineData(false, null)]
        [InlineData(false, "island")]
        [InlineData(true, null)]
        [InlineData(true, "island")]
        public async Task RealDiscordNetHandlersBindRouteSegmentsAndSelectionValues(bool select, string suffix)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<BindingProbeModule>(services);
            var prefix = select ? "probe-select" : "probe";
            var id = prefix + ":pick:private-token:12-0" + (suffix == null ? "" : ":" + suffix);
            var data = InterfaceStub.Create<IComponentInteractionData>(("CustomId", id),
                ("Type", select ? ComponentType.SelectMenu : ComponentType.Button), ("Values", new[] { "0083" }));
            var user = InterfaceStub.Create<IUser>(("Id", 123UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IComponentInteraction>(("Data", data), ("Type", InteractionType.MessageComponent), ("User", user));
            var context = new InteractionContext(client, interaction);
            var pattern = InteractionRouting.FindPattern(id, suffix, service.ComponentCommands.Select(command => command.Name));
            Assert.NotNull(pattern);
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ComponentCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(suffix == null ? new[] { "pick", "private-token", "12-0" } : new[] { "pick", "private-token", "12-0", "island" }, capture.Route);
            if (select) Assert.Equal(new[] { "0083" }, capture.Values);
        }
    }
}

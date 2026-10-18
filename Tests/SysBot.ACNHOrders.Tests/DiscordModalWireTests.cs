using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public sealed class OrderFlowWireProbeModule : InteractionModuleBase<IInteractionContext>
    {
        public BindingCapture Capture { get; set; }
        [ModalInteraction("wire-paste:*:*")]
        public Task Paste(string token, int revision, OrderPasteModal modal) { Capture.Values = new[] { modal.Items }; return Task.CompletedTask; }
        [ModalInteraction("wire-quick:*:*")]
        public Task Quick(string token, int revision, OrderQuickModal modal) { Capture.Values = new[] { modal.Command }; return Task.CompletedTask; }
        [ModalInteraction("wire-options:*:*")]
        public Task Options(string token, int revision, OrderOptionsModal modal) { Capture.Values = new[] { modal.Language, modal.Villager }; return Task.CompletedTask; }
        [ModalInteraction("wire-quantity:*:*")]
        public Task Quantity(string token, int revision, OrderQuantityModal modal) { Capture.Values = new[] { modal.Quantity }; return Task.CompletedTask; }
        [ModalInteraction("wire-recipe:*:*")]
        public Task Recipe(string token, int revision, OrderRecipeSearchModal modal) { Capture.Values = new[] { modal.Query }; return Task.CompletedTask; }
        [ModalInteraction("wire-villager:*:*")]
        public Task Villager(string token, int revision, OrderVillagerSearchModal modal) { Capture.Values = new[] { modal.Query }; return Task.CompletedTask; }
        [ModalInteraction("wire-search:*:*")]
        public Task Search(string token, int revision, OrderSearchModal modal) { Capture.Values = new[] { modal.Query }; return Task.CompletedTask; }
        [ModalInteraction("wire-legacy:*:*")]
        public Task Legacy(string token, int revision, OrderModal modal) { Capture.Values = new[] { modal.Items, modal.Villager, modal.Language }; return Task.CompletedTask; }
        [SlashCommand("wire-nhi", "Test NHI attachment binding")]
        public Task Nhi(IAttachment file) { Capture.Values = new[] { file.Filename }; return Task.CompletedTask; }
    }
    internal static class DiscordModalWire
    {
        internal static IModalInteractionData Parse(DiscordSocketClient client, string json)
        {
            var constructor = typeof(SocketModalData).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var resolver = typeof(Discord.Rest.DiscordRestClient).Assembly.GetType("Discord.Net.Converters.DiscordContractResolver", throwOnError: true);
            var settings = new JsonSerializerSettings
            { ContractResolver = (Newtonsoft.Json.Serialization.IContractResolver)Activator.CreateInstance(resolver, nonPublic: true) };
            var model = JsonConvert.DeserializeObject(json, constructor.GetParameters()[0].ParameterType, settings);
            return (IModalInteractionData)constructor.Invoke(new[] { model, client, null, null, null });
        }
    }

    public sealed class DiscordModalWireTests
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(18, false)]
        [InlineData(1, true)]
        [InlineData(18, true)]
        public async Task MultiFieldOptionsAndLegacyOrderFormsKeepTheirNativeValues(int wrapper, bool legacy)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<OrderFlowWireProbeModule>(services);
            var fields = legacy ? new[] { ("modal-items", "lucky cat"), ("modal-villager", "ジャック"), ("modal-language", "jp") }
                : new[] { ("language", "jp"), ("villager", "ジャック") };
            var containers = fields.Select(pair =>
            {
                var field = new { type = 4, custom_id = pair.Item1, value = pair.Item2 };
                return wrapper == 1 ? (object)new { type = 1, components = new[] { field } } : new { type = 18, component = field };
            }).ToArray();
            var pattern = legacy ? "wire-legacy:*:*" : "wire-options:*:*";
            var id = legacy ? "wire-legacy:token:12" : "wire-options:token:12";
            var data = DiscordModalWire.Parse(client, JsonConvert.SerializeObject(new { custom_id = id, components = containers }));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", InterfaceStub.Create<IUser>(("Id", 1UL), ("Username", "tester"))));
            var context = new InteractionContext(client, interaction);
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(legacy ? new[] { "lucky cat", "ジャック", "jp" } : new[] { "jp", "ジャック" }, capture.Values);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("island")]
        public async Task ExistingSlashNhiAttachmentBindingStillWorksAfterTheUpgrade(string suffix)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<OrderFlowWireProbeModule>(services);
            var name = suffix == null ? "wire-nhi" : "wire-nhi_" + suffix;
            var json = "{\"id\":\"987\",\"name\":\"" + name + "\",\"type\":1,\"options\":[{\"name\":\"file\",\"type\":11,\"value\":\"123\"}],\"resolved\":{\"attachments\":{\"123\":{\"id\":\"123\",\"filename\":\"order.nhi\",\"size\":8,\"url\":\"https://cdn.discordapp.com/attachments/test/order.nhi\",\"proxy_url\":\"https://media.discordapp.net/attachments/test/order.nhi\"}}}}";
            var factory = typeof(SocketSlashCommandData).GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic);
            var resolver = typeof(Discord.Rest.DiscordRestClient).Assembly.GetType("Discord.Net.Converters.DiscordContractResolver", true);
            var modelType = factory.GetParameters().Single(parameter => parameter.ParameterType.FullName == "Discord.API.ApplicationCommandInteractionData").ParameterType;
            var model = JsonConvert.DeserializeObject(json, modelType, new JsonSerializerSettings
            { ContractResolver = (Newtonsoft.Json.Serialization.IContractResolver)Activator.CreateInstance(resolver, true) });
            var arguments = factory.GetParameters().Select(parameter => parameter.ParameterType == modelType ? model : parameter.ParameterType.IsInstanceOfType(client) ? client
                : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null).ToArray();
            var data = (IApplicationCommandInteractionData)factory.Invoke(null, arguments);
            var interaction = InterfaceStub.Create<ISlashCommandInteraction>(("Data", data), ("Type", InteractionType.ApplicationCommand), ("User", InterfaceStub.Create<IUser>(("Id", 1UL), ("Username", "tester"))));
            var result = await service.SlashCommands.Single(command => command.Name == "wire-nhi").ExecuteAsync(new InteractionContext(client, interaction), services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(new[] { "order.nhi" }, capture.Values);
        }
        [Theory]
        [InlineData("paste", "items", "$order 00F000016A2", 1)]
        [InlineData("paste", "items", "$order 00F000016A2", 18)]
        [InlineData("quick", "command", "$ordercat 0083 villager:ost10", 1)]
        [InlineData("quick", "command", "$ordercat 0083 villager:ost10", 18)]
        [InlineData("quantity", "quantity", "3", 1)]
        [InlineData("quantity", "quantity", "3", 18)]
        [InlineData("recipe", "query", "木製椅", 1)]
        [InlineData("recipe", "query", "木製椅", 18)]
        [InlineData("villager", "query", "ジャック", 1)]
        [InlineData("villager", "query", "ジャック", 18)]
        [InlineData("search", "query", "lucky cat", 1)]
        [InlineData("search", "query", "lucky cat", 18)]
        public async Task EachExistingOrderingFormBindsThroughTheRealWireDecoder(string flow, string fieldName, string value, int wrapper)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<OrderFlowWireProbeModule>(services);
            var field = new { type = 4, custom_id = fieldName, value };
            object container = wrapper == 1 ? new { type = 1, components = new[] { field } } : new { type = 18, component = field };
            var id = $"wire-{flow}:token:12";
            var data = DiscordModalWire.Parse(client, JsonConvert.SerializeObject(new { custom_id = id, components = new[] { container } }));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", InterfaceStub.Create<IUser>(("Id", 1UL), ("Username", "tester"))));
            var context = new InteractionContext(client, interaction);
            var pattern = $"wire-{flow}:*:*";
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(new[] { value }, capture.Values);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("island")]
        public async Task NativeFileUploadJsonIsDecodedAndBoundByTheUpgradedLibrary(string suffix)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new NhiBindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<NhiBindingProbeModule>(services);
            var id = "shop-nhi:token:12" + (suffix == null ? "" : ":" + suffix);
            var json = JsonConvert.SerializeObject(new
            {
                custom_id = id,
                components = new[] { new { type = 18, id = 1, component = new { type = 19, custom_id = "file", values = new[] { "123" } } } },
                resolved = new { attachments = new { _placeholder = 0 } }
            });
            var body = Newtonsoft.Json.Linq.JObject.Parse(json);
            body["resolved"]["attachments"] = new Newtonsoft.Json.Linq.JObject
            {
                ["123"] = Newtonsoft.Json.Linq.JObject.FromObject(new { id = "123", filename = "order.nhi", size = 8,
                    url = "https://cdn.discordapp.com/attachments/test/order.nhi", proxy_url = "https://media.discordapp.net/attachments/test/order.nhi" })
            };
            var data = DiscordModalWire.Parse(client, body.ToString());
            Assert.Equal(ComponentType.FileUpload, Assert.Single(data.Components).Type);
            Assert.Equal("123", Assert.Single(Assert.Single(data.Components).Values));
            var file = Assert.Single(data.Attachments);
            Assert.Equal("order.nhi", file.Filename);
            var user = InterfaceStub.Create<IUser>(("Id", 1UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", user));
            var context = new InteractionContext(client, interaction);
            var pattern = InteractionRouting.FindPattern(id, suffix, service.ModalCommands.Select(command => command.Name));
            InteractionRouting.BindMatches(context, id, pattern);
            var result = await service.ModalCommands.First(command => command.Name == pattern).ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Same(file, capture.File);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(18)]
        public async Task BothLegacyRowsAndNewLabelTextFieldsReachExistingOrderModals(int wrapper)
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync });
            var capture = new BindingCapture();
            using var services = new ServiceCollection().AddSingleton(capture).BuildServiceProvider();
            await service.AddModuleAsync<BindingProbeModule>(services);
            var field = new { type = 4, custom_id = "query", value = "もくせいチェア" };
            object container = wrapper == 1 ? new { type = 1, components = new[] { field } } : new { type = 18, component = field };
            var data = DiscordModalWire.Parse(client, JsonConvert.SerializeObject(new { custom_id = "probe-modal:token:12", components = new[] { container } }));
            var user = InterfaceStub.Create<IUser>(("Id", 1UL), ("Username", "tester"));
            var interaction = InterfaceStub.Create<IModalInteraction>(("Data", data), ("Type", InteractionType.ModalSubmit), ("User", user));
            var context = new InteractionContext(client, interaction);
            InteractionRouting.BindMatches(context, data.CustomId, "probe-modal:*:*");
            var result = await service.ModalCommands.First(command => command.Name == "probe-modal:*:*").ExecuteAsync(context, services);
            Assert.True(result.IsSuccess, result.ErrorReason);
            Assert.Equal(new[] { "もくせいチェア" }, capture.Values);
        }
    }
}

using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Discord.Interactions;
using Discord.WebSocket;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public sealed class InteractionConfigurationTests
    {
        [Fact]
        public void CommandModeSerializesAsANameAndAcceptsTheLegacyBoolean()
        {
            var config = new CrossBotConfig
            {
                CommandMode = DiscordCommandMode.InteractionCommands,
            };

            var json = JsonSerializer.Serialize(config);

            json.Should().Contain("\"CommandMode\":\"InteractionCommands\"");
            json.Should().NotContain("\"UseInteractionCommands\"");

            var legacy = JsonSerializer.Deserialize<CrossBotConfig>("{\"UseInteractionCommands\":true}");
            legacy.Should().NotBeNull();
            legacy!.CommandMode.Should().Be(DiscordCommandMode.InteractionCommands);

            var legacyText = JsonSerializer.Deserialize<CrossBotConfig>("{\"UseInteractionCommands\":false}");
            legacyText.Should().NotBeNull();
            legacyText!.CommandMode.Should().Be(DiscordCommandMode.TextCommands);
        }

        [Fact]
        public async Task InteractionModulesLoadWithValidModalContracts()
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client);
            using var services = new ServiceCollection()
                .AddSingleton(client)
                .AddSingleton(service)
                .BuildServiceProvider();

            await service.AddModulesAsync(Assembly.GetAssembly(typeof(SysCord))!, services);

            service.SlashCommands.Should().NotBeEmpty();
            service.SlashCommands.Count.Should().BeLessOrEqualTo(100);
            service.ComponentCommands.Should().NotBeEmpty();
            service.ModalCommands.Should().Contain(command => command.Name.StartsWith("shop-search:"));
            service.ModalCommands.Should().Contain(command => command.Name.StartsWith("shop-paste:"));
            service.ModalCommands.Should().Contain(command => command.Name == "shop-quick:*:*");
            service.ModalCommands.Should().Contain(command => command.Name == "shop-quick:*:*:*");
            service.ModalCommands.Should().Contain(command => command.Name.StartsWith("shop-options:"));
            service.ModalCommands.Should().Contain(command => command.Name == "shop-quantity:*:*");
            service.ModalCommands.Should().Contain(command => command.Name == "shop-quantity:*:*:*");
            service.ModalCommands.Should().Contain(command => command.Name == "shop-villager:*:*");
            service.ModalCommands.Should().Contain(command => command.Name == "shop-villager:*:*:*");
            service.SlashCommands.Should().Contain(command => command.Name == "setup-control");
            service.ComponentCommands.Should().Contain(command => command.Name == "control:*");
            service.ModalCommands.Select(command => command.Modal).Should().OnlyContain(modal => modal != null);
            foreach (var command in service.ComponentCommands.Where(command => command.Name.StartsWith("shop-select:")))
                command.Parameters.Last().ParameterType.Should().Be(typeof(string[]));

            var buildCommand = typeof(SysCord).GetMethod("BuildCommand", BindingFlags.NonPublic | BindingFlags.Static);
            buildCommand.Should().NotBeNull();
            foreach (var command in service.SlashCommands)
                buildCommand!.Invoke(null, new object[] { command, command.Name }).Should().NotBeNull();
        }

        [Fact]
        public async Task InteractionCommandNamesRemainWithinDiscordLimitsWithSuffix()
        {
            using var client = new DiscordSocketClient();
            var service = new InteractionService(client);
            using var services = new ServiceCollection()
                .AddSingleton(client)
                .AddSingleton(service)
                .BuildServiceProvider();

            await service.AddModulesAsync(Assembly.GetAssembly(typeof(SysCord))!, services);

            foreach (var command in service.SlashCommands)
            {
                var name = $"{command.Name}_my-island";
                name.Length.Should().BeLessOrEqualTo(32);
                name.Should().MatchRegex("^[a-z0-9_-]+$");
            }
        }
    }
}

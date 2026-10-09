using System;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Xunit;

namespace SysBot.ACNHOrders.Tests
{
    public class DeferralProbe : DispatchProxy
    {
        public IUserMessage Message;
        public bool Responded;
        public string Method;
        public bool Ephemeral;
        public int Calls;
        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "get_HasResponded") return Responded;
            if (method.Name == "get_Message") return Message;
            if (method.ReturnType != typeof(Task)) throw new InvalidOperationException(method.Name);
            Method = method.Name; Ephemeral = (bool)args[0]; Calls++; Responded = true;
            return Task.CompletedTask;
        }
    }

    public sealed class PrivateInteractionResponseTests
    {
        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public async Task ComponentsAndModalsOnlyUpdateMessagesThatAreAlreadyPrivate(bool component, bool isPrivate)
        {
            IDiscordInteraction interaction = component
                ? DispatchProxy.Create<IComponentInteraction, DeferralProbe>()
                : DispatchProxy.Create<IModalInteraction, DeferralProbe>();
            var capture = (DeferralProbe)interaction;
            capture.Message = InterfaceStub.Create<IUserMessage>(("Flags", isPrivate ? MessageFlags.Ephemeral : MessageFlags.None));
            await PrivateInteractionResponse.DeferAsync(interaction);
            Assert.Equal(isPrivate ? "DeferAsync" : "DeferLoadingAsync", capture.Method);
            Assert.Equal(!isPrivate, capture.Ephemeral);
            await PrivateInteractionResponse.DeferAsync(interaction);
            Assert.Equal(1, capture.Calls);
        }

        [Fact]
        public async Task SlashCommandsAndModalsWithoutAMessageCreateAPrivateReply()
        {
            foreach (IDiscordInteraction interaction in new IDiscordInteraction[]
            {
                DispatchProxy.Create<IDiscordInteraction, DeferralProbe>(),
                DispatchProxy.Create<IModalInteraction, DeferralProbe>()
            })
            {
                var capture = (DeferralProbe)interaction;
                await PrivateInteractionResponse.DeferAsync(interaction);
                Assert.True(capture.Ephemeral);
                Assert.Equal(interaction is IModalInteraction ? "DeferLoadingAsync" : "DeferAsync", capture.Method);
            }
        }
    }
}

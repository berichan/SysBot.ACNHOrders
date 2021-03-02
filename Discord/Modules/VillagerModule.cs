using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders
{
    // ReSharper disable once UnusedType.Global
    public class VillagerModule : ModuleBase<SocketCommandContext>
    {

        [Command("injectVillager"), Alias("iv")]
        [Summary("Injects a villager based on the internal name.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task InjectVillagerAsync(int index, string internalName)
        {
            var bot = Globals.Bot;
            var nameSearched = internalName;

            if (!VillagerResources.IsVillagerDataKnown(internalName))
                internalName = GameInfo.Strings.VillagerMap.FirstOrDefault(z => string.Equals(z.Value, internalName, StringComparison.InvariantCultureIgnoreCase)).Key;

            if (internalName == default)
            {
                await ReplyAsync($"{Context.User.Mention} - {nameSearched} is not a valid internal villager name.");
                return;
            }

            if (index > byte.MaxValue || index < 0)
            {
                await ReplyAsync($"{Context.User.Mention} - {index} is not a valid index");
                return;
            }

            int slot = index;

            var replace = VillagerResources.GetVillager(internalName);
            var user = Context.User;
            var mention = Context.User.Mention;
            var request = new VillagerRequest(replace, (byte)index)
            {
                OnFinish = success =>
                {
                    var reply = success
                        ? $"Villager has been injected by the bot at Index {slot}. Please go talk to them!"
                        : "Failed to inject villager. Please tell the bot owner to look at the logs!";
                    Task.Run(async () => await ReplyAsync($"{mention}: {reply}").ConfigureAwait(false));
                }
            };

            bot.VillagerInjections.Enqueue(request);

            var msg = $"{mention}: Villager inject request has been added to the queue and will be injected momentarily. I will reply to you once this has completed.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("injectVillager"), Alias("iv")]
        [Summary("Injects a villager based on the internal name.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task InjectVillagerAsync(string internalName) => await InjectVillagerAsync(0, internalName).ConfigureAwait(false);

        [Command("villagerName")]
        [Alias("vn", "nv", "name")]
        [Summary("Gets the internal name of a villager.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetVillagerInternalNameAsync([Summary("Language code to search with")] string language, [Summary("Villager name")][Remainder] string villagerName)
        {
            var strings = GameInfo.GetStrings(language);
            await ReplyVillagerName(strings, villagerName).ConfigureAwait(false);
        }

        [Command("villagerName")]
        [Alias("vn", "nv", "name")]
        [Summary("Gets the internal name of a villager.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetVillagerInternalNameAsync([Summary("Villager name")][Remainder] string villagerName)
        {
            var strings = GameInfo.Strings;
            await ReplyVillagerName(strings, villagerName).ConfigureAwait(false);
        }

        private async Task ReplyVillagerName(GameStrings strings, string villagerName)
        {
            var map = strings.VillagerMap;
            var result = map.FirstOrDefault(z => string.Equals(villagerName, z.Value, StringComparison.InvariantCultureIgnoreCase));
            if (string.IsNullOrWhiteSpace(result.Key))
            {
                await ReplyAsync($"No villager found of name {villagerName}.").ConfigureAwait(false);
                return;
            }
            await ReplyAsync($"{villagerName}={result.Key}").ConfigureAwait(false);
        }
    }
}
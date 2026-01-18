using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class BanModule : ModuleBase<SocketCommandContext>
    {
        [Command("unBan")]
        [Summary("unbans a user by their long number id.")]
        [RequireSudo]
        public async Task UnBanAsync(string id)
        {
            if (GlobalBan.IsBanned(id))
            {
                GlobalBan.UnBan(id);
                await ReplyAsync($"{id} has been abuse-unbanned.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"{id} could not be found in the ban list.").ConfigureAwait(false);
            }
        }

        [Command("ban")]
        [Summary("bans a user by their long number id.")]
        [RequireSudo]
        public async Task BanAsync(string id)
        {
            if (GlobalBan.IsBanned(id))
            {
                await ReplyAsync($"{id} is already abuse-banned").ConfigureAwait(false);
            }
            else
            {
                GlobalBan.Ban(id);
                await ReplyAsync($"{id} has been abuse-banned.").ConfigureAwait(false);
            }
        }

        [Command("checkBan")]
        [Summary("checks a user's ban state by their long number id.")]
        [RequireSudo]
        public async Task CheckBanAsync(string id) => await ReplyAsync(GlobalBan.IsBanned(id) ? $"{id} is abuse-banned" : $"{id} is not abuse-banned").ConfigureAwait(false);

        [Command("restrict")]
        [Summary("temporarily restricts a user by their long number account id.")]
        [RequireSudo]
        public async Task RestrictAsync(ulong id)
        {
            if (GlobalBan.IsTempRestricted(id))
            {
                await ReplyAsync($"{id} is already temporarily restricted").ConfigureAwait(false);
            }
            else
            {
                GlobalBan.TempRestrict(id);
                await ReplyAsync($"{id} has been temporarily restricted.").ConfigureAwait(false);
            }
        }

        [Command("unRestrict")]
        [Summary("removes temporary restriction from a user by their long number account id.")]
        [RequireSudo]
        public async Task UnRestrictAsync(ulong id)
        {
            if (GlobalBan.IsTempRestricted(id))
            {
                GlobalBan.RemoveTempRestrict(id);
                await ReplyAsync($"{id} has been removed from temporary restriction.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"{id} is not temporarily restricted").ConfigureAwait(false);
            }
        }

        [Command("checkRestrict")]
        [Summary("checks a user's temporary restriction state by their long number account id.")]
        [RequireSudo]
        public async Task CheckRestrictAsync(ulong id) => await ReplyAsync(GlobalBan.IsTempRestricted(id) ? $"{id} is temporarily restricted" : $"{id} is not temporarily restricted").ConfigureAwait(false);
    
    }
}

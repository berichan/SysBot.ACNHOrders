using System;
using System.Collections.Generic;
using System.Linq;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public sealed record CrossBotConfig : SwitchConnectionConfig
    {
        #region Discord

        /// <summary> When enabled, the bot will accept commands from users via Discord. </summary>
        public bool AcceptingCommands { get; set; } = true;

        /// <summary> Custom Discord Status for playing a game. </summary>
        public string Name { get; set; } = "CrossBot";

        /// <summary> Bot login token. </summary>
        public string Token { get; set; } = "DISCORD_TOKEN";

        /// <summary> Bot command prefix. </summary>
        public string Prefix { get; set; } = "$";

        /// <summary> Users with this role are allowed to interact with the bot. If "@everyone", anyone can interact. </summary>
        public string RoleUseBot { get; set; } = "@everyone";

        // 64bit numbers white-listing certain channels/users for permission
        public List<ulong> Channels { get; set; } = new();
        public List<ulong> Users { get; set; } = new();
        public List<ulong> Sudo { get; set; } = new();

        #endregion

        #region Features

        /// <summary> Skips creating bots when the program is started; helpful for testing integrations. </summary>
        public bool SkipConsoleBotCreation { get; set; }

        /// <summary> When enabled, the Bot will not allow RAM edits if the player's item metadata is invalid. </summary>
        /// <remarks> Only disable this as a last resort, and you have corrupted your item metadata through other means. </remarks>
        public bool RequireValidInventoryMetadata { get; set; } = true;

        public DropBotConfig DropConfig { get; set; } = new();

        public OrderBotConfig OrderConfig { get; set; } = new();

        public DodoRestoreConfig DodoModeConfig { get; set; } = new();

        /// <summary> When enabled, users in Discord can request the bot to pick up items (spamming Y a <see cref="DropBotConfig.PickupCount"/> times). </summary>
        public bool AllowClean { get; set; }

        /// <summary> Allows for the use of people to use the $lookup command </summary>
        public bool AllowLookup { get; set; }

        /// <summary> The filename to use when saving position and rotation anchors </summary>
        public string AnchorFilename { get; set; } = "Anchors.bin";

        /// <summary> Whether to stop the main loop and allow the user to update their anchors </summary>
        public bool ForceUpdateAnchors { get; set; } = false;

        public int MapPlaceX { get; set; } = -1;
        public int MapPlaceY { get; set; } = -1;

        /// <summary> How many bytes to pull at a time. Lower = slower but less likely to crash </summary>
        public int MapPullChunkSize { get; set; } = 4096;

        /// <summary> If Channels is populated, should anything invalid (such as general conversation) be removed? Does not apply to sudo </summary>
        public bool DeleteNonCommands { get; set; } = false;

        /// <summary> Extra time to wait between dodo talk keypresses in milliseconds </summary>
        public int DialogueButtonPressExtraDelay { get; set; } = 0;

        /// <summary> Extra time to wait before game gets restarted. Possibly useful if you have to wait for the "checking if game can be played" wheel </summary>
        public int RestartGameWait { get; set; } = 0;

        /// <summary> How much extra time, if any, should we wait while orville is connecting to the internet in milliseconds </summary>
        public int ExtraTimeConnectionWait { get; set; } = 1000;

        /// <summary> Should we not use instant text? </summary>
        public bool LegacyDodoCodeRetrieval { get; set; } = false;

        /// <summary> Should we allow villager injection? </summary>
        public bool AllowVillagerInjection { get; set; } = true;

        public string BlockerEmoji { get; set; } = "\u2764";

        #endregion

        public bool CanUseCommandUser(ulong authorId) => Users.Count == 0 || Users.Contains(authorId);
        public bool CanUseCommandChannel(ulong channelId) => Channels.Count == 0 || Channels.Contains(channelId);
        public bool CanUseSudo(ulong userId) => Sudo.Contains(userId);

        public bool GetHasRole(string roleName, IEnumerable<string> roles)
        {
            return roleName switch
            {
                nameof(RoleUseBot) => roles.Contains(RoleUseBot),
                _ => throw new ArgumentException($"{roleName} is not a valid role type.", nameof(roleName)),
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class TwitchConfig
    {
        private const string Startup = nameof(Startup);
        private const string Operation = nameof(Operation);
        private const string Messages = nameof(Messages);
        public override string ToString() => "Twitch Integration Settings";

        // Startup

        ///<summary>Bot Login Token</summary>
        public string Token { get; set; } = string.Empty;

        ///<summary>Bot Username</summary>
        public string Username { get; set; } = string.Empty;

        ///<summary>Channel to Send Messages To</summary>
        public string Channel { get; set; } = string.Empty;

        ///<summary>Bot Command Prefix</summary>
        public char CommandPrefix { get; set; } = '$';

        // Messaging

        ///<summary>Throttle the bot from sending messages if X messages have been sent in the past Y seconds.</summary>
        public int ThrottleMessages { get; set; } = 100;

        ///<summary>Throttle the bot from sending messages if X messages have been sent in the past Y seconds.</summary>
        public double ThrottleSeconds { get; set; } = 30;

        ///<summary>Throttle the bot from sending whispers if X messages have been sent in the past Y seconds.</summary>
        public int ThrottleWhispers { get; set; } = 100;

        ///<summary>Throttle the bot from sending whispers if X messages have been sent in the past Y seconds.</summary>
        public double ThrottleWhispersSeconds { get; set; } = 60;

        // Operation

        ///<summary>Sudo Usernames</summary>
        public string SudoList { get; set; } = string.Empty;

        ///<summary>Users with these usernames cannot use the bot.</summary>
        public string UserBlacklist { get; set; } = string.Empty;

        ///<summary>When enabled, the bot will process commands sent to the channel.</summary>
        public bool AllowCommandsViaChannel { get; set; } = true;

        ///<summary>When enabled, the bot will allow users to send command via whisper (bypasses slow mode)</summary>
        public bool AllowCommandsViaWhisper { get; set; }

        // Message Destinations

        ///<summary>Determines where generic notifications are sent.</summary>
        public TwitchMessageDestination NotifyDestination { get; set; } = TwitchMessageDestination.Channel;

        ///<summary>Determines where Start notifications are sent.</summary>
        public TwitchMessageDestination OrderStartDestination { get; set; } = TwitchMessageDestination.Channel;

        ///<summary>Determines where Wait notifications are sent. Cannot be public</summary>
        public TwitchMessageDestination OrderWaitDestination { get; set; } = TwitchMessageDestination.Channel;

        ///<summary>Determines where Finish notifications are sent.</summary>
        public TwitchMessageDestination OrderFinishDestination { get; set; }

        ///<summary>Determines where Canceled notifications are sent.</summary>
        public TwitchMessageDestination OrderCanceledDestination { get; set; } = TwitchMessageDestination.Channel;

        ///<summary>Determines where Finish notifications are sent.</summary>
        public TwitchMessageDestination UserDefinedCommandsDestination { get; set; }

        ///<summary>Determines where Canceled notifications are sent.</summary>
        public TwitchMessageDestination UserDefinedSubOnlyCommandsDestination { get; set; }

        // Commands

        public bool AllowDropViaTwitchChat { get; set; } = false;

        /// <summary> Dictionary of user-defined commands</summary>
        public Dictionary<string, string> UserDefinitedCommands { get; set; } = 
            new Dictionary<string, string>() { 
                { "island", "The dodo code for {islandname} is {dodo}. There are currently {vcount} visitors on {islandname}." }, 
                { "islandlist", "The following people are on {islandname}: {visitorlist}." },
                { "villagers", "The following villagers may be adopted on {islandname}: {villagerlist}." },
                { "custom", "Hello, @{user}!" }
            };

        public Dictionary<string, string> UserDefinedSubOnlyCommands { get; set; } =
            new Dictionary<string, string>() {
                { "subdodo", "The dodo code for {islandname} is {dodo}. There are currently {vcount} visitors on {islandname}." },
                { "sub", "Hello, @{user}! Thanks for being a subscriber!" }
            };

        public bool IsSudo(string username)
        {
            var sudos = SudoList.Split(new[] { ",", ", ", " " }, StringSplitOptions.RemoveEmptyEntries);
            return sudos.Contains(username);
        }
    }

    public enum TwitchMessageDestination
    {
        Disabled,
        Channel,
        Whisper,
    }
}

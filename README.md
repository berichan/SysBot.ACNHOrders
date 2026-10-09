# SysBot.ACNHOrders
Designed as a fully automated queue-based order bot that injects item orders directly onto your island's map and lets the player that queued pick them up, then leave. All dodo fetching, gate opening & closing, movement and dialoguing (including Tom Nook's or Isabelle's morning announcement progression) is automated.

Alternatively, you may use this as a fully automated connection, logging, advanced item dropping and map refresh bot (among other things) that restores the online session after a network or online crash/disconnect. [Read here](https://github.com/berichan/SysBot.ACNHOrders/wiki/Automatic-dodo-online-session-restoring) for setup instructions.

![License](https://img.shields.io/badge/License-AGPLv3-blue.svg)

## See also

[SysBot.AnimalCrossing](https://github.com/kwsch/SysBot.AnimalCrossing)

[kwsch](https://github.com/kwsch) for the original project and everything that makes this work.

[Red](https://github.com/hp3721) for the original Dodo-fetch code, pointer logic and a bunch of other things that make this possible.

Resources:

[Read the wiki](https://github.com/berichan/SysBot.ACNHOrders/wiki) for setup instructions and faq.

[Watch the bot showcase](https://youtu.be/Y-_Tg8bwveY) to see it in action from the host's POV.

[Watch how to order items using ACNHMS](https://youtu.be/SWVAf7uyyuw) to be used by the end-user of your bot.

## Support Discord:

[<img src="https://canary.discordapp.com/api/guilds/771477382409879602/widget.png?style=banner2">](https://discord.gg/5bT8XK8dYe)

[sys-botbase](https://github.com/olliz0r/sys-botbase) client for remote control automation of Nintendo Switch consoles.

Uses [Discord.Net](https://github.com/discord-net/Discord.Net) as a dependency via NuGet.

## Discord command mode

New configurations use prefixed text commands by default. To use slash commands without the privileged Message Content or Server Members intents, set:

```json
{
  "CommandMode": "InteractionCommands",
  "SlashCommandSuffix": "my-island"
}
```

`SlashCommandSuffix` is optional in interaction mode and is normalized to lowercase (spaces become underscores). The bot registers its complete command set as guild commands, so each island should use its own Discord application and bot token; Discord limits an application to 100 guild chat-input commands, and this bot currently exposes 71. A suffix is useful for identifying an island's commands (for example `/order_my-island`) but does not make multiple full command sets fit in one application. Changing a suffix reconciles the guild command set on the next startup; rerun `/setup-embed` after changing it so persistent buttons use the current command namespace. If the value cannot fit Discord's 32-character command-name limit, startup fails with a configuration error. File orders use the `file` option on the corresponding `/order-nhi` command (such as `/order-nhi_my-island`) and do not require reading message attachments.

Text-command mode requires the Message Content and Server Members privileged intents to be enabled for the bot in Discord's Developer Portal. Interaction mode uses only non-privileged gateway intents. Mentioning the bot returns ordering buttons and instructions for pasting an old command into the order form.

## Order panel

In interaction mode, run `/setup-embed` as the application owner or a Sudo user, then pin the panel. Members use **Find items**, **Place order (guided)**, **Place order (quick)**, **My order**, and **Help**. Set `AllowLookup` to `true` to enable item search and suggestions in lookup commands.

Guided orders let members find items, choose how many copies to add, paste lists or old commands, upload `.nhi` files with `/order-nhi`, choose presets, or load their last order. They review the items privately before pressing **Confirm order** to join the queue. Quick orders accept a pasted `order` or `ordercat` command and submit directly when the member presses Submit. The command selects Standard or Catalogue automatically, and a plain item list uses Standard. Both paths check item safety, permissions, DMs, and queue availability. All stackable items use full stacks in every mode. Standard fills 40 slots with variants. Exact keeps chosen items, variants, and quantities. Catalogue uses the existing separator layout.

**My order** shows queue position and order status, including when the island is ready to visit. Item lists and panel IDs are saved in `OrderData`, so keep that folder when updating. Waiting orders and current status are cleared when the bot restarts.

Read the [setup guide](docs/wiki/Order-panel-setup.md), [ordering guide](docs/wiki/How-to-order-with-the-panel.md), and [troubleshooting guide](docs/wiki/Order-panel-troubleshooting.md). The [wiki guide](docs/wiki/README.md) explains how to publish these pages.

## Sudo control panel

Run `/setup-control` in a staff channel to create a saved control panel, or `/control-panel` for a private copy. The channel does not need to be in `Channels`. Every control checks for a Sudo user or the application owner.

The panel shows Discord and Switch connection status and provides start, stop, reconnect, pause/resume, screen, controller, queue, and restore-mode controls. Stopping or restarting the console loop waits for the current order to finish and keeps Discord and waiting orders available. Status updates about every 15 seconds; check its timestamp if the bot goes offline.

Read the [control panel guide](docs/wiki/Control-panel.md) for setup and details.

## Other Dependencies
Animal Crossing API logic is provided by [NHSE](https://github.com/kwsch/NHSE/).

# License
Refer to the `License.md` for details regarding licensing.

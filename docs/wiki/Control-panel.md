# Control panel

Sudo users can manage the bot from a Discord panel instead of typing separate status, start, stop, and screen commands. The panel works in interaction mode.

## Setup

1. Set `CommandMode` to `InteractionCommands` and add the operators' Discord user IDs to `Sudo` in `config.json`.
2. Restart the application after updating the configuration.
3. As a Sudo user or the application owner, run `/setup-control` in a server text channel. You can choose another channel using its `channel` option.
4. Pin the panel. If you set `SlashCommandSuffix`, use the matching command, such as `/setup-control_berry`.

The control channel does **not** need to appear in `Channels`. Sudo users can also open a private panel with `/control-panel` from any channel. Panel access does not depend on the order role or the user whitelist. The bot checks Sudo access on every button press, even if `IgnoreAllPermissions` is enabled.

Give the bot View Channel, Send Messages, Embed Links, and Read Message History in the control channel. Use a staff channel if you want to keep the status private. Members who can see a panel still cannot use its controls without Sudo access.

Running `/setup-control` again updates that channel's saved panel. Run it again if you delete the panel or change the command suffix. Panels can be placed in several channels.

## Status

The panel shows:

- Discord connection status.
- Switch connection status and whether the console bot is starting, running, stopping, stopped, or stopped after an error.
- Whether requests are open or paused, the number of waiting orders, and whether an order is in progress.
- The last screen command and battery reading. These values may be older than the current status update.
- Restore-mode settings for Mash B and map refresh.

The panel updates about every 15 seconds. **Refresh status** opens a private copy with the latest status. The public panel does not show a Dodo code or the names of people ordering. **View queue** shows waiting names privately to the operator.

Discord stays running if the console loop stops or fails, so operators can check the panel and reconnect the Switch. A failed console loop pauses requests. Fix the connection or console problem, then press **Start bot**.

If the Discord connection drops, the bot tries to mark the panel Disconnected. If Discord's API or the server's network is also unavailable, it cannot update the message. Check the timestamp: a panel whose updates have stopped may show an old Connected status. The panel updates again after Discord reconnects. If the application itself has stopped, restart it on the server.

## Controls

| Control | What it does |
| --- | --- |
| Start bot | Starts the console loop or reconnects it after an error, and opens requests. It does not launch a stopped server process. |
| Pause requests | Pauses new requests and queue processing. The current order can finish. |
| Resume requests | Opens requests while the console loop is starting or running. |
| Stop bot | Pauses requests, waits for the current order to finish, then stops the console loop and disconnects the Switch. Discord and waiting orders stay available. |
| Restart connection | Pauses requests, waits for the current order to finish, then stops and starts the console loop. Requests stay paused until you press Resume requests. |
| Screen on / Screen off | Sends the corresponding screen command to the connected Switch. |
| Detach controller | Detaches the virtual controller. The bot may attach it again when its next action runs. |
| View queue | Shows waiting names in a private reply. |
| New Dodo code | Requests a game restart and a new code in restore mode. Visitors will be disconnected. |
| Mash B on / off | Changes automatic dialogue button presses in restore mode. |
| Map refresh on / off | Changes automatic map refresh in restore mode. |
| Guide | Opens a short explanation of the controls. |

Stop, restart, controller detach, and new Dodo requests need confirmation. A confirmation expires after five minutes and can only be used once by the operator who opened it.

Screen and controller commands require a Switch connection. New Dodo code, Mash B, and Map refresh only apply to restore mode. `SkipConsoleBotCreation: true` disables starting and restarting the console loop; change it in the configuration and restart the application to enable console control.

## Saved settings

Control panel message IDs are saved in `OrderData/control-panels.json`. Keep `OrderData` when upgrading.

Button changes apply to the running application. Edit `config.json` for settings you want to keep after restarting the server process. Waiting orders stay queued when you stop or restart the console loop from the panel. They are cleared if the whole application restarts.

Existing admin commands remain available as shortcuts. The panel provides the common controls in one place; advanced memory, anchor, and moderation commands still use their existing commands.

## Check the setup

Before handing the panel to your operators:

1. Create it in a staff channel outside `Channels`. Check that a Sudo user can use it and a normal member cannot.
2. Pause and resume requests, then check the order panel and queue.
3. During an order, request Stop bot. Check that the current visit finishes, waiting orders remain, and Discord stays available.
4. Start the bot again and try Restart connection. Check that it reconnects with requests paused.
5. Test Screen on and Screen off while connected. Check the last command shown on the panel.
6. With no current order, disconnect the Switch. Check that its status changes and the control panel remains available. Restore the connection, then use Start bot.
7. Restart the application with no orders waiting. Check that the saved control panel resumes updating.

Automated tests cover access rules, button registration, confirmations, and console lifecycle changes. The checks above also test your Discord permissions and physical console connection.

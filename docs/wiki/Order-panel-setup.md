# Order panel setup

Members use the Discord order panel to choose Guided or Quick ordering and check their queue position. The panel works in low-intent mode, without the Message Content or Server Members privileged intents.

For a first installation, follow the existing [Discord bot setup](https://github.com/berichan/SysBot.ACNHOrders/wiki/Discord-bot-setup) instructions for the console connection, map position, and five anchors. Then use this guide to set up the Discord panel.

## Update an existing bot

1. Let the current order finish, then stop the bot. Waiting orders are cleared when the bot stops.
2. Back up your bot folder, including `config.json`, `Anchors.bin`, presets, `UserOrder`, and `OrderData` if present.
3. Build this version with the .NET 10 SDK:

   ```powershell
   dotnet test Tests/SysBot.ACNHOrders.Tests/SysBot.ACNHOrders.Tests.csproj
   dotnet publish SysBot.ACNHOrders.csproj -f net10.0 -c Release -o publish
   ```

4. Copy the files from `publish` into your server's bot folder. Keep your existing configuration, anchors, presets, and saved orders. The server needs the .NET 10 runtime.
5. Update the settings below, then start the bot.

Run `dotnet SysBot.ACNHOrders.dll` from the bot folder. On Windows, you can also run `SysBot.ACNHOrders.exe`. If you use a service or startup script, keep its working directory the same so the bot can find its configuration and saved orders. Each island needs its own bot folder, Discord application, and token.

## Discord permissions

Use your island's application in the Discord Developer Portal. Invite the bot with the `bot` and `applications.commands` scopes. If your older invite did not include slash commands, invite it again with both scopes.

Interaction mode does not need the Message Content or Server Members privileged intents. You can leave them disabled. Enable both if you switch back to text commands.

Give the bot these permissions in the order channel:

- View Channel
- Send Messages
- Embed Links
- Read Message History, so it can update the saved panel
- Attach Files, if you use commands that send files

Members need View Channel and Use Application Commands. A moderator can pin the panel. The bot does not need Administrator or Manage Messages to run the panel.

## Configuration

Add or update these fields in `config.json`. Keep your other settings, including your IP, port, map coordinates, and anchors.

```json
{
  "CommandMode": "InteractionCommands",
  "SlashCommandSuffix": "",
  "AcceptingCommands": true,
  "AllowLookup": true,
  "RoleUseBot": "@everyone",
  "Channels": [123456789012345678],
  "Sudo": [234567890123456789],
  "SkipConsoleBotCreation": false,
  "DodoModeConfig": {
    "LimitedDodoRestoreOnlyMode": false
  },
  "OrderConfig": {
    "MaxQueueCount": 50,
    "UserTimeAllowed": 180,
    "WaitForArriverTime": 60,
    "NHIPresetsDirectory": "presets"
  }
}
```

Replace the example IDs with your order channel and bot administrator IDs. Enable Discord's Developer Mode to use Copy ID in the right-click menu.

| Setting | What it does |
| --- | --- |
| `CommandMode` | Use `InteractionCommands` for the panel and slash commands, or `TextCommands` for chat commands. |
| `SlashCommandSuffix` | Optional island name, such as `berry`, for commands like `/order_berry` and `/setup-embed_berry`. Each island still needs its own application and token. |
| `AllowLookup` | Set `true` to enable Find items and suggestions while typing a lookup command. Members can still paste lists or upload files when it is disabled. |
| `RoleUseBot` | Exact name of the role allowed to order. `@everyone` allows all members who meet the other access settings. |
| `Channels` | Allowed channel IDs. Include every channel with a panel. Leave empty to allow any channel. |
| `Users` | Optional list of allowed user IDs. Leave empty to allow any user who meets the other access settings. |
| `Sudo` | User IDs allowed to run setup and admin commands. The application owner also has access. |
| `AcceptingCommands` | Set `false` to pause new orders and queue processing. Members can still check My order and cancel waiting orders. |
| `AllowVillagerInjection` | Enables villager orders. Villagers that cannot be adopted are rejected. |
| `OrderConfig.MaxQueueCount` | Maximum number of waiting orders. If the queue is full, members keep their saved item list and can try again later. |

Order mode requires `LimitedDodoRestoreOnlyMode` and `SkipConsoleBotCreation` to be `false`. Members can edit their item lists while orders are paused.

## Create the panel

1. Start the bot and wait for its Discord ready message.
2. As the application owner or a user in `Sudo`, run `/setup-embed` in the order channel. You can also choose a channel using the command's `channel` option.
3. If you set a suffix, use the matching command, such as `/setup-embed_berry`.
4. Pin the panel and link to it in your server's order instructions.

The panel has **Find items**, **Place order (guided)**, **Place order (quick)**, **My order**, and **Help** buttons. Only the member who clicks a button can see the bot's reply.

For operator controls, run `/setup-control` in a staff channel. This channel does not need to be in `Channels`. See [Control panel](Control-panel) for Sudo permissions, connection status, and start/stop controls.

Running setup again updates that channel's saved panel. You can add panels in several allowed channels. The panel's order availability, waiting count, and current order update about every 30 seconds. The current order is separate from the waiting queue. Posts underneath the panel do not affect updates. Keep `OrderData/panels.json` and the same working folder across restarts so the bot can find its saved message. Run setup again if you delete a panel.

After changing the command suffix, restart the bot and run setup again. Members should reopen the panel because their old private buttons use the previous suffix.

## Review an order

**Place order (guided)** and the slash order commands show a review screen before submitting. Members join the queue when they press **Confirm order** and the bot accepts the order.

**Place order (quick)** opens a command form. Members paste an `order` or `ordercat` command and press Submit to queue it directly. The command selects Standard or Catalogue, and a plain item list defaults to Standard. Quick uses the same item safety, access, DM, duplicate-order, pause, and queue-limit checks. Failed commands are saved for retry separately from the guided item list.

| Mode | Items the member receives |
| --- | --- |
| Standard | Fills 40 pickup slots with available variants, as in the existing order system. The review screen shows the resulting items. |
| Exact | Keeps the chosen items and variants. Other pickup slots stay empty. Files and presets start in this mode. Choosing a variant also selects Exact. |
| Catalogue | Members collect items before the separator and leave extra copies beyond it. At 40 requested items, all slots are requested items and there is no separator or extra filler. The review screen shows only the items to collect. |

All stackable items use full stacks in every mode. This also applies to files, presets, item codes, and saved orders. During lookup, members can press **Quantity** before **Add to order** to add several copies of the selected item. Each copy uses one order slot. Choosing a quantity selects Exact mode and cannot exceed the space left in the 40-slot list.

Members must correct unknown items and remove unsafe items before confirming. Lists over 40 items must be shortened. The bot keeps the list so members can edit it.

## Villager lookup

Set both `AllowLookup` and `AllowVillagerInjection` to `true` to enable **Find villager** inside guided orders. The same control appears on the review screen. Members can search by name or submit a blank search to browse all orderable villagers, ten per page.

The language menu uses the same saved language as item lookup. Villager names support English, Japanese, French, German, Spanish, Italian, Korean, Simplified Chinese, and Traditional Chinese. Members can change languages without changing the selected villager. Name entry in Options and pasted commands also uses the chosen language; English names from older orders remain accepted.

Results exclude special NPCs, restricted villagers, and villagers without available delivery data. The bot checks eligibility and host settings again when adding the villager and confirming the order. Disabling either lookup or villager orders disables the search button. Members can still remove an existing villager selection.

Only one villager can be selected per order. Adding, replacing, or removing it preserves the item list. The selected ID and language are saved in the existing draft and last-order files; no new configuration or Discord intents are required. Villager orders can be placed with or without items. Members need an empty housing plot to adopt the villager.

## Presets and inventory files

Put valid `.nhi` files in `OrderConfig.NHIPresetsDirectory`, or use the Sudo-only `/uploadpreset` command. Members choose them under **Place order (guided) → Presets**. Selecting a preset replaces the current item list and opens the review screen.

To upload an inventory file, members use `/order-nhi` and select the file in its `file` option. It must be a valid `.nhi` file with up to 40 items. The bot does not need permission to read ordinary chat attachments.

## DMs and queue status

Members must allow DMs from members of your server. They can press **Test DMs** on the guided review screen to check. Both Confirm order and Quick submission test DMs before adding an order to the queue. This test message says that the order has not been accepted yet. A separate message confirms whether it was accepted.

**My order** shows whether the member is waiting, preparing to visit, ready to visit, visiting, or finished. It also shows cancelled and failed orders with an explanation. Members can keep checking it after their order leaves the waiting queue. Press **Refresh** to check again.

The Dodo code is sent by DM and shown in that member's private My order reply while the island is ready for them.

Members can cancel a waiting order after confirming the cancellation. They cannot cancel it with this button once it starts. An old Cancel button cannot remove a newer order.

## Saved orders and restarts

The bot needs permission to write to its working directory. Keep `OrderData` when updating:

- `OrderData/panels.json` saves the panel message IDs.
- `OrderData/draft-<guild>-<user>.json` saves each member's item list, options, and any errors they still need to fix.
- `OrderData/Quick/draft-<guild>-<user>.json` saves a failed Quick command for retry, separately from the guided list.
- `OrderData/last-<guild>-<user>.json` saves the last accepted order for **Order again**.

These files are ignored by Git. Panels, item lists, and order history survive restarts. The waiting queue and current order status do not. Tell waiting members to rejoin after a restart. They can review and confirm a guided list or reopen Quick order and submit their saved command.

If a member has no newer saved order, **Order again** can read their old order from `UserOrder`. New order history also saves villager and mode choices.

## Switch from chat commands

- Link members to [How to order with the panel](How-to-order-with-the-panel).
- Replace channel instructions for `$order` messages with a link to the panel.
- Members can paste `$order`, `!order`, `$ordercat`, or a copied slash order into **Place order (quick)** to submit directly, or use **Place order (guided) → Paste / edit list** to review before confirming.
- Mentioning the bot shows the panel buttons and explains where to paste an old command.
- Existing order buttons still work when their suffix matches. Run setup to replace them with the new panel.
- `/order`, `/ordercat`, `/order-nhi`, `/preset`, `/lastorder`, `/lookup`, `/lookup-lang`, `/queue`, and `/remove` use the same ordering screens. Admin and in-island commands remain available.

## Check the setup

Before announcing the update, try these steps with a normal member account on mobile:

Test Quick with `order` and `ordercat`, check the reported mode and queue position, and confirm that invalid input, closed DMs, and a full queue prevent submission. Reopen a failed Quick form and check that its pasted command is kept and the guided list is unchanged.

1. Find lucky cat, choose a variant and quantity, add it, and review the Exact order. Check the number of copies and try a quantity larger than the remaining space.
2. Paste an old order command and check its items and optional villager.
3. Try a misspelled item and a list over 40 items. Check that the bot asks for corrections and keeps the list.
4. Block DMs and try to confirm. Enable DMs, then retry the saved list.
5. Confirm an order, then press the old confirmation button again. Check that only one order was added.
6. Check My order while waiting, preparing, ready, and visiting. Check that only the ordering member can see the Dodo code.
7. Cancel a waiting order, place another, and check that the old Cancel button cannot remove it.
8. With no orders waiting, run setup again and restart the bot. Check that the panel and saved item lists are still available.

Also test **Find villager** with a blank search and a localized name. Change the language, add a villager, replace and remove it, and confirm that items stay saved. Check that the villager and language survive a restart and that turning off villager orders prevents confirmation.

Automated tests cover item parsing, order contents, saved lists, button handling, and queue status. These checks also test your Discord permissions, DMs, and Switch setup.

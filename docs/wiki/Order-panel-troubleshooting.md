# Order panel troubleshooting

## Quick order was not queued

Read the private reply for the reason. Invalid items, unsafe items, closed DMs, a paused or full queue, and an existing order prevent submission. Press **Retry quick order** to reopen the saved command, make any corrections, and submit again. An outdated form cannot submit the same order twice, reopen Quick order to use the latest form.

## A button says the previous action is still finishing

Wait a moment, then press it again. The bot keeps edits for one member in order, and opening a form cannot interrupt an action that is still finishing.

## A saved list or previous order cannot be restored

The bot checks saved files before loading them. If a file is incomplete or damaged, it logs the problem and lets the member create a new list. A damaged previous-order file cannot be loaded with **Order again**. Keep backups of `OrderData` if you need to recover older lists.

## Commands or buttons are missing

Set `CommandMode` to `InteractionCommands`, restart the bot, and wait for Discord ready. Invite the correct application with the `bot` and `applications.commands` scopes. Members need Use Application Commands in the order channel.

If you set `SlashCommandSuffix`, use the matching commands. For example, `berry` gives you `/setup-embed_berry`. Run setup again after changing the suffix. Each island needs its own bot folder, application, and token.

## I cannot run setup

The application owner or a user listed in `Sudo` must run `/setup-embed`. Use Discord user IDs in `Sudo`, not names or role IDs. Run setup in a server text channel or choose one in the command's `channel` option.

The bot needs View Channel, Send Messages, Embed Links, and Read Message History there. If you restrict channels in the configuration, include the panel's channel.

## Find items is disabled or finds nothing

Set `AllowLookup` to `true`. Restart if you edited the configuration file, then run setup again or wait for the panel to refresh. Members can still paste lists and upload files while lookup is disabled.

Search needs at least two characters and uses the language chosen under **Options**. Try a shorter part of the item's name. English is the default. Available language codes are `en`, `jp`, `fr`, `de`, `es`, `it`, `ko`, `chs`, and `cht`.

## Pasted items are not recognized

Separate full names with commas or newlines. Separate hex codes with spaces or newlines. Use **Find items** if you are unsure of a name.

The bot keeps your list and shows errors to fix before confirming. **Paste / edit list** reopens it. **Clear list** removes the list and its errors so you can start again.

## Find villager is disabled or finds nothing

The host needs both `AllowLookup` and `AllowVillagerInjection` enabled. Open **Place order (guided)** to find the villager search button. If either setting is disabled, the button is unavailable; an existing selection can still be removed.

Use the language menu on the results screen, then search again using the villager's name in that language. Single-character queries work, or submit a blank search to browse all orderable villagers. Special NPCs and restricted villagers do not appear because they cannot be delivered for adoption.

Use **Add to order** on the selection screen, then review and confirm your order. Items are optional, and you need an empty housing plot to adopt the villager. Changing the villager or language keeps the item list.

## The review shows more items than I chose

Standard fills 40 slots with available variants. Choose **Exact** to keep your chosen items and variants, then review again. All stackable items use full stacks in every mode.

Catalogue uses the existing separator layout. Extra copies after the separator are not shown in the review because members should leave them on the island.

At 40 requested items, there is no room for a separator or extra filler. The review tells members to collect all items.

## DMs fail when I confirm

Allow DMs from members of this server and check that you have not blocked the bot. Use **Test DMs** after changing your Discord privacy settings. Your item list stays saved if the check fails.

The first DM only checks that the bot can message you. A separate message confirms whether your order was accepted. If that confirmation DM fails, check **My order** before trying again. It still shows an accepted order. Fix DMs so you can receive arrival instructions.

## The queue is full or orders are paused

Your list is saved. Wait for space or for orders to reopen, then review and confirm again.

`AcceptingCommands: false` pauses new orders and queue processing. Members can still check **My order** and cancel waiting orders. An order that has already started can finish.

The bot also cannot accept orders with `SkipConsoleBotCreation` enabled or in dodo restore-only mode.

## An old button or form says my list changed

After you edit your list, buttons from an earlier reply no longer work. Use the latest private reply or reopen the panel.

Review your saved list again after the bot restarts. If you selected an item but had not pressed **Add to order**, search for it again.

## My order left the queue before I finished

**My order** shows Preparing, Ready, or Visiting after your order starts. You can keep checking it there. Cancellation only applies to waiting orders.

The Dodo code appears only in the ordering member's private reply and DM while the island is ready for them. Finished or cancelled orders no longer show the code.

## The panel count is out of date

The panel normally updates within 30 seconds when the waiting count, current order, or order availability changes. Messages posted underneath it do not affect updates: the bot edits the exact message ID saved in `OrderData/panels.json`.

The waiting count excludes the order already being processed. **Current order** shows Preparing island, Waiting for arrival, or Visitor on island separately. If **My order** shows a waiting queue position but the public panel still says zero after 30 seconds, the panel is stale.

Keep `OrderData/panels.json` when updating, and start the bot from the same working folder. A restart reloads the saved channel and message IDs. If the file is missing, restore it from the previous bot folder and restart, or run `/setup-embed` in the order channel to register a panel again. Use your island suffix if configured.

Check the bot's logs for `OrderPanelService`. Missing saved files are reported with their full path. Missing channels, unreadable or deleted panel messages, and failed edits are reported with the saved IDs. The bot retries failed refreshes, including channels missing from Discord's local cache. Give it View Channel and Read Message History permission, and check that the panel belongs to this bot application. Run setup again if the panel was deleted.

**My order** checks the current status as soon as you press it, without waiting for the panel update.

## Orders disappeared after a restart

Waiting orders and current order status are cleared when the bot stops. Panels, item lists, and last accepted orders are saved. Announce planned restarts, let the current order finish, and ask waiting members to rejoin afterward.

Keep `OrderData`, `UserOrder`, configuration, presets, and anchors when updating. Use the same working directory so the bot can find them. **Order again** opens the last accepted order for review.

## A file or preset will not load

Use a valid `.nhi` inventory file containing up to 40 items. Each item uses 8 bytes, so the file must be a positive multiple of 8 bytes and no larger than 320 bytes. Renaming another file to `.nhi` will not work.

Upload files through `/order-nhi`'s `file` option. Put host presets in `OrderConfig.NHIPresetsDirectory` and choose them from **Presets**. The host must fix any invalid preset files.

## Console connection, anchors, or crashes

Use the existing [Discord bot setup](https://github.com/berichan/SysBot.ACNHOrders/wiki/Discord-bot-setup) and [FAQ / Troubleshooting](https://github.com/berichan/SysBot.ACNHOrders/wiki/FAQ---Troubleshooting) guides for console and game problems. **My order** shows failed or cancelled orders when the bot reports them. Check the host's logs for more details.

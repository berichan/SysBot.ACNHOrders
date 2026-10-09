# How to order with the panel

Open your server's pinned island order panel. Its buttons let you order without memorizing commands or item codes.

Choose **Place order (guided)** to build and review your list, or **Place order (quick)** to paste a command and join the queue on Submit.

## Quick order

1. Enable DMs from members of this server.
2. Press **Place order (quick)**.
3. Paste your command, then press **Submit**. The bot reads the command and queues the order immediately if it passes the checks.

```text
$order 0083 3107
```

`order` selects Standard. Use `ordercat` for Catalogue:

```text
$ordercat lucky cat, nook miles ticket villager:cat23
```

Plain item lists use Standard. The form also accepts your host's command prefix and copied slash commands such as `/ordercat_berry items:0083 3107`. All stackable items use full stacks.

Quick skips the review and Confirm order steps. If the list is invalid, orders are paused, the queue is full, or DMs fail, no new order is queued. The bot keeps the pasted command so you can press **Retry quick order**, edit it, and submit again. Your guided item list stays saved separately.

## Find items

1. Press **Find items** and enter part of an item's name, such as `lucky cat`.
2. Choose an item from the results. Use **Next** and **Previous** to see more results.
3. Choose a variant if available. Press **Quantity** to choose how many copies to add, from 1 to the number of remaining order slots. Each stackable copy is a full stack.
4. Press **Add to order**.
5. Find more items, or press **Review order**.

Only you can see your item list, and the bot saves it as you edit. Adding items does not join the queue.

## Use a list, preset, or file

For a guided order, press **Place order (guided) → Paste / edit list** to paste item names, codes, or an old order command:

```text
lucky cat, nook miles ticket
```

```text
0083 3107
```

```text
$order lucky cat, nook miles ticket villager:cat23
```

Separate full names with commas or newlines. Separate hex codes with spaces or newlines. If the bot cannot find an item, edit its name or use **Find items**. Fix any errors before confirming.

When you edit items selected through search, a preset, or a file, the form shows item codes to preserve their variants. You can keep those codes, delete lines, or replace them with item names. If a pasted list has errors, correct it before adding or removing items through the other controls.

Choose a host preset under **Place order (guided) → Presets**. It replaces your current item list and opens the review screen.

For an `.nhi` inventory file, use `/order-nhi` and choose the file in its `file` option. If your server uses an island suffix, use the matching command, such as `/order-nhi_berry`. You can review the items before confirming.

## Review and confirm

The review screen shows what you will receive. All stackable items use full stacks in every mode, including items from files, presets, codes, and saved orders. Choose a mode:

- **Standard:** fill 40 pickup slots with available variants.
- **Exact:** keep your chosen items and variants. Leave other slots empty.
- **Catalogue:** collect the items before the separator. Leave the extra copies beyond it. If you request all 40 slots, collect all items, there is no separator or extra filler.

Choosing a variant or quantity selects Exact, which keeps the chosen variants and number of copies. Files and presets also start in Exact. You can change the mode on the review screen. The quantity controls how many order slots to add; stackable items always use full stacks.

Press **Keep editing** to change your list. **Options** lets you choose the language for item names or request a villager by name or ID. The host must allow villager orders, and the villager must be adoptable. You need an empty housing plot to adopt one.

Allow direct messages from members of this Discord server. You can press **Test DMs** to check that the bot can message you.

Press **Confirm order** when the items are correct. You join the queue after the bot confirms that your order was accepted. If orders are paused, the queue is full, or DMs fail, your list stays saved. Review it and try again when the problem is resolved.

## Check your order

Press **My order** on the panel, or use `/queue`. Press **Refresh** to check again.

- **Waiting:** shows your queue position and an approximate wait.
- **Preparing:** empty your inventory, talk to Orville, and wait at the Dodo code entry screen.
- **Ready:** use the private arrival instructions promptly. The bot also sends your Dodo code by DM.
- **Visiting:** collect your items and leave through the airport before time runs out.
- **Completed, cancelled, or failed:** shows what happened and any explanation.

Your order leaves the waiting queue when preparation starts. You can still follow it through **My order**.

## Cancel or order again

While waiting, press **My order → Cancel order**, then confirm. You will lose your queue position. This button cannot cancel an order that has started.

**Order again** loads your last accepted order for you to review and edit. Press **Confirm order** when ready. `/lastorder` opens the same screen.

If an old button says your item list changed, use the latest reply or reopen the panel. You can also reopen the panel if Discord stops showing an older private reply. Your item list is saved.

A bot restart clears waiting orders. Follow your host's instructions about joining the queue again.

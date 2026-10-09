# Wiki guide

These pages are ready to copy into the [project wiki](https://github.com/berichan/SysBot.ACNHOrders/wiki) when you install this build. Keep the existing console, anchor, Twitch, and dodo-restoration guides.

| New page title | Source file |
| --- | --- |
| Order panel setup | [Order-panel-setup.md](Order-panel-setup.md) |
| How to order with the panel | [How-to-order-with-the-panel.md](How-to-order-with-the-panel.md) |
| Order panel troubleshooting | [Order-panel-troubleshooting.md](Order-panel-troubleshooting.md) |
| Control panel | [Control-panel.md](Control-panel.md) |

Create each wiki page with the title above and copy in the matching file's contents. These titles keep the links between pages working. This README is for publishing the pages and does not need its own wiki page.

Add this section to the wiki Home page:

```markdown
## Discord order panel

- [Order panel setup](Order-panel-setup)
- [How to order with the panel](How-to-order-with-the-panel)
- [Order panel troubleshooting](Order-panel-troubleshooting)
- [Sudo control panel](Control-panel)

The Discord bot setup page covers the console connection, map coordinates,
and five anchors. Order panel setup covers Discord permissions and
configuration for low-intent mode.
```

Add this note near the top of **Discord bot setup**:

```markdown
For the order panel and low-intent slash commands, follow
[Order panel setup](Order-panel-setup) for Discord permissions,
configuration, and panel creation. Use this page for the console
connection, map position, and anchors.
```

Add this note near the top of **How to order**:

```markdown
If your server uses the order panel or slash commands, follow
[How to order with the panel](How-to-order-with-the-panel).
The chat-command examples below apply to TextCommands mode.
Use Place order (quick) to paste an old command and submit directly,
or Place order (guided) to review your list before confirming.
```

These pages are local files. They have not been published to the wiki.

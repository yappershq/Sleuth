# Sleuth

Stealth-admin toggle for ModSharp (CS2). An admin with the `sleuth:stealth` permission can drop
into "covert mode" and become as invisible as the engine permits — no chat tag, no join/leave
spam, no team-change announcement, and removed from the in-game scoreboard.

Toggle in chat: `!sleuth` (toggle), `!sleuth on`, `!sleuth off`. Requires the `sleuth:stealth`
admin permission (registered via AdminManager `MountAdminManifest`).

## What covert mode hides

When a slot enters covert mode, Sleuth:

1. **Chat tag** — hides the player's HexTag (via `IHexTagsShared.SetHidden`), if HexTags is loaded.
2. **Join / leave announcements** — silences the player's connection messages (via
   `IConnectionMessagesShared.SetSilent`), if ConnectionMessages is loaded and
   `sleuth_silence_announcements` is on.
3. **Team-change announcement** — suppresses the `"X joined Spectators / Counter-Terrorists"`
   broadcast that would otherwise fire when the admin is moved off their team. This is done by
   hooking the `player_team` game event (`Sharp.Extensions.GameEventManager`) and, **only for
   slots currently in covert mode**, setting the event's `Silent` flag and returning
   `ChangeParamReturnDefault` with `serverOnly = true` — the engine still applies the team change
   server-side, but clients never see the announcement. Team changes for everyone else are left
   completely untouched.
4. **Scoreboard** — moves the controller to team `UnAssigned (0)` (configurable via `sleuth_team`).
   `UnAssigned` drops the player off **all three** rendered scoreboard sections (CT / T /
   Spectators). `Spectator (1)` only removes them from the playing teams — they would still appear
   under the "Spectators" header — so `UnAssigned` is the default and hides better.

On exit (or disconnect / plugin unload) every one of the above is restored: tag visibility,
announcements, and (if `sleuth_restore_team` is on) the original team.

## Known limitation — the `status` console list is NOT hidden

The native `status` console command still shows the covert admin's slot, name, SteamID, and ping.
This is **not** faked or worked around, because it is not feasible to do cleanly:

- ModSharp's only interception point is `IHookManager.PrintStatus`, which fires **once per
  `status` invocation** and only tells you **who ran it** (the requesting client, or null for
  server console / RCON).
- The supported actions are `Ignored` or `SkipCallReturnOverride` — i.e. **all-or-nothing for the
  whole table**. There is no per-row callback and no string buffer to edit, so you cannot
  surgically remove a single player's row while leaving the rest of the table intact.
- The only thing `PrintStatus` could do is suppress the **entire** `status` output for a requester,
  which would break a legitimate command for normal players (and admins/RCON would still see the
  full table anyway).

So: covert mode hides the **chat tag**, the **team-change announcement**, **join/leave messages**,
and removes the admin **from the in-game team scoreboard** (via `UnAssigned`) — but the
`status` console list (visible via server console / RCON) **still shows the covert slot**. This is
a CS2 / ModSharp limitation, documented here rather than papered over.

## ConVars

| ConVar | Default | Description |
|---|---|---|
| `sleuth_enabled` | `1` | Enable the plugin. |
| `sleuth_team` | `0` | Team on entering covert mode: `0` = UnAssigned (hides from scoreboard), `1` = Spectator. |
| `sleuth_restore_team` | `1` | Restore the original team when leaving covert mode. |
| `sleuth_silence_announcements` | `1` | Suppress join/leave chat announcements while covert. |

## Dependencies

- **AdminManager** (required) — command registration + permission.
- **GameEventManager** extension (required, bundled) — `player_team` hook. Ships as
  `Sharp.Extensions.GameEventManager.dll` alongside `Sleuth.dll` (the server core does not provide
  this assembly).
- **HexTags** (optional) — chat-tag hiding.
- **ConnectionMessages** (optional) — join/leave announcement silencing.

Optional dependencies degrade gracefully: if absent, the corresponding covert feature is simply
skipped and a warning is logged.

## Build

```bash
cd /home/claude/Sleuth
dotnet build *.slnx -c Release --nologo -clp:ErrorsOnly
```

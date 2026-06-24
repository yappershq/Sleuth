<div align="center">
  <h1><strong>Sleuth</strong></h1>
  <p>Stealth-admin toggle for ModSharp (CS2) — go covert so cheaters can't spot staff.</p>
</div>

<p align="center">
  <img src="https://img.shields.io/github/stars/yappershq/Sleuth?style=flat&logo=github" alt="Stars">
</p>

---

Sleuth lets an admin drop into "covert mode" and become as invisible as the engine permits: no chat tag, no join/leave spam, no team-change announcement, and removed from the in-game scoreboard. It degrades gracefully — optional integrations are skipped (with a warning) when the plugin they depend on isn't installed.

## 🚀 Install

Copy the build output into your ModSharp install (`<sharp>` = your `sharp` directory):

| From | To |
|------|----|
| `.build/modules/Sleuth/` | `<sharp>/modules/Sleuth/` |
| `.build/locales/sleuth.json` | `<sharp>/locales/sleuth.json` |

The `modules/Sleuth/` directory ships both `Sleuth.dll` and the bundled `Sharp.Extensions.GameEventManager.dll` (the server core does not provide that assembly). `configs/sleuth.cfg` is auto-generated on first run. Restart the server (or change map) to load.

**Requires** AdminManager (ships with ModSharp). Optional: [HexTags](https://github.com/yappershq), ConnectionMessages, and AdminPanel — each unlocks an extra covert feature when present.

## ⌨️ Commands

| Command | Description | Permission |
|---------|-------------|------------|
| `!sleuth` | Toggle covert mode on/off for yourself. | `sleuth:stealth` |

If AdminPanel is installed, the same self-toggle is also exposed as a **Sleuth: toggle stealth** entry in the in-game admin menu (gated on `sleuth:stealth`).

## ⚙️ Configuration

`configs/sleuth.cfg` (auto-generated on first run):

| ConVar | Default | Meaning |
|--------|---------|---------|
| `sleuth_enabled` | `1` | Enable the plugin. |
| `sleuth_team` | `0` | Team on entering covert mode: `0` = UnAssigned (hides from scoreboard), `1` = Spectator. |
| `sleuth_restore_team` | `1` | Drop back to Spectator when leaving covert mode so the player can re-pick a team. |
| `sleuth_silence_announcements` | `1` | Suppress join/leave chat announcements while covert. |

## 🔧 How it works

When a slot enters covert mode, Sleuth hides the player's HexTag (if HexTags is loaded), silences their join/leave announcements (if ConnectionMessages is loaded and `sleuth_silence_announcements` is on), suppresses the team-change broadcast by hooking the `player_team` game event for covert slots only, and moves the controller to `UnAssigned` so it drops off all three scoreboard sections (CT / T / Spectators). On exit, disconnect, or plugin unload, every change is restored.

**Known limitation:** the native `status` console list still shows the covert slot. ModSharp's `PrintStatus` hook is all-or-nothing per invocation with no per-row callback, so Sleuth reconstructs a filtered listing for the requesting client where it can, but real ping/loss are not exposed by the public API and the table cannot be surgically edited row-by-row.

## 📦 Build

```bash
dotnet build -c Release
```

Outputs `.build/modules/Sleuth/Sleuth.dll` (plus the bundled `Sharp.Extensions.GameEventManager.dll`), and copies `.assets/locales/sleuth.json` to `.build/locales/`.

---

<div align="center">
  <p>Made with ❤️ by <a href="https://github.com/yappershq">yappershq</a></p>
  <p>⭐ Star this repo if you find it useful!</p>
</div>

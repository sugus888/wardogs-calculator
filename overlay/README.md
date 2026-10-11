# WARDOGS Map Overlay

A small Electron desktop app that shows the official
[WARDOGS Artillery Calculator](https://wardogs-artillery.com/) in a frameless,
always-on-top window on top of the game.

The overlay is a window around the official site, nothing more:

- It loads `https://wardogs-artillery.com/` unmodified, in its own sandboxed
  view. Maps, saved targets, lobbies and settings all behave exactly as in a
  browser.
- It does **not** read game memory, inject into the game, hook DirectX, or
  capture the screen. Coordinates are entered the same way as on the website.
- It does not bundle, cache, proxy or re-host map tiles or Terrain3D data.
  See [CDN & hosted assets](../docs/cdn.md).

## Requirements

- Windows 10/11 (macOS works too; Linux works without click-through focus handling)
- Node.js 22+
- The game must run in **Borderless** or **Windowed** mode. A normal desktop
  window can't be drawn over exclusive fullscreen.

## Run

```bash
cd overlay
npm install
npm start
```

Build a portable Windows `.exe` (output goes to `overlay/release/`):

```bash
npm run dist:win
```

## Controls

| Hotkey | Action |
|---|---|
| `Ctrl + Shift + O` | Show or hide the overlay |
| `Ctrl + Shift + C` | Toggle click-through (the mouse passes through to the game) |
| `Ctrl + Shift + ↑` / `↓` | Change opacity in 10% steps (20–100%) |
| `F5` | Reload the map (while the overlay has focus) |

Hotkeys are system-wide, so they work while the game has focus. If another
program already uses one of them, the overlay prints a warning and the tray
menu still works.

The toolbar at the top of the window has:

- the drag area (move the window by dragging the toolbar)
- Back and Reload
- the **Compact / Desktop** layout switch. Compact uses the map-first
  `/mobile/` interface and suits a small overlay. Desktop is the full interface.
- an opacity slider and the click-through toggle
- Hide and Quit

Resize the window from its edges. The tray icon has the same options plus
**Reset position**. Window position, size, opacity and layout are saved
between sessions.

While click-through is on, the overlay ignores the mouse completely, toolbar
included. Turn it off with `Ctrl + Shift + C` or from the tray icon.

## Local development

To check overlay changes against a local build of this repository instead of
the live site:

```bash
# repository root
npm run build
cd dist && python -m http.server 8000

# second terminal
cd overlay
npm run start:local
```

`--url` accepts only the official site or an `http://localhost` /
`127.0.0.1` server. Protected maps don't load in a local build, same as in a
browser (see the main README).

## Security notes

- Both views run with `sandbox`, `contextIsolation` and no Node integration.
- The site view can only navigate within the site's own origin. Other links
  (Ko-fi, GitHub, Discord, ...) open in the default browser.
- The site only gets clipboard (for coordinate copy and paste) and fullscreen
  permissions. All other permission requests are denied.
- The toolbar talks to the main process over a small allow-listed IPC
  channel, and messages from any other sender are ignored.

## Tests

```bash
npm test
```

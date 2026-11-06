# Changelog

## [1.1.0] - 2026-11-05

- **Lighting per app.** Each game can set the mouse's lighting: a solid color, or off. The desktop can have its own color, or leave lighting to the mouse. Pick colors in Settings (presets or any color), and they preview on the mouse right away.
- Settings file lines can carry a color: `cs2.exe = 800, #FF0000` (or `off`). Inline comments now start with `;` only.
- `litedpi-probe features` lists the mouse's HID++ features and lighting zones; `litedpi-probe color FF0000|off|device` sets the lighting once.
- MIT license.
- Release builds can be code-signed through SignPath Foundation (see `docs/SIGNING.md`).

## [1.0.0] - 2026-11-05

First release.

- Per-game DPI: switches speed when a listed game's window comes to the front, desktop speed otherwise.
- Rules match an exe name (`cs2.exe`) or a whole folder (`C:\Games\Apex\`).
- Tray icon shows the current DPI; colour shows desktop, game or paused.
- Settings window: desktop speed, game list, "Add a running program", "Try it", start with Windows.
- Re-applies after unplug/replug and wake from sleep.
- Puts the mouse in onboard-memory mode when G HUB isn't running so its buttons keep working.
- Warns when G HUB is running.
- `litedpi-probe.exe` command-line check.
- Installer (`install.ps1`) and uninstaller, with a Settings > Apps entry.

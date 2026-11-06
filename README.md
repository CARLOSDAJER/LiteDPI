# LiteDPI

A tiny Windows tray app that sets your Logitech mouse's DPI per game, without G HUB.

G HUB's per-game profiles often don't kick in, so you end up at desktop speed in the middle of a match and have to restart G HUB. LiteDPI watches which window is in front and sets the mouse's DPI straight away. It's a single 40 KB exe with nothing to install alongside it.

- **Per-game DPI.** Each game gets its own speed; everything else gets your desktop speed.
- **Per-game lighting.** Each game can also set the mouse's color, or turn the lights off.
- **Instant.** It switches the moment a game window comes to the front.
- **Recovers on its own.** It re-applies the speed after the mouse is unplugged or the PC wakes from sleep.
- **Your mouse buttons still work.** The DPI buttons use the speeds stored in the mouse; LiteDPI only sets the speed again when you switch programs.
- **Doesn't touch the mouse's memory.** It only changes the live DPI setting, never its stored profiles.

## Install

Open PowerShell and run:

```powershell
irm https://raw.githubusercontent.com/CARLOSDAJER/LiteDPI/main/install.ps1 | iex
```

This installs to `%LOCALAPPDATA%\Programs\LiteDPI` (no admin needed), adds a Start menu shortcut, turns on start with Windows, and starts LiteDPI.

You can also install by hand: download `LiteDPI-vX.Y.Z.zip` from [Releases](https://github.com/CARLOSDAJER/LiteDPI/releases), unzip it, then right-click `install.ps1` and choose **Run with PowerShell**. Or just run `LiteDPI.exe` straight from the folder.

> **Quit G HUB first.** If both run, G HUB keeps overriding the DPI. Right-click G HUB's tray icon and choose Quit, then in G HUB's settings untick "Start on system startup".

## Use

A small coloured square appears in the system tray, showing the current DPI (for example **2.7** = 2700):

| Colour | Meaning |
|--------|---------|
| Blue   | Desktop speed |
| Green  | A game's speed is active |
| Grey   | Paused, or mouse not found |

Double-click the icon (or right-click it and choose **Settings**) to set your desktop speed and add games. **Add a running program...** lists open windows, so start the game first. Use **Try it** to feel a speed before you save. Right-click the icon for **Pause switching** and **Start with Windows**.

### Lighting

Click a game's **Lighting** cell in Settings to choose a preset color, **Off**, or any color. The mouse shows your pick right away so you can see it. A game with no color uses the desktop lighting. If the desktop is set to **Mouse's own**, LiteDPI leaves the lighting alone and the mouse uses its built-in effect. Colors apply to every lighting zone, which on the G502 HERO means the logo and the side strip. Nothing is saved to the mouse.

### Settings file

Settings live in `LiteDPI.ini` next to the exe. You can edit it by hand; changes apply instantly.

```ini
desktop = 1600                                     ; no color: the mouse keeps its own lighting
C:\Program Files\EA Games\Apex\ = 3000, #FF0000   ; any exe inside this folder, red
cs2.exe = 800, off                                 ; or just an exe name, lights off
```

## Supported mice

LiteDPI works with Logitech mice that use HID++ 2.0 and support adjustable DPI. That covers most G-series mice from the last several years, wired and LIGHTSPEED. It's tested on a wired **G502 HERO**.

To check your mouse, run `litedpi-probe.exe` from the install folder. It prints the mouse name, current DPI and supported range. `litedpi-probe set 1600` sets the DPI once, and `litedpi-probe features` lists what the mouse supports, including its lighting zones.

Lighting needs HID++ feature `0x8070` (Color LED Effects), which the G502 HERO and many G-series mice have. Mice with only the newer RGB feature (`0x8071`) still get per-game DPI, but LiteDPI doesn't change their lighting yet.

## Uninstall

Go to **Settings > Apps > Installed apps > LiteDPI > Uninstall**, or run `uninstall.ps1` from the install folder.

## Build from source

No SDK needed. Windows 10 and 11 include the C# compiler (.NET Framework 4.x).

```bat
build.cmd
```

This produces `LiteDPI.exe` and `litedpi-probe.exe`. To install your own build, run `install.ps1` from the repo folder.

### Releasing a new version

1. Bump `Version` in `src/AssemblyInfo.cs`.
2. Add an entry at the top of `CHANGELOG.md`.
3. Commit, then tag and push:
   ```
   git tag v1.1.0
   git push origin main v1.1.0
   ```

GitHub Actions builds the zip and publishes the release. The install one-liner always fetches the latest release.

## How it works

Logitech mice speak a protocol called HID++. LiteDPI talks to the mouse through the standard Windows HID API. It looks up the "Adjustable DPI" feature (`0x2201`) and sets the sensor DPI. For lighting, it takes software control of the "Color LED Effects" feature (`0x8070`) and sets a static effect on each zone; it hands control back when no color is wanted. When G HUB isn't running, LiteDPI also switches the mouse to onboard-memory mode (`0x8100`), so its buttons work without software. That switch isn't saved to the mouse and resets when you unplug it.

The protocol details come from the open-source projects [Solaar](https://github.com/pwr-Solaar/Solaar) and [libratbag](https://github.com/libratbag/libratbag). No Logitech software was decompiled.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- Committers and reviewers: [CARLOSDAJER](https://github.com/CARLOSDAJER)
- Approvers: [CARLOSDAJER](https://github.com/CARLOSDAJER)

Release builds are made from this repository by GitHub Actions on GitHub-hosted runners. Only tagged releases (`v*`) are signed, and every signing request is approved by hand.

**Privacy policy:** This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. LiteDPI itself makes no network connections. The installer contacts GitHub only to download the release you asked it to install.

## Notes

- Signed releases show **SignPath Foundation** as the publisher. Windows SmartScreen may still warn about a new release until it builds up download history. If it does, choose **More info > Run anyway**, or build LiteDPI yourself from source.
- LiteDPI isn't affiliated with or endorsed by Logitech.

## License

[MIT](LICENSE)

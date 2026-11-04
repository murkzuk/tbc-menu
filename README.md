# TBC Menu

MelonLoader mod for **Tank Battle Classic**. Adds an in-game settings menu (F7) for things the
game does not expose: real screen resolution, the internal render resolution, the post-process
filter, URP quality settings, and optional gameplay/AI tuning.

Unofficial. Contains no game content. Buy the game:
https://store.steampowered.com/app/5086560/Tank_Battle_Classic/

Author: **murkzuk** - https://github.com/murkzuk

![menu](docs/screenshot-menu.png)

## Why this exists

The game ships locked to your desktop resolution and renders the 3D view into a fixed
854x480 point-filtered buffer, then stretches it to the window over a post-process stack of
film grain 0.5, chromatic aberration 0.15 and vignette 0.2. That combination is what makes it
look soft and noisy. None of it is exposed in the game's Settings menu.

This mod makes all of it adjustable at runtime. The background and the exact numbers are in
[docs/graphics-notes.md](docs/graphics-notes.md).

## Requirements

- **MelonLoader 0.7.3** (IL2CPP build). BepInEx does **not** work on this game - see the notes.
- Windows x64.

## Install

1. Install MelonLoader into the game (the installer picks IL2CPP automatically):
   https://github.com/LavaGang/MelonLoader/releases
2. Launch once so MelonLoader generates its Il2Cpp assemblies, then close the game.
3. Drop `TBC_Menu.dll` into `<game>\Mods\`
4. Launch. Press **F7** in game.

Config lives at `<game>\UserData\TBC_Menu.json` and is written by **Save settings** in the menu.
Nothing is written unless you press it.

## Uninstall

Delete `Mods\TBC_Menu.dll` and `UserData\TBC_Menu.json`. To remove MelonLoader entirely,
delete `version.dll`, `MelonLoader\`, `Mods\` and `UserData\` from the game folder; Steam's
"verify integrity" does the same.

## The menu

`F7` opens it. Rows: **left half of the value area decreases, right half increases**. ON/OFF rows
toggle, choice rows cycle. The cursor is unlocked while it is open and the gun is muted, so
clicking cannot fire.

| Tab | What it controls |
|---|---|
| **Resolution** | window size, fullscreen mode, internal render scale (0 = keep the stock 854x480 buffer), live buffer readout |
| **Visuals** | film grain, chromatic aberration, vignette, fog density, antialiasing (off/fxaa/smaa/taa), dithering, texture aniso |
| **Quality** | MSAA 1-8x, main light shadowmap 1024-4096, soft shadow quality, shadow distance, render scale (supersampling above 1) |
| **Effects** | tonemapping (none/neutral/ACES), bloom + threshold, exposure, contrast, saturation |
| **Tuning** | tuning multiplier scales, max tuning levels, AI preset and scales, AI passive, unlock all tanks, points |

## Defaults

Resolution and the visual cleanup are on; quality is raised (MSAA 4x, 2048 shadowmap, high soft
shadows, 200 m shadow distance); effects and tuning are **off**.

## Compatibility

Built and tested against:

- game version **1.2.0**
- Unity **6000.2.10f1**
- `global-metadata.dat` sha256 `510415579aab976aca8135b23a69640dfa7ebb00f5610fb15ff02e7568b925d6`

The mod reads IL2CPP field names through the generated interop assemblies, so a game update can
break it. If the fields move, rebuild against the new interop assemblies.

## Known limitations

- Unity stripped the IMGUI module from this build, so the UI is uGUI with manual hit-testing.
  It is not resizable or scrollable; it is a panel with fixed rows.
- Effects are added to the scene's volume profile at runtime. If the game ever ships its own
  Bloom/Tonemapping components, the mod will use those instead of creating new ones.
- The **Tuning** tab writes into live player data, which the game saves on exit. Back up
  `%USERPROFILE%\AppData\LocalLow\Chobi Softwear\Tank Battle Classic\save.json` before using
  *Unlock all tanks* or *Points*.

## Building

Requires the .NET 6 SDK (or newer) and an installed copy of the game with MelonLoader.

    powershell -ExecutionPolicy Bypass -File tools/find-game.ps1
    powershell -ExecutionPolicy Bypass -File build.ps1

`tools/find-game.ps1` writes `game.path` (gitignored). You can also set the `TBC_GAME_DIR`
environment variable, create a `Local.props` with `<GameDir>...</GameDir>`, or pass
`-p:GameDir="D:\...\Tank Battle Classic"`.

`build.ps1` produces `dist\TBC_Menu-<version>.zip` containing the DLL, README and licence.

Source layout: `MenuMod.cs` (lifecycle and state), `Config.cs` (JSON shape and IO),
`Apply.cs` (everything that mutates the game), `MenuUi.cs` (the uGUI menu).

## Licence

MIT - see [LICENSE](LICENSE).

Not affiliated with Chobi Softwear. No game assets are included or redistributed.

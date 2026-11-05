# TBC Menu

MelonLoader mod for **Tank Battle Classic**. Adds an in-game settings menu (F7) for things the
game does not expose: real screen resolution, the internal render resolution, the post-process
filter, URP quality settings, and optional gameplay/AI tuning.

Unofficial. Contains no game content. Buy the game:
https://store.steampowered.com/app/5086560/Tank_Battle_Classic/

Author: **murkzuk** - https://github.com/murkzuk

**Built by DeepSeek** - an AI agent wrote the code, did the reverse engineering behind
[docs/graphics-notes.md](docs/graphics-notes.md) and wrote this documentation, at murkzuk's
request. murkzuk asked for it, directed the work, tested it, and owns the result.

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
| **Resolution** | window size, fullscreen mode, internal render scale (0 = keep the stock 854x480 buffer: **leave it there**, see the aiming note), live buffer readout |
| **Visuals** | film grain, chromatic aberration, vignette, fog density, antialiasing (off/fxaa/smaa/taa), dithering, texture aniso, **commander view** |
| **Quality** | MSAA 1-8x, main light shadowmap 1024-4096, soft shadow quality, shadow distance, render scale (supersampling above 1) |
| **Effects** | tonemapping (none/neutral/ACES), bloom + threshold, exposure, contrast, saturation |
| **Vehicle** | per-tank top speed, turret traverse, gun elevation, acceleration and torque, plus a realism table and a live speed readout |
| **Tuning** | tuning multiplier scales, max tuning levels, AI preset and scales, AI passive, unlock all tanks, points |

## Commander view (F6)

The game only has third person, the gun sight and the map. **F6** adds a first-person seat on the
turret.

The camera keeps its original parent — it must **not** be parented to the turret. This game slaves
the turret to the camera's forward direction (`Aiming_Control_CS`), so a turret-parented camera
creates a feedback loop and the turret spins endlessly (fixed in 1.2.1). Instead the observer point
is placed on the turret and the camera's world pose is driven directly every frame, with
`Camera_Pivot_CS`, `Camera_Rotation_CS`, `Camera_Avoid_Obstacle_CS` and `Camera_Zoom_CS` suspended.

![commander view](docs/screenshot-commander.jpg)

| Setting | Meaning |
|---|---|
| `commander_height` / `commander_forward` | eye offset from the turret origin, in metres |
| `commander_fov` | field of view (30-110) |
| `commander_free_look` | `false` = the view rides the turret and the mouse trains the gun as usual. `true` = the mouse looks around while the gun stays put (`Aiming_Control_CS` is suspended; firing still works) |
| `commander_sensitivity` | free-look sensitivity |

Notes: the tank's own body is what you are sitting on, so you see the deck and barrel in frame.

The seat is a *request* rather than a one-shot switch, so it survives the game taking the camera
away:

- Pressing `F6` **while the gun sight (or the map) is up** leaves that view and takes the seat -
  the manager's camera mode is reset instead of the seat giving up.
- Switching to the gun sight yourself while seated hands the camera over and the seat **comes back
  by itself** when you leave the gun sight. The same happens on `Restart` and on scene changes, so
  the seat follows you into the next mission.
- `F6` pressed while a scene is still loading waits for the tank to appear (up to a few seconds)
  instead of failing on that frame.
- `camera.commander_view` = `true` enters the seat automatically in the next mission.

## Vehicle overrides

The game quantises every vehicle into two speed brackets (35 or 40 km/h) and models turret
traverse several times too fast, so a Tiger I and a T-34/76 end up 5 km/h apart and both turrets
snap around like a tank in an arcade game. This tab replaces those numbers per tank, in real units:

    "vehicle": {
      "enabled": true,
      "player_only": false,
      "per_tank": {
        "T-34/76": { "max_speed": 14.7, "traverse": 5.0, "accel_time": 24.0 },
        "Tiger I": { "max_speed": 12.7, "traverse": 8.0, "accel_time": 26.0 }
      }
    }

| Key | Unit | Meaning |
|---|---|---|
| `max_speed` | m/s | top speed (14.7 = 53 km/h) |
| `traverse` | deg/s | turret traverse |
| `elevation` | deg/s | gun elevation |
| `accel_time` | s | standstill to full speed |
| `decel_time` | s | full speed to standstill |
| `torque_scale` | x | multiplies the game's wheel torque (slopes only, see below) |

Entries are keyed by `Tank_Prop_SO.Tank_Name` ("T-34/76", "Tiger I"); the **Tank** row in the menu
cycles through the tanks in the current mission and edits the selected one. **Load realism table**
fills in the six tanks that were measured; **Dump vehicle values** writes every live value,
including the flags, to `UserData\TBC_Vehicles.json`.

Two things worth knowing before you tune:

- **Torque is not the speed or acceleration lever.** Measured in game, 2100 and 3000 torque
  produced byte-identical acceleration: the wheels are already at their angular-velocity cap and
  the hull is traction-limited. Top speed is `maxAngVelocity * wheel radius`, and that cap is a
  private field the wheel parent computes once from `Max_Speed` - which is why the mod re-runs it.
- **The ramp is the lever.** `Acceleration_Time` only takes effect when `Acceleration_Flag` is on,
  `Sync_Speed_Rate` is off, and the cached `acceleRate` is refreshed; the mod sets all three when
  `accel_time` is given. That is what stops every tank accelerating like a go-kart.
  Note it disables the game's speed synchronisation for that tank, which has not been tested on
  hills.

The full reverse-engineering write-up, with field offsets and the measured curves, is in
[docs/vehicle-model.md](docs/vehicle-model.md).

## Defaults

Resolution and the visual cleanup are on; quality is raised (MSAA 4x, 2048 shadowmap, high soft
shadows, 200 m shadow distance); effects, tuning and vehicle overrides are **off**.

## Compatibility

Built and tested against:

- game version **1.2.0**
- Unity **6000.2.10f1**
- `global-metadata.dat` sha256 `510415579aab976aca8135b23a69640dfa7ebb00f5610fb15ff02e7568b925d6`

The mod reads IL2CPP field names through the generated interop assemblies, so a game update can
break it. If the fields move, rebuild against the new interop assemblies.

## Known limitations

See [TODO.md](TODO.md) for the backlog and the measured game-vs-real-life data.

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
`Apply.cs` (visuals, quality, effects, tuning), `Vehicle.cs` (per-tank vehicle overrides),
`Camera.cs` (commander view), `MenuUi.cs` (the uGUI menu).

## Credits

- **Built by DeepSeek** (AI), at the request of **murkzuk**. The mod, the reverse-engineering
  notes and this README were produced by the model in a single session; every step was driven
  by murkzuk's requests and validated by running the game.
- **Requested, directed and tested by murkzuk** (https://github.com/murkzuk), who owns the
  result and chose to keep the repository private.
- Thanks to **fierman** for
  [TankBattleMissionEditor](https://github.com/fiermanb/TankBattleMissionEditor), whose approach
  to regenerating the game's stripped type trees is described in the notes.

## Licence

MIT - see [LICENSE](LICENSE).

Not affiliated with Chobi Softwear. No game assets are included or redistributed.
## Aiming note (important)

The game does not aim from the reticle's pixels: `Aiming_Control_CS` casts the shot with
`Camera.main.ScreenPointToRay` and keeps the crosshair lined up by letterboxing the camera
(`Fixed_Aspect_Camera_CS`, which recomputes the camera rect and the reticle's transform scale from
`Screen.width/height`).

**Resizing the game's 854x480 render buffer breaks that mapping.** The reticle and the ray part
company: the gun trails left and down from the crosshair, worse the further from centre you aim.
It looks like a turret or ballistics bug and is neither.

So `resolution.internal_scale` defaults to **0** (leave the buffer alone). For sharpness use
**Quality -> Render scale**, which supersamples inside the render pipeline and leaves the mapping
intact; MSAA, anisotropy and the shadow settings are likewise safe. The menu labels the buffer row
"Internal scale (breaks aim)" and points at Render scale instead.

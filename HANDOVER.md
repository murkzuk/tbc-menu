# Handover

Everything needed to pick this up cold. Mod version **1.6.0** (per-tank vehicle overrides).

## The two repos

| Repo | What | Licence |
|---|---|---|
| https://github.com/murkzuk/tbc-menu | **This project** - MelonLoader mod for Tank Battle Classic | MIT |
| https://github.com/fiermanb/TankBattleMissionEditor | **Third party** - mission/map editor for the same game, by fierman | GPL-3.0 |

Local copies:

- mod source: `K:\TvTDeepseek\tbc_menu_repo` (git, pushed, clean)
- mission editor (release build): `K:\TvTDeepseek\tbc_mod\missioneditor\app\TankBattleMissionEditor\TankBattleMissionEditor.exe`
- scratch mods / experiments: `K:\TvTDeepseek\tbc_mod`
- IL2CPP dump + dumper: `K:\TvTDeepseek\tbc_modding\` (`dump.cs`, `script.json`, `il2cpp.h`, `DummyDll\`) - **do not publish** (derived from game code)

## The game

- Path: `L:\SteamLibrary\steamapps\common\Tank Battle Classic`
- Version **1.2.0**, Unity **6000.2.10f1**, x86_64, D3D12, URP
- **IL2CPP**, metadata **v31**, unencrypted, unobfuscated
  - `global-metadata.dat` sha256 `510415579aab976aca8135b23a69640dfa7ebb00f5610fb15ff02e7568b925d6`
- No anti-cheat, no DRM wrapper, no Steamworks (Steam Auto-Cloud only, no Workshop)
- Serialized assets have **stripped type trees** - static asset editing needs a typetree source
- Save data: plain JSON at `%USERPROFILE%\AppData\LocalLow\Chobi Softwear\Tank Battle Classic\save.json` (plus `save_backup.json`)
- Built on **Physics Tank Maker** (`ChobiAssets.PTM`). The user owns PTM; its source is at
  `I:\PTM\PTMtest\Assets\Physics Tank Maker` (v3.2). **Never copy PTM source into the public repo.**

## Tooling reality check (important)

- **BepInEx does not work on this game.** BepInEx 6.0.0-be.651 ships Cpp2IL that only reads metadata
  24-29; the game aborts on boot. Don't retry it.
- **MelonLoader 0.7.3 works.** Cpp2IL 2022.1.0-pre-release.21 + Il2CppInterop 1.5.1 generate the
  interop assemblies.
- Il2CppInterop prefixes game namespaces: `ChobiAssets.PTM` -> `Il2CppChobiAssets.PTM` when compiling.
- **Unity stripped the IMGUI module** - `GUI`/`GUILayout` throw "Method unstripping failed". Any overlay
  UI must be uGUI (the mod builds a Canvas with plain Text/Image and hit-tests `Input.mousePosition`).
- Legacy `UnityEngine.Input` works (the game polls keys with it), even though the Input System package
  is present.

## What the mod does

`TBC_Menu` - in-game menu, **F7**. Config at `<game>\UserData\TBC_Menu.json` (written only when you press
Save). Tabs:

| Tab | Controls |
|---|---|
| Resolution | window size, fullscreen mode, internal render scale (0 = keep the stock 854x480 buffer) |
| Visuals | film grain, chromatic aberration, vignette, fog density, AA (off/fxaa/smaa/taa), dithering, texture aniso, commander view |
| Quality | MSAA 1-8x, shadowmap 1024-4096, soft shadow quality, shadow distance, render scale |
| Effects | tonemapping (none/neutral/ACES), bloom + threshold, exposure, contrast, saturation |
| Vehicle | per-tank top speed, traverse, elevation, accel/decel time, torque scale, realism table, live speed readout, dump vehicle values |
| Tuning | tuning multiplier scales, AI preset + scales, unlock all tanks, points, manual respawn |

Menu rows: click `<` / `>` either side of a value (the whole row also works - left half down, right half up).

Other hotkeys: **F6** commander view (per-tank seats, `Alt` + arrows / `PgUp`/`PgDn` / wheel to move the
eye, `Alt+S` to save). The seat is a request, not a one-shot: F6 works from the gun sight (it resets
`Camera_Manager_CS.Camera_Mode` and `Switch_Camera_Mode`), the seat yields to the gun sight and
resumes by itself, and a request made during a scene load waits for the tank instead of failing.

## What was discovered (the useful part)

### Why the game looks the way it does
1. The 3D view renders into a fixed **854x480 point-filtered RenderTexture** (`Render Texture (Main)`),
   stretched to the window by a RawImage on `Canvas (Main RT)`.
2. A global post volume ships **film grain 0.5, chromatic aberration 0.15, vignette 0.2** - nothing else.
3. Exponential fog, density 0.0005 in menus, 0.0010 in missions.
4. URP asset: **MSAA off, main light shadowmap 1024, shadow distance 128, soft shadows Low,
   colour grading LDR, zero renderer features, additional-light shadows unsupported.**
5. The art is full resolution (2048x2048 albedos); the crunch was never the textures.

### Things that surprised us
- The game has **no resolution setting at all**; nothing calls `Screen.SetResolution` at startup. **F11**
  toggles fullscreen (`Full_Screen_Manager_CS`).
- The **turret is slaved to `Camera.main`** (`Aiming_Control_CS` raycasts from it). Parenting a camera to the
  turret makes the turret chase its own child and spin forever - that was a real bug in 1.2.0 of this mod.
- **The Tuning tab's six scales do nothing at runtime.** `Special_Settings_CS` is
  `[DefaultExecutionOrder(-1)]` and copies values into the vehicle in `Start()` and on respawn, so scaling the
  `Tuning_Value` static tables afterwards is a no-op. Use the Vehicle tab instead.
- **Torque is neither the speed nor the acceleration lever** (we believed otherwise in 1.5.0). Top speed is
  `Drive_Wheel_Parent_CS.maxAngVelocity * wheel radius`, and that private field is computed **once** from
  `Drive_Control_CS.Max_Speed` - so the wheel parents have to be re-initialised when the speed changes.
  2100 vs 3000 torque measured identical acceleration; the speed ramp (`Acceleration_Time` plus
  `Acceleration_Flag` on, `Sync_Speed_Rate` off and the cached `acceleRate` refreshed) is what decides
  0-100% time. Full write-up in `docs\vehicle-model.md`.
- The game quantises top speed into **two brackets, 35 or 40 km/h**, for every vehicle. The ~4900-5000
  torque of the AI T-34/76 comes from the prefab, not from code (the player's Tiger I sits at 3000) - the
  multipliers `Special_Settings_CS` applies are 1.0 at the default Tuning level.
- Turret traverse is wildly fast where it should be slow: **T-34/76 35 deg/s** (hand traverse, real ~4-5),
  Hetzer 40, Jagdpanther 15, Tiger I 10, Tiger II 15.
- The game already ships a **Player Vehicle Tuning** screen (Firepower / Armor / Speed / Gun Traverse /
  Reload Speed / Shell Velocity + Score Multiplier) plus an **Experimental** submenu - both clamps at x1.0.

### Vehicle dump
`F7` -> Vehicle -> **Dump vehicle values** writes `<game>\UserData\TBC_Vehicles.json` with every spawned
vehicle's live drive values (`Max_Speed`, `Torque`, `defaultTorque`, accel/decel, mass, the flags), every
wheel parent's `Radius` and `maxAngVelocity`, turret and cannon `Speed_Mag`, and a `tankTypes` list of
`Tank_Prop_SO` names with their spec numbers. Run it **inside a mission** (menu scenes have no
`Drive_Control_CS`); `tankTypes` only lists the assets that scene has loaded.

### Per-tank overrides (1.6.0)
`vehicle.per_tank` holds absolute values per `Tank_Prop_SO.Tank_Name`: `max_speed` (m/s), `traverse`
(deg/s), `elevation` (deg/s), `accel_time`/`decel_time` (s), `torque_scale`. Measured in the tutorial
mission: a T-34/76 taken from the stock 40 km/h cap to `max_speed` 14.7 read **51-52 km/h** on the game's
own speedometer, and `accel_time` 24 s stretched 0->52 km/h from ~7 s to ~22 s. **Load realism table** in
the menu writes the six measured tanks; the rest are listed in `TODO.md` for when their keys are confirmed.

## Where it stands

- Public, MIT, credited to murkzuk with the DeepSeek build note.
- Installed build: `Mods\TBC_Menu.dll` v1.6.0.
- **`TODO.md`**: the per-tank work is done and the four open questions are answered; what is left is adding
  the remaining tanks' keys, measuring torque on slopes, and the small backlog (`cost_scale`,
  `allow_manual_respawn`, the two unexposed AI knobs).
- Verified end-to-end in game on 2026-11-05: menu renders, per-tank apply takes, speed cap moves, ramp
  stretches, realism table writes, dump writes. Method: `K:\TvTDeepseek\scratch\tbcwin.ps1` (window
  launch/screenshot/click/key helpers) + `MelonLoader\Latest.log` + the in-game speedometer.

## Next: the mission editor

The user wants to move on to **https://github.com/fiermanb/TankBattleMissionEditor** (fierman, GPL-3.0,
Python + UnityPy 1.25.4 + TypeTreeGeneratorAPI).

Verified facts worth keeping:

- Release v1.26.0 was generated against **exactly this game build** - its recorded
  `metadata_sha256` equals ours, so all 1075 script layouts apply with no mismatch.
- It edits the game's own `level*` / `sharedassets*` files: tanks, waypoints, routes, scenery, fog, day/dusk/
  night lighting, events with timers and trigger areas, terrain brushes, navmesh rebuild, `.tbscenario`
  import/export, per-save backups.
- Its trick for stripped type trees: generate MonoBehaviour field layouts from `GameAssembly.dll` +
  `global-metadata.dat` with TypeTreeGeneratorAPI, freeze them into `layouts\tbc_layouts.json.gz`, and validate
  per script against `SerializedType old_type_hash`. Writing keeps original object bytes verbatim, appends
  changed objects 16-byte aligned, and regenerates only the metadata block.
- Two ships with it: Kursk - Deep Defense and Kursk - Breakthrough.
- Limitations it states: existing objects and tank types only, maps use the game's terrains and cannot be
  resized, terrain/AI-route rebuild only for custom scenarios, event messages capped ~45 chars, briefing
  lists at most 9 unit groups.

Natural overlap with this mod: the editor cannot change per-vehicle combat values, so a per-tank realism
table here (TODO item) and a custom scenario there are complementary.

## Custom scenarios (2026-11-05, unsupervised session)

Two custom missions are installed in the game under the editor's **Custom Missions** page:
`TBC proof - waypoints and timers` (city terrain) and `TBC rolling ground` (a 2.5x stretched
Mountain terrain, the one that plays as rolling hills). Both are backed up in the editor's
`backups\<slug>\scenarios\` and can be removed with `Scenario > Remove scenario`.

The whole workflow is scriptable with the editor's own Python modules - no GUI needed:
`ScenarioManager.create`, `TerrainPaint.ensure_copy`, `SceneModel.set_field` /
`new_route` / `new_waypoint`, `briefing_sync.sync_mission`, `navmesh_edit.NavMesh`,
`ScenarioBackups.backup`. Working notes and the evidence are in
`docs/terrain-and-scenarios.md` (repo) and `scratch\rolling-hills-scenario.md` (workspace).

Key facts: a terrain's world size is `m_Heightmap.m_Scale` x/z (an 8-byte patch to stretch);
stretching alone keeps cliff structure, so heights also need a blur and an amplitude scale;
~2.5 deg mean slope with ~9 m of relief over 2.5 km is the band that still reads as hills;
`Trigger_Tanks` holds Transform pids; AI pathing needs the route map rebuilt after any terrain or
object change.

Still open there: the mission-complete event is wired to the single enemy but has not been played
through by actually killing it; 7 unused hostile spawns remain deactivated in the scene.

## Housekeeping

- Back up `save.json` before using **Unlock all tanks** or **Points** - they write into live player data
  which the game saves on exit.
- To go back to vanilla: delete `version.dll`, `MelonLoader\`, `Mods\`, `UserData\` from the game folder. Steam
  "verify integrity" also removes the loader but keeps the editor's custom scenarios.
- Build: `tools/find-game.ps1` then `build.ps1` (produces `dist\TBC_Menu-<version>.zip`).
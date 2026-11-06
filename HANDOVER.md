# Handover

Everything needed to pick this up cold. Mod version **1.7.6** (native-resolution rendering in every
camera mode, the menu and buffer clean-up, a Tuning tab that undoes itself, the four vehicle-override
fixes, and the Effects tab retired as not deliverable - the 1.7.6 commit, see `git log`). Two
installed custom scenarios. Read this, then `README.md`, `TODO.md`, `docs/vehicle-model.md` and
`docs/terrain-and-scenarios.md`.

## The two repos

| Repo | What | Licence |
|---|---|---|
| https://github.com/murkzuk/tbc-menu | **This project** - MelonLoader mod for Tank Battle Classic | MIT |
| https://github.com/fiermanb/TankBattleMissionEditor | **Third party** - mission/map editor for the same game, by fierman | **GPL-3.0** |

Local copies:

- mod source: `K:\TvTDeepseek\tbc_menu_repo` (git, clean, pushed)
- mission editor, installed release: `K:\TvTDeepseek\tbc_mod\missioneditor\app\TankBattleMissionEditor\`
  (`TankBattleMissionEditor.exe`, `settings.json` already points at the game, `Scenarios\`, `backups\`)
- mission editor, **source clone**: `K:\TvTDeepseek\scratch\tbcme` - its Python modules are imported
  headlessly (see below). GPL-3.0: do not copy its code into the MIT repo.
- IL2CPP dump + tools: `K:\TvTDeepseek\tbc_modding\Il2CppDumper\` (`dump.cs`, `script.json`,
  `il2cpp.h`, `DummyDll\`) - **do not publish** (derived from game code)
- scratch / experiments / notes: `K:\TvTDeepseek\scratch` (see "Automation harness")

## The game

- Path: `L:\SteamLibrary\steamapps\common\Tank Battle Classic`
- Version **1.2.0**, Unity **6000.2.10f1**, x86_64, D3D12, URP
- **IL2CPP**, metadata **v31**, unencrypted, unobfuscated
  - `global-metadata.dat` sha256 `510415579aab976aca8135b23a69640dfa7ebb00f5610fb15ff02e7568b925d6`
- No anti-cheat, no DRM wrapper, no Steamworks (Auto-Cloud only, no Workshop)
- Serialized assets have **stripped type trees** - any asset editor must regenerate the field
  layouts from `GameAssembly.dll` + `global-metadata.dat` and validate per object.
- Save data: plain JSON at `%USERPROFILE%\AppData\LocalLow\Chobi Softwear\Tank Battle Classic\save.json`
- Built on **Physics Tank Maker** (`ChobiAssets.PTM`). The user owns PTM (source at
  `I:\PTM\PTMtest\Assets\Physics Tank Maker`, v3.2). **Never copy PTM source into the public repo.**

## Tooling reality check

- **BepInEx does not work** on this game (its Cpp2IL reads metadata 24-29; the game aborts on boot).
- **MelonLoader 0.7.3 works.** Il2CppInterop prefixes namespaces: `ChobiAssets.PTM` ->
  `Il2CppChobiAssets.PTM`. Private fields *and* private methods are reachable through the interop
  proxies (e.g. `wp.Initialize()`, `mgr.Switch_Camera_Mode(false)`) - verified by compiling.
- **IMGUI is stripped** - any overlay UI must be uGUI with manual hit-testing (that is what the mod does).
- Disabling the mod without uninstalling: rename `<game>\version.dll` (MelonLoader's proxy) and the
  game runs vanilla; rename back to re-enable. That is how "is it the mod?" gets tested.

## What the mod does

`TBC_Menu` - in-game menu, **F7**, config at `<game>\UserData\TBC_Menu.json` (written only on Save).

| Tab | Controls |
|---|---|
| Resolution | window size, fullscreen mode, **Render direct to screen**, internal buffer scale (0 = stock, "breaks aim"), live buffer readout |
| Visuals | film grain, chromatic aberration, vignette, fog density, AA, dithering, texture aniso, commander view |
| Quality | MSAA, shadowmap, soft shadows, shadow distance, render scale |
| Effects | tonemapping, bloom, exposure, contrast, saturation |
| Vehicle | per-tank top speed / traverse / elevation / accel / torque, realism table, live speed readout, dump vehicle values |
| Tuning | tuning scales, max levels, AI preset + scales, AI passive, unlock all tanks, points |

Hotkeys: **F7** menu, **F6** commander view (per-tank seats; `Alt`+arrows/`PgUp`/`PgDn`/wheel to
move the eye, `Alt+S` saves), **F8** dumps render/UI geometry to the log.

## The five things that matter (measured, not guessed)

1. **Top speed is `Drive_Wheel_Parent_CS.maxAngVelocity * wheel radius`.** That private field is
   computed **once** in `Initialize()` from `Drive_Control_CS.Max_Speed`, so the wheel parents must
   be re-initialised after a speed change. T-34/76 measured 51-52 km/h vs the stock 40.
2. **Torque is not the speed or acceleration lever.** 2100 vs 3000 torque measured identical
   acceleration; wheels are at their angular-velocity cap and the hull is traction-limited. It still
   decides what a tank pulls on a slope, and `Torque` is recomputed every frame from `defaultTorque`.
3. **Acceleration is the ramp**: `Acceleration_Flag` on, `Sync_Speed_Rate` off, cached private
   `acceleRate` refreshed. With all three, 0->52 km/h went from ~7 s to ~22 s.
4. **Aiming depends on two static fields.** `Aiming_Control_CS` casts with
   `Camera.main.ScreenPointToRay` on a *screen* position, converted to buffer pixels with
   `General_Settings_CS.Render_Texture_Width/Height` (**static, initialised 854x480, never read from
   the real surface**). Resize the buffer or bypass it without updating those and the gun trails the
   crosshair. Full table of what each change did in `README.md` / `docs/graphics-notes.md`.
5. **A terrain's world size is `m_Heightmap.m_Scale.x/z`** - extent = `(resolution - 1) * scale`. Two
   floats. Changing them stretches the heightmap: features widen and slopes divide. It is an 8-byte
   patch (`1.0, 128.0, 1.0` appears exactly once per TerrainData) and it must be done on a scenario's
   **own** terrain copy, never on a shipped `sharedassetsN.assets`.

## Custom scenarios (installed and backed up)

Two missions are on the game's **Custom Missions** page (06/06):

| Scenario | Battle | Terrain |
|---|---|---|
| `TBC proof - waypoints and timers` | `level68` | copy of City 03, emptied |
| `TBC rolling ground` | `level70` | copy of Mountain 01, **stretched 2.5x to 2560 m**, blurred, amplitude x0.32 (= half the earlier height) |

`TBC rolling ground` content: player T-34/76, one hostile Pz. III N on a four-waypoint patrol, a
timer message at 8 s, mission complete when that tank dies. Verified in game: the message fires, the
AI drives its route (11.8 m/s), and the mission has now been **played through to the results screen
twice** - `Clear_Count 2`, best `69.87 s`, 1 kill, `Hit_Ratio 100.0`, no damage taken, `Total_Score
2100`, then a clean return to the mission list. Vertical relief ~4.6 m, mean slope ~1.2 deg.

Both are backed up by the editor in `...\backups\tank_battle_classic-2e580b51\scenarios\<scene_id>\`
(`battle.level`, `terrain.npz`, `meta.json`, `mission.json`), so a game update cannot destroy them.
Remove them with the editor's `Scenario > Remove scenario`, or restore from those backups.

### Building one without the GUI (the reliable path)

The editor's window is DPI-stretched on this machine and its rows are ~20 px tall, so synthetic
clicks land on the wrong row; the F5/F6 menu accelerators help but item selection needs a mouse.
**Do it headlessly instead** - everything below works with the source clone:

```python
import sys; sys.path.insert(0, r"K:\TvTDeepseek\scratch\tbcme")
from scene_io import GameContext
from scene_model import SceneModel, tank_catalogue
from scenarios import ScenarioManager
from scenario_backup import ScenarioBackups
from briefing_sync import sync_mission
from navmesh_edit import NavMesh
from terrain_edit import TerrainPaint, read_heights, with_heights
from terrain_features import box_blur

game = r"L:\SteamLibrary\steamapps\common\Tank Battle Classic"
backup = r"K:\TvTDeepseek\tbc_mod\missioneditor\app\TankBattleMissionEditor\backups\tank_battle_classic-2e580b51"
ctx = GameContext(game); mgr = ScenarioManager(ctx, backup)

scene_name, battle_idx = mgr.create(124, "Title", "Briefing")   # 124 = Hunter or Hunted (Mountain)
m = SceneModel(ctx, battle_idx)
tp = TerrainPaint(m); m.begin("copy"); tp.ensure_copy(); m.commit(); m.save(); tp.assets.save()
#   patch m_Scale x/z in the scenario's own sharedassetsN.assets, and/or write heights:
#   sf.write(tpid, with_heights(td, raw))  with  raw = blurred/amplitude-scaled heights

m.begin("content")
m.set_field(transform_pid, "m_LocalPosition", {"x": x, "y": ground(x, z), "z": z})
m.set_field(spawn_event_pid, "m_IsActive", True)
w1 = m.new_route(p1); pack_go = m.parent_go(w1); pack_tr = m.tr_of_go[pack_go]
for p in (p2, p3): m.new_waypoint(pack_tr, prev, p)
m.set_field(spawn_event_pid, "WayPoint_Pack", {"m_FileID": 0, "m_PathID": pack_go})
m.commit(); m.save()

sync_mission(GameContext(game), mgr, SceneModel(GameContext(game), battle_idx), backup)
m3 = SceneModel(GameContext(game), battle_idx); nav = NavMesh(m3); tiles, _ = nav.build()
m3.begin("rebuild"); nav.write(tiles); m3.commit(); m3.save()
ScenarioBackups(ctx, mgr).backup(entry)      # protect it
```

**Wiring gotchas that cost a crash:** `Trigger_Tanks` holds **Transform** pids (a component pid makes
the rule pass vacuously and the event fires at mission start; a GameObject pid makes the game exit
while loading the scene). `WayPoint_Pack` points at the pack's **GameObject**, `Respawn_Point_Pack`
at its **Transform**. Deactivation (`m_IsActive = False`) clears play but keeps the object in the
file, and the briefing screen must be regenerated (`sync_mission`) after spawn changes.

**Terrain shaping, in numbers:** remeasure after any change - `ScenarioManager` work is cheap but
iterating on slopes needs the statistics (`np.gradient` over the stretched cell size, mean/p95/max
slope). Measured band for "reads as rolling hills" in this game's camera: **~2.5 deg mean slope with
~9 m relief over 2.5 km**. At 1.9 deg / 7 m it reads as a plain. Stretching alone keeps the cliff
structure (a 2.5x stretch left a 55 deg maximum), so blur (`box_blur` x2, r=8) and amplitude matter.

## Automation harness

`K:\TvTDeepseek\scratch\`:

- `tbcwin.ps1` - game window: `Save-TbcShot`, `Save-TbcCrop`, `Invoke-TbcClick`, `Invoke-TbcKey`,
  `Invoke-TbcHold`, `Invoke-TbcMouse` (1 = left, 2 = right)
- `scr.ps1` - DPI-aware full-screen capture and clicks for non-game windows (`SetProcessDpiAwarenessContext`)
- `mewin.ps1` - the editor window (same idea; see the DPI warning above)
- `offsets.json` + `dis.py` / `range.py` / `callers2.py` - Capstone disassembly of
  `GameAssembly.dll` by `Type::Method` name, with call targets resolved
- `terrain_stats2.py`, `all_terrains.py`, `quadrants.py`, `spectrum.py` - the terrain survey
- `gen_candidates2.py` - procedural heightmap experiments (rejected: noise looked blotchy)
- `half_height.py`, `soften*.py`, `stretch_test.py` - terrain shaping, verified to round-trip
- notes: `rolling-hills-scenario.md`, `mission-editor-proof.md`, `terrain-relief-analysis.md`

Pattern for verifying anything in game: screenshot/measure, never assume. The mod's own instruments -
**F8** geometry dump, **Dump vehicle values** (`UserData\TBC_Vehicles.json`, includes positions and
drive flags) and `MelonLoader\Latest.log` - are usually faster than eyeballing a screenshot.

## Where it stands right now

- Repo clean, pushed, MIT, credited to murkzuk with the DeepSeek build note. `dist\TBC_Menu-1.7.6.zip`.
- Installed: `<game>\Mods\TBC_Menu.dll` 1.7.6 (`ABABDC33...`), hash-identical to the build. `version.dll` is back
  in place (MelonLoader active).
- User config: `Render direct to screen` on, `internal_scale` 0, MSAA 8, render scale 2, shadow
  distance 400, seven per-tank vehicle entries, `player_only: true`.
- Game and mission editor were both closed at handover; nothing is left running.
- `save.json` carries the two verified clears (`Mission_Cleared` 6, 12 kills, 12200 points); backups
  taken before the play tests are in `scratch\save_before_play*.json`.

## Verified since the previous handover (1.7.1, 1.7.2)

Both items that were open at the 1.7.0 handover have been measured and closed, and 1.7.2 closed the
first of the follow-ups:

- **1.7.4 - the vehicle overrides are correct now.** Torque scales `defaultTorque` instead of the
  slope-limited `Torque` (dump evidence: 2799.9 vs 3000.0); `decel_time` refreshes its own cached
  `deceleRate` and turns the ramp on, instead of being inert unless `accel_time` was also set;
  *Clear this tank* puts the game's values back (measured: `maxSpeed 11.200 / defaultTorque 3000 /
  accel 8.00 / deceleRate 0.500 / syncSpeedRate on`); *Load realism table* updates the three fields
  in place so `elevation`/`torque_scale`/`decel_time` survive. The dump also reports
  `acceleRate`/`deceleRate` now. **And the tank keys are settled offline**: the editor's
  `tank_catalogue()` shows 38 `Tank_Prop_SO` assets = 19 vehicles x (player + AI), AI variants share
  the name, and every key in `RealismTable` was already right.
- **1.7.3 - the Tuning tab is reversible.** Unticking it now restores the six multiplier arrays, the
  live AI settings and the passive flag, and the multiplier indices it may have overwritten; before,
  all of it latched (the arrays especially, since they are copied into every vehicle at spawn).
  Measured from the log with attack/drive/reload 1.5 and preset `ace`: applied `drive 1.800 /
  lead 2.025 / passive on`, restored `drive 1.200 / lead 1.500 / passive off`. *Dump vehicle values*
  gained an `ai:` block on the way.
- **1.7.2 - the buffer is identified by the canvas that displays it**, so a briefing screen can no
  longer mistake its 256x256 minimap for the 854x480 view buffer. In the same release: fog returns
  to the game value at scale 1.0 (it used to stick), *Reload from disk* rebuilds the menu instead of
  leaving it bound to the replaced config object, per-frame warnings are rate-limited, and the gun
  is genuinely muted while the menu is open - the 1.7.0 mute never worked, because the game's mouse
  input scripts have no `Update` and are polled by their owner; the fire path's `Is_Loaded` guard is
  what is held down instead. Verified with `save.json`: one shot in a whole session, from the single
  click made with the menu closed.

- **The bypass now covers every camera mode.** With `bypass_buffer` on, 1.7.0 left `Gun_Camera`
  (gun sight) and `Camera (Map)` rendering into the 854x480 buffer whose canvas the bypass hides -
  a dark scope with a reticle and no world, and a blank map. Both cameras only exist while their
  view is switched on, so an inactive camera is absent from `Camera.allCameras` and a one-shot
  redirect missed them. 1.7.1 maintains the redirect every frame and resets its tracking per
  scene; the log names each camera as it is caught. Verified in game: both views at native
  resolution, map markers on their tanks.
- **Mission complete has been played through**, twice - see the scenario section above. The kill
  was a single gun-sight shot at 247 m, which is also the proof that the aiming correction (the
  `Render_Texture_Width/Height` trap) holds: `Hit_Ratio 100.0`, `Penetration 60.6/30.0 mm`.

## Open items, in the order they matter

1. **The Effects tab is retired** - this game's pipeline honours only the volume components it ships,
   so exposure/contrast/saturation/bloom/tonemapping cannot be added from a mod (three delivery
   routes tried and measured; see `TODO.md`). Film grain, chromatic aberration and vignette work and
   live in the Visuals tab. Going further means patching below the Volume system - a research task.
2. **Aim at a non-16:9 window** is geometry-checked, not shot-tested: at 1440x1080 the game
   letterboxes the 3D camera (`pixelRect 0,135,1440,810`) while the constants track the screen, so
   the two no longer match. 16:9 is unaffected. Fix, if needed, is to take the constants from the main
   camera's `pixelRect`.
3. **Terrain: iterate on the half-height version** - it is at ~1.2 deg mean slope now; if it reads too
   flat, the way to keep the height while restoring hill character is shorter wavelengths (ridged
   noise), not more amplitude.
4. **The 12 unmeasured tanks still only have estimated performance.** Their `Tank_Name` keys are
   verified (see 1.7.4 above) and the entries therefore work; what is guessed is `max_speed`,
   `traverse` and `accel_time` for everything except the six measured tanks.

5. **Torque on slopes** is still unmeasured (`torque_scale` only shows on gradients).
6. **Dead AI knob** - `ai_visibility_scale` multiplies `Extended_Visibility`, which is **0** on this
   game's vehicles, so it does nothing. Find the field the AI actually spots with, or drop the knob.
7. **Backlog**: `cost_scale` and `allow_manual_respawn` (lost in an earlier consolidation), the two
   AI knobs that exist in config but have no menu row, AI deceleration (dumps show 0.01 s, i.e. AI
   tanks stop instantly).

## Housekeeping

- Back up `save.json` before using **Unlock all tanks** or **Points** - they write into live player
  data, which the game saves on exit.
- To go vanilla: delete `version.dll`, `MelonLoader\`, `Mods\`, `UserData\` from the game folder
  (Steam "verify integrity" also removes the loader). That does **not** remove the custom scenarios -
  they live in `Tank Battle Classic_Data` and are removed with `Scenario > Remove scenario`.
- Build: `tools/find-game.ps1` then `build.ps1` (produces `dist\TBC_Menu-<version>.zip`).
- A game update replaces `level*`/`sharedassets*`: the mod needs rebuilding against the new
  assemblies, and custom scenarios need `Scenario > Restore scenarios from backup` (the editor
  converts them).

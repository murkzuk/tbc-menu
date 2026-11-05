# Handover

Everything needed to pick this up cold. Mod version **1.7.0** (native-resolution rendering),
last commit `989143a`. Two installed custom scenarios. Read this, then `README.md`, `TODO.md`,
`docs/vehicle-model.md` and `docs/terrain-and-scenarios.md`.

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
AI drives its route (11.8 m/s), the mission completes. Vertical relief ~4.6 m, mean slope ~1.2 deg.

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

- Repo clean, pushed, MIT, credited to murkzuk with the DeepSeek build note. `dist\TBC_Menu-1.7.0.zip`.
- Installed: `<game>\Mods\TBC_Menu.dll` 1.7.0, hash-identical to the build. `version.dll` is back
  in place (MelonLoader active).
- User config: `Render direct to screen` on, `internal_scale` 0, MSAA 8, render scale 2, shadow
  distance 400, seven per-tank vehicle entries, `player_only: true`.
- Game and mission editor were both closed at handover; nothing is left running.
- `save.json` is untouched (a pre-test backup was taken and removed; nothing wrote to it).

## Open items, in the order they matter

1. **Verify the bypass in the less-travelled camera states** - gun sight and map camera with
   `bypass_buffer` on. Third person, driving, HUD, messages and the gun sight were checked; the map
   camera was not. If something is off, the toggle is live in the Resolution tab.
2. **Mission complete has never been played through** - the trigger is wired to the enemy's Transform
   (correct form, copied from the stock mission) but the tank was never actually killed.
3. **Terrain: iterate on the half-height version** - it is at ~1.2 deg mean slope now; if it reads too
   flat, the way to keep the height while restoring hill character is shorter wavelengths (ridged
   noise), not more amplitude.
4. **Torque on slopes** is still unmeasured (`torque_scale` only shows on gradients).
5. **Backlog**: `cost_scale` and `allow_manual_respawn` (lost in an earlier consolidation), the two
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

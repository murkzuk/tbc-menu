# TODO

## Done: menu and buffer clean-up (1.7.2)

Five fixes, each verified in game rather than reasoned about:

- **The buffer finder is anchored, sticky and throttled.** It now takes the RenderTexture from the
  RawImage on `Canvas (Main RT)` and refuses anything under 512 px, so the 256x256 minimap of a
  briefing screen can no longer be mistaken for the view buffer (1.7.1 logged
  `camera 'Camera (Map)' ... (had the 256x256 buffer)`). The reference survives a scene transition
  and is re-acquired at most twice a second.
- **Fog returns to the game value at scale 1.0.** The old early-out meant going back to 1.0 left the
  scene at whatever the last scaled density was. It now writes and logs
  `fog density 0.00045 (game 0.00075 x 0.60)`.
- **Reload from disk rebuilds the menu.** `Load()` replaces the config object while every row
  closure had captured a section of the old one, so edits after a reload went nowhere. Verified:
  reload reverts the display, and the next click still reaches the config.
- **Per-frame failures log once per 5 s** instead of every frame.
- **The gun is actually muted while the menu is open.** The 1.7.0 mute never worked: the game's mouse
  input scripts (`Cannon_Fire_Input_01_Mouse_CS`, `Camera_Rotation_Input_01_Mouse_CS`, ...) have
  **no `Update`** - they expose `Get_Input()` and their owner calls it every frame - so
  `enabled = false` does nothing (measured: a click fired through the menu with four of them
  disabled). What the fire path does test is `Cannon_Fire_CS.Is_Loaded`, so the player's cannon is
  held unloaded while the menu is up, re-asserted on the 2 s tick and after a scene change, and
  handed back loaded on close. AI cannons are untouched. Verified with `save.json`: one shot in a
  whole session, from the single click made with the menu closed.

Cosmetic: a frozen "Reloading" bar while the menu is open. Clicks still reach the game's own UI
behind the panel - only the gun is protected.

## Done: the bypass in every camera mode, and a mission played to completion (1.7.1)

Two handover items were open; both are now measured rather than assumed.

**The gun sight and the map were broken by the bypass.** With `bypass_buffer` on, 1.7.0 left
`Gun_Camera` and `Camera (Map)` rendering into the 854x480 buffer whose canvas the bypass hides,
so the gun sight showed the dark scope disc and its reticle with no world behind it, and the map
was blank. Both cameras are inactive - hence absent from `Camera.allCameras` - until their mode
is switched on, so a redirect applied once at scene load never saw them. 1.7.1 maintains the
redirect every frame and resets its tracking per scene. Verified in game: both views render at
native resolution, the map's markers sit on their tanks, and the log names each camera as it is
caught (`camera 'Gun_Camera' now draws to the screen (had the 854x480 buffer)`).

**Mission complete has been played through**, twice. The second run, from the log and the game's
own save data: `TBC_rolling_ground`, `Clear_Count 2`, `Clear_Time 69.87 s`, `Kills 1`,
`Hit_Ratio 100.0`, `Hits_Taken 0`, `Total_Score 2100`, the `99_Result` scene and a clean return
to the mission list. The kill was a single gun-sight shot at **247 m** (hit angle 39.1 deg,
`Penetration 60.6/30.0 mm`), which doubles as proof of the aiming correction: a scale error in
`Render_Texture_Width/Height` is metres wide at that range.

**Still open:** in a *briefing* scene the buffer is found through the full-screen RawImage, and
the minimap's 256x256 RenderTexture can be picked instead (log: `camera 'Camera (Map)' ... (had
the 256x256 buffer)`). Battle scenes are unaffected. Fix: identify the buffer as the RenderTexture
the world cameras render into, never one under about 512 px.

## Done: per-tank vehicle overrides (1.6.0)

The Vehicle tab now takes **absolute** per-tank values, keyed by `Tank_Prop_SO.Tank_Name`
("T-34/76", "Tiger I", ...), in the same shape as the commander seats:

    "vehicle": {
      "enabled": true,
      "player_only": false,
      "per_tank": {
        "T-34/76":  { "max_speed": 14.7, "traverse": 5.0, "accel_time": 24.0 },
        "Tiger I":  { "max_speed": 12.7, "traverse": 8.0, "accel_time": 26.0 }
      }
    }

`max_speed` m/s, `traverse` and `elevation` deg/s, `accel_time`/`decel_time` seconds, all
absolute; `torque_scale` multiplies the game's torque. Anything left out falls back to the global
scales. **Load realism table** in the menu writes the table below for all six measured tanks, and
**Dump vehicle values** writes the live fields (plus the flags) to `UserData\TBC_Vehicles.json`.

The mechanisms behind all of it are in [docs/vehicle-model.md](docs/vehicle-model.md).

### The four open questions, answered

- **Does scaling `Torque` change top speed?** No. Top speed is `maxAngVelocity * wheel radius`,
  and `maxAngVelocity` is a private field the wheel parent computes once from `Max_Speed`. It
  also does not change acceleration on flat ground: 2100 vs 3000 torque measured identically
  (15 / 33 / 48 / 52 km/h at 2 / 4 / 6 / 10 s) because the wheels are already at their angular
  velocity cap and the hull is traction-limited. Torque still decides what a tank pulls on a
  slope (`Limit_Torque`), so the knob stays.
- **Does `Max_Speed` clamp anything?** Yes - it is both the wheel-speed reference and the
  `Current_Velocity / Max_Speed` normaliser. Measured: writing it plus re-running the wheel
  parents moved a T-34/76 from the stock 40 km/h to 51-52 km/h.
- **Where does the AI's flat ~5000 torque come from?** From the prefabs, not from `Special_Settings_CS`:
  `Send_Special_Settings()` only multiplies whatever the prefab holds by the Tuning multiplier for
  the level (1.0 at the default level). All T-34/76 in the dumps sit at 4900-5000 and the player's
  Tiger I at 3000, so it is per-prefab tuning, not a constant the code forces.
- **Per-tank keys.** `Tank_Prop_SO.Tank_Name` is stable; the GameObject name is not
  ("Tiger-I_Late_ST(Clone)"). The mod falls back to the object name only if the SO is missing.

### What the real lever for "go-kart" feel turned out to be

Not torque - the **speed ramp**. `Acceleration_Time` is only honoured when
`Acceleration_Flag` is on **and** `Sync_Speed_Rate` is off **and** the private cached
`acceleRate` is refreshed (it is computed once in `Initialize()`). The mod does all three when a
`accel_time` is set. Measured on the T-34/76, 0 -> 52 km/h:

| stock | `accel_time` 24 s |
|---|---|
| ~7 s | 5 s: 12 km/h, 12 s: 29 km/h, 24 s: 52 km/h |

### The realism table (starting point, in the menu)

Road top speed, turret traverse, and time from standstill to full speed. The accel column is what
stops every tank feeling like a go-kart; the traverse column is the biggest historical error
(the T-34/76 is modelled at 35 deg/s, which is roughly 8x reality).

| Tank | `max_speed` | `traverse` | `accel_time` | stock speed | stock traverse |
|---|---|---|---|---|---|
| Pz. III N | 11.1 m/s (40 km/h) | 16 deg/s | 18 s | 40 km/h | 45 |
| Jagdpanther | 12.8 (46) | 5 (casemate) | 22 s | 40 | 15 |
| T-34/76 | 14.7 (53) | 5 | 24 s | 40 | 35 |
| Tiger I | 12.7 (45.8) | 8 | 26 s | 35 | 10 |
| Hetzer | 11.7 (42) | 6 (casemate +/-10) | 26 s | 40 | 40 |
| Tiger II | 11.5 (41.5) | 9 | 30 s | 35 | 15 |

Accel times are estimates from power/weight and period trials, not measured on a real vehicle -
treat them as a starting point. Speeds and traverse rates are the measured in-game values against
the historical figures from the earlier dumps.

### The vehicle roster (19) and where each entry stands

The scenario editor's Kursk pack declares the game's full playable roster (`meta.json` ->
`player_tanks`), which is the ground truth for what exists:

| Asset (`*_TankProp`) | Table key | Status |
|---|---|---|
| `T-34_76` | `T-34/76` | **measured** |
| `Tiger-I` | `Tiger I` | **measured** |
| `Tiger-II` | `Tiger II` | **measured** |
| `Hetzer` | `Hetzer` | **measured** |
| `Jagdpanther` | `Jagdpanther` | **measured** |
| `Panzer-III_N` | `Pz. III N` | **measured** |
| `T-34_85`, `Panther-G`, `Panzer-III_J`, `Panzer-IV`, `StuG-III_Late`, `KV-1`, `KV-2`, `IS-2`, `Sdkfz251`, `Sdkfz251_Pakwagen`, `BT-7`, `BT-42` | guess | estimates in the table, **key unconfirmed** |
| `Tiger-I_Late` | `Tiger I`? | probably the same `Tank_Name` as `Tiger-I`, so one entry covers both |

`BT-7` was confirmed from a live dump; every other guess needs one dump taken in a mission that
spawns that vehicle (`F7 -> Vehicle -> Dump vehicle values`, then read `tankTypes` and the per-drive
`tank` field). A wrong key is harmless - it simply never matches.

## Answered: an AI does patrol a waypoint pack

The earlier stall (35 m of travel then ~4 cm/s wheelspin) was the **stale AI route map**, not the
route. Rebuilding it for the edited terrain fixed it: on a 2.5x stretched Mountain terrain a
four-waypoint patrol kept a Pz. III N moving at ~11.8 m/s, measured from the vehicle dump
(`speedRate 1.000`, `velocity_mps 11.838`, then 4.98 m/s at the next waypoint). Timed events work
too - an 8 s timer message appeared on schedule and cleared after its display time.

Custom scenarios can be built entirely headlessly with the editor's own Python modules
(`ScenarioManager.create`, `TerrainPaint.ensure_copy`, `SceneModel.set_field`/`new_route`,
`briefing_sync.sync_mission`, `navmesh_edit.NavMesh`) - no GUI needed. See
[docs/terrain-and-scenarios.md](docs/terrain-and-scenarios.md) for the measurements and the recipe.

## Backlog

- **Briefing-scene buffer identification** - see the 1.7.1 section above: `FindBuffer()` should
  take the RenderTexture the world cameras render into and refuse anything below ~512 px, so the
  256x256 minimap can never be mistaken for the view buffer.
- **Restore `cost_scale`** - scale `Tank_Prop_SO.Tank_Tier_Costs` so unlocks are cheaper. Existed in the
  old standalone tuner, lost in the consolidation.
- **Restore `allow_manual_respawn`** - one-line toggle on
  `General_Settings_CS.Allow_Manual_Respawn`. Also lost in the consolidation.
- **Expose the two AI knobs** that exist in the config but have no menu row:
  `ai_visibility_scale`, `ai_combat_speed_scale`.
- **Torque on slopes** - `torque_scale` is currently unmeasurable on flat ground. Measure a climb
  (gradient + time) to give it a number worth putting in the table.
- **AI decel is 0.01 s** in the dumps, i.e. AI tanks stop instantly. Worth a global
  `decel_time` floor for AI.
- **`tankTypes` in the dump is scene-limited** - it lists the `Tank_Prop_SO` assets the current
  scene has loaded, not every tank in the build.

## Known limitations

- **The Tuning tab's six scales have no visible effect on gameplay.** They scale the static
  `Tuning_Value` multiplier tables, but the game copies those into each vehicle at spawn
  (`Special_Settings_CS` is `[DefaultExecutionOrder(-1)]` and pushes values in `Start()` and on respawn).
  Changing the tables mid-mission is a no-op. Use the Vehicle tab instead.
- **`accel_time` turns `Sync_Speed_Rate` off for that tank.** The game's own speed synchronisation
  is what overrides the ramp, so it has to go; the side effect on hills and soft ground has not
  been measured.
- Commander seats are per tank (`camera.per_tank`); the rest of the camera settings are global.
- Vehicle overrides are re-applied every 2 s while enabled, so they survive respawns and scene
  changes, but a tank spawned and destroyed within 2 s may keep the game's values.
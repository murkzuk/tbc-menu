# TODO

## Next up: per-tank vehicle overrides (realism pass)

Vehicle values are currently one global multiplier per axis (Vehicle tab). That cannot express real
differences between vehicles, because the game's own numbers are compressed into two brackets.

### Measured in game (TBC_Vehicles.json dumps, 2 scenes)

Top speed, from `Drive_Control_CS.Max_Speed` (m/s = km/h / 3.6, and it always matches the game's
own `Tank_Prop_SO.Speed` spec figure):

| Tank | In game | Real (road) | Delta |
|---|---|---|---|
| T-34/76 | 11.2 m/s = 40 km/h | ~53 km/h | -24% |
| Tiger I | 9.75 m/s = 35 km/h | ~45.8 km/h | -23% |
| Tiger II | 9.75 m/s = 35 km/h | ~41.5 km/h | -15% |
| Jagdpanther | 11.2 m/s = 40 km/h | ~46 km/h | -12% |
| Hetzer | 11.2 m/s = 40 km/h | ~42 km/h | -4% |
| Pz. III N | 11.2 m/s = 40 km/h | ~40 km/h | 0% |

Every `specSpeed` seen so far is either **35 or 40** - the game quantises top speed into two
brackets, so a Tiger I and a T-34/76 end up 5 km/h apart.

Turret traverse, from `Turret_Horizontal_CS.Speed_Mag` (degrees per second):

| Tank | In game | Real | Delta |
|---|---|---|---|
| T-34/76 | 35 | ~4-5 (hand traverse) | ~8x too fast |
| Hetzer (casemate +/-10) | 40 | ~5-10 | 4-8x too fast |
| Pz. III N | 45 | ~14-20 | ~2.5x too fast |
| Jagdpanther (casemate +/-11) | 15 | ~5 | 3x too fast |
| Tiger II | 15 | 6-19 (rpm dependent) | plausible |
| Tiger I | 10 | 6-19 (rpm dependent) | plausible |

Also measured: **every AI vehicle sits at ~4900-5000 `Torque`** regardless of mass (Hetzer is ~15 t,
Tiger II ~70 t), which is why everything accelerates like a go-kart. The player's tank came in lower
(Tiger I base ~3000, Pz. III N ~2990).

### Plan

Per-tank overrides keyed by tank name, same pattern as the commander seats (`camera.per_tank`):
explicit **m/s** and **degrees per second** rather than multipliers, so the config reads like a
reference table.

    "vehicle": {
      "enabled": true,
      "per_tank": {
        "T-34/76":    { "max_speed": 14.7, "torque_scale": 0.7,  "traverse": 5  },
        "Tiger I":    { "max_speed": 12.7, "torque_scale": 0.6,  "traverse": 8  },
        "Tiger II":   { "max_speed": 11.5, "torque_scale": 0.55, "traverse": 9  },
        "Hetzer":     { "max_speed": 11.7, "torque_scale": 0.7,  "traverse": 6  },
        "Jagdpanther":{ "max_speed": 12.8, "torque_scale": 0.6,  "traverse": 5  },
        "Pz. III N":  { "max_speed": 11.1, "torque_scale": 0.7,  "traverse": 16 }
      }
    }

Still to fill in: Panther G (46 km/h, 18-24 deg/s), KV-1 (35, 4-5), KV-2 (28, 4), IS-2 (37, 6-8),
T-34/85 (53, 20-25), StuG III (40, casemate), Sd.Kfz.251 (53), Pakwagen (80), BT-7 (52), BT-42.

### Open questions to resolve first

- Does scaling `Torque` actually change top speed on the heavy tanks, or does something else bind
  first? Needs a timed measurement, not a static dump.
- `Max_Speed` is only used as a normalisation reference in PTM (`Current_Velocity / Max_Speed` for
  anti-slip and downforce) - confirm whether it clamps anything in this newer build.
- Where does the AI's flat ~5000 torque come from? Per-prefab, or applied by the game's
  `Special_Settings_CS` at spawn?
- Per-tank keys: `Tank_Prop_SO.Tank_Name` is stable ("Tiger I"), the GameObject name is not
  ("Tiger-I_Late_ST(Clone)").

## Backlog

- **Restore `cost_scale`** - scale `Tank_Prop_SO.Tank_Tier_Costs` so unlocks are cheaper. Existed in the
  old standalone tuner, lost in the consolidation.
- **Restore `allow_manual_respawn`** - one-line toggle on
  `General_Settings_CS.Allow_Manual_Respawn`. Also lost in the consolidation.
- **Expose the two AI knobs** that exist in the config but have no menu row:
  `ai_visibility_scale`, `ai_combat_speed_scale`.
- **Vehicle dump improvements** - include `Tank_Prop_SO` for every loaded tank (not just spawned ones), and
  an option to dump automatically on every scene change instead of by button.
- **Realism preset** - one switch that writes the whole per-tank table above.

## Known limitations

- **The Tuning tab's six scales have no visible effect on gameplay.** They scale the static
  `Tuning_Value` multiplier tables, but the game copies those into each vehicle at spawn
  (`Special_Settings_CS` is `[DefaultExecutionOrder(-1)]` and pushes values in `Start()` and on respawn).
  Changing the tables mid-mission is a no-op. Use the Vehicle tab instead.
- Vehicle overrides are **global** (one multiplier per axis, optionally player-only). No per-tank yet -
  that is the item at the top of this file.
- Commander seats are per tank (`camera.per_tank`); the rest of the camera settings are global.
- Vehicle override *feel* has not been measured against the HUD speed readout - only the field values
  have been verified (the dump shows Max_Speed/Torque scaling exactly by the configured factors).

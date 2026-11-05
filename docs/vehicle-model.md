# How the game drives a tank

Everything here was read out of `GameAssembly.dll` (game 1.2.0, Unity 6000.2.10f1, IL2CPP
metadata v31) with Il2CppDumper (`dump.cs` for the field offsets) plus Capstone over the method
bodies, and then confirmed by running the game with the mod attached and reading its own
speedometer. It is the reference for the **Vehicle** tab.

Field offsets are from this exact build. A game update invalidates them; the mod reads fields by
name through the interop assemblies, so it keeps working as long as the names stay.

## The scripts involved

| Type | Role |
|---|---|
| `Drive_Control_CS` | per-tank drive: speed, torque, ramping, anti-slip |
| `Drive_Control_Input_01_Player_CS` | keyboard/gamepad input -> L/R input rates |
| `Drive_Control_Input_99_AI_CS` | AI input |
| `Drive_Wheel_Parent_CS` | one per wheel group: turns drive torque into a wheel speed cap |
| `Drive_Wheel_CS` | one per road wheel: pushes `maxAngularVelocity` and `AddRelativeTorque` at the Rigidbody |
| `Turret_Horizontal_CS` | turret traverse |
| `Cannon_Vertical_CS` | gun elevation |
| `Special_Settings_CS` | applies the Tuning multipliers to the tank at spawn/respawn |
| `Tuning_Value` | the static multiplier tables the Tuning screen indexes into |

## Top speed: `maxAngVelocity`, not `Max_Speed`

`Drive_Wheel_Parent_CS.Initialize()` (offset `0x3CD850`) runs once, at Start:

    maxAngVelocity (0x40) = Max_Speed (Drive_Control 0x24) / (2 * Radius) * k

Every frame `Control_Velocity_And_Torque()` then sets

    Max_Angular_Velocity (0x2C) = Speed_Rate (Drive_Control 0xA8) * maxAngVelocity

and `Drive_Wheel_CS.FixedUpdate()` copies that to each wheel's `Rigidbody.maxAngularVelocity`.

So the top speed is **`maxAngVelocity * wheel radius`**, and `maxAngVelocity` is *private and
cached*. Writing `Max_Speed` on a tank that has already spawned does not move the cap - the wheel
parents have to recompute it (the mod calls `Initialize()` again; if that ever fails it scales the
stored value by the speed ratio instead).

Measured in game, T-34/76 with `max_speed = 14.7` (53 km/h):

    radius 0.45 -> maxAngVelocity 32.67 rad/s -> 14.70 m/s
    radius 0.35 -> maxAngVelocity 42.00 rad/s -> 14.70 m/s
    radius 0.30 -> maxAngVelocity 49.00 rad/s -> 14.70 m/s

and the HUD settled at 51-52 km/h, where the stock tank is pinned to 40.

`Max_Speed` is also the normaliser for `Current_Velocity / Max_Speed` (the "speed rate" the
anti-slip, downforce and speed-sync code use), which is why it still has to be written.

## Acceleration: three flags and a cached rate

`Drive_Control_CS.Set_Driving_Values()` (offset `0x3CBC80`) decides how the input becomes
`Speed_Rate`:

    if (!Acceleration_Flag (0x38))          -> rates take the raw input, instantly
    else {
        if (Sync_Speed_Rate (0x80)) Synchronize_Speed_Rate();
        left  = Calculate_Speed_Rate(L_Input_Rate, left);
        right = Calculate_Speed_Rate(R_Input_Rate, right);
    }

`Calculate_Speed_Rate` steps toward the input by `fixedDeltaTime * acceleRate`, and `acceleRate`
(0xE4) / `deceleRate` (0xE8) are private fields computed **once** in `Initialize()` as
`1 / Acceleration_Time` and `1 / Deceleration_Time`.

Consequences, all measured:

- `Torque` changes **nothing** on flat ground. 2100 vs 3000 gave identical readings
  (15 / 33 / 48 / 52 km/h at 2 / 4 / 6 / 10 s). The wheels sit at their angular-velocity cap and
  the hull is pulled by traction, so extra torque only spins them.
- Writing `Acceleration_Time` alone changes nothing either - the flag and the cached rate win.
- With `Acceleration_Flag = true`, `Sync_Speed_Rate = false` and
  `acceleRate = 1/Acceleration_Time`, the ramp appears:

| `accel_time` | 5 s | 12 s | 24 s | ramp finished |
|---|---|---|---|---|
| 8 s (stock) | 33 km/h (at 4 s) | - | - | ~7 s |
| 24 s | 12 km/h | 29 km/h | 52 km/h | ~22 s |

`Torque` is still worth keeping: `Limit_Torque()` recomputes `Torque = defaultTorque * (1 - slope
factor)` from the private `defaultTorque` (0xE0), so it is what a tank pulls with on a slope.
`Limit_Torque` has no direct `call` site in the binary - the compiler inlined it - but the live
value drifts from what was written every frame, which is how we know it runs. Writing
`defaultTorque` (not just `Torque`) is what makes a torque change stick.

## Turret traverse and gun elevation

`Turret_Horizontal_CS.Manual_Turn()` (offset `0x3E5250`):

    angleY (0x5C) += fixedDeltaTime * Speed_Mag (0x2C) * Turn_Rate (0x6C)

`Speed_Mag` is degrees per second - that is the traverse knob. `Turn_Rate` is the ramped
**input** (+/-1), not a rate: `Aiming_Control_CS.Turret_Turn_Rate` feeds it. Scaling that field
therefore does not slow the turret down, it lets the input overshoot 1 and turns the turret faster
than `Speed_Mag`. The old version of this mod scaled it by mistake; 1.6.0 leaves it alone.

`Cannon_Vertical_CS.Speed_Mag` (0x28) is the same thing for elevation.

## The Tuning multipliers are applied at spawn, not live

`Special_Settings_CS.Send_Special_Settings()` (offset `0x3DE090`) multiplies, in order:

    Max_Speed      *= Tuning_Value.Drive_Speed_Multipliers[index]
    Torque         *= same multiplier * maxAdjustRate
    Downforce      *= ...
    Turret Speed   *= Tuning_Value.Turret_Speed_Multipliers[index]
    ...

It is called from `Start()` and from `Respawned()`, which is why editing the Tuning arrays during
a mission does nothing and why every override in this mod is re-written on a timer instead of once.

## Reproducing this

- `dump.cs` + `global-metadata.dat` from the game, then Capstone on `GameAssembly.dll`
  (`Offset:` from the dump is the file offset).
- To measure a change: start the tutorial mission (the player tank is a T-34/76 there), edit
  `UserData\TBC_Menu.json`, press **Reload from disk** in the menu, hold W, and read the
  `Automatic NN km/h` readout bottom-right. The mod prints every applied value to
  `MelonLoader\Latest.log` and **Dump vehicle values** writes the live fields to
  `UserData\TBC_Vehicles.json`.

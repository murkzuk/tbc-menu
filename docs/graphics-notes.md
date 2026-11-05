# Graphics notes - what is actually happening

Measured in game on Tank Battle Classic 1.2.0 / Unity 6000.2.10f1, IL2CPP build.

## Tooling reality check

- The build is **IL2CPP** with **metadata v31**. **BepInEx 6.0.0-be.651 does not work**: its bundled
  Cpp2IL only understands metadata versions 24-29 and the game aborts on boot with
  `Unsupported metadata version found! We support 24-29, got 31`.
- **MelonLoader 0.7.3 does work** (Cpp2IL 2022.1.0-pre-release.21 + Il2CppInterop 1.5.1). It
  generates 97 interop assemblies and mods load normally.
- Il2CppInterop prefixes game namespaces, so `ChobiAssets.PTM` becomes `Il2CppChobiAssets.PTM` when
  compiling against the generated assemblies.
- Serialized assets have **stripped type trees**, so editing `sharedassets*` / `level*` directly
  needs a typetree source. (The TankBattleMissionEditor project solves this with
  TypeTreeGeneratorAPI, generated from GameAssembly.dll + global-metadata.dat.)
- **Unity stripped the IMGUI module** - the game never uses OnGUI, so `GUI` / `GUILayout` calls
  fail at runtime with "Method unstripping failed". Any overlay UI has to be uGUI.

## Why the game looks the way it does

Three separate things stack up:

1. **The 3D view is rendered into a fixed 854x480 RenderTexture** named `Render Texture (Main)`
   (ARGB32, depth 32, **Point** filter). Two cameras render into it:
   `Camera (Background)` (depth -10) and `Camera (Main)` (depth 1). A single **RawImage** on
   `Canvas (Main RT)` (ScreenSpaceOverlay, CanvasScaler ConstantPixelSize, reference 640x480)
   stretches that buffer over the window.
2. **A global post volume** `Post_Effect_Volume` with profile `DefaultVolumeProfile (for Scene)`
   carrying **film grain 0.5 (Thin1, response 0.8)**, **chromatic aberration 0.15** and
   **vignette 0.2**. Nothing else - no bloom, no tonemapping, no colour grading.
3. **Fog**, exponential, density 0.0005 in menus and 0.0010 in missions, warm colour
   (1.0, 0.961, 0.882).

The art itself is full resolution: the PolygonWar albedos are 2048x2048 bilinear. Only some props
(cobblestone 128x128, wheel maps 256x256) use Point filtering.

## URP settings as shipped

| Setting | Stock |
|---|---|
| `msaaSampleCount` | 1 (off) |
| `mainLightShadowmapResolution` | 1024 |
| `shadowCascadeCount` / `shadowDistance` | 3 / 128 m |
| soft shadows | supported, quality Low |
| additional lights | PerPixel, max 1, shadows unsupported |
| colour grading | LowDynamicRange, LUT 32 |
| `renderScale` | 1.0 |
| HDR | on, 32-bit |
| renderer features | 0 (no SSAO, no decals) |
| camera post-processing | on for Camera (Main), AA none, dithering off |

Post-process components are not baked into the profile, but all of URP's are creatable at
runtime: `ScriptableObject.CreateInstance<T>()` + `profile.components.Add()` works for Bloom,
Tonemapping, ColorAdjustments, WhiteBalance, DepthOfField, MotionBlur and LensDistortion, and
their shaders (`Hidden/Universal Render Pipeline/Bloom`, `UberPost`, `LutBuilder`, ...) are present in the
build.

## The camera stack

Three modes only - third person, gun sight, map:

- `Camera_Manager_CS` (singleton) holds `Camera_Mode`, `Available_Gun_Camera` and references to the
  pivot / gun / map / obstacle scripts. `Switch_Camera_Mode(bool)` is private; `Set_Camera_Points` is public.
- `Camera_Points_Manager_CS` is per tank and holds the authored `mainCameraPoint` and `gunCameraPoint`.
- `Camera_Pivot_CS` follows the body in `LateUpdate` (`Set_Camera_Point`, `Switch_Main_Camera`);
  `Camera_Rotation_CS` rotates the pivot (`Rotate_TPV`, static `Instance`); `Camera_Avoid_Obstacle_CS`
  sphere-casts to pull the camera in; `Camera_Zoom_CS` owns `minFOV`/`maxFOV`.
- `Gun_Camera_CS` is the gunner sight with its own reticle canvases and `Switch_Gun_Camera(bool, bool)`.

There is no first-person or commander mode. Adding one is trickier than it looks: the turret is
**slaved to the camera's forward direction**, so parenting the camera to the turret makes the turret
chase its own child and spin forever. The working approach is to leave the camera's parent alone,
place an observer point on `Turret_Base` and drive the camera's world pose each `LateUpdate` while
`Camera_Pivot_CS`, `Camera_Rotation_CS`, `Camera_Avoid_Obstacle_CS` and `Camera_Zoom_CS` are suspended.
Muting `Aiming_Control_CS` additionally gives free look with the gun staying put.

## The game already ships a vehicle tuning screen

The mission menu has a **Player Vehicle Tuning** panel with exactly six multipliers, plus an
**Experimental** submenu: **Firepower, Armor, Speed, Gun Traverse, Reload Speed, Shell Velocity**
(each shown as `x1.0` by default) and a Score Multiplier. These map one-to-one onto
`Tuning_Value`'s six tables, so the mod's Tuning tab is not inventing a system - it scales the same
curves without having to back out to the mission menu.

## Things that are not worth trying

- **SSAO** - the renderer has zero features and the SSAO shaders are not in the loaded set.
- **Additional light shadows** - `supportsAdditionalLightShadows = false`, one per-pixel light.
- **HDR colour grading** - the offscreen buffer is ARGB32 LDR; HDR grading needs a float buffer.
- **Light layers** - disabled in the asset.

## Other useful facts

- The game has **no resolution setting at all**; nothing in game code calls `Screen.SetResolution`
  at startup. `Full_Screen_Manager_CS` only reads the window size in `Awake` and binds **F11** to toggle
  fullscreen.
- `Fixed_Aspect_Camera_CS` letterboxes the 3D camera to a fixed `targetAspect` (1.7778).
- `General_Settings_CS` exposes static handles to live save data (`Current_Player_Data`,
  `Current_Option_Data`, `Allow_Manual_Respawn`) and the render buffer constants
  (`Render_Texture_Width` / `Height`).
- `Tuning_Value` is a static class of `float[]` multiplier tables (attack, defence, drive, turret,
  reload, bullet) indexed by the `*_Max_Index` values in `save.json`.
- Save data is plain JSON at `%USERPROFILE%\AppData\LocalLow\Chobi Softwear\Tank Battle Classic\save.json`,
  with `save_backup.json` beside it.
- The game uses legacy `UnityEngine.Input` for key polling (F11, and the menu confirm keys), so
  `Input.GetKeyDown` works in mods even though the Input System package is present.
## Aiming is tied to the buffer constants (found 2026-11-05)

`Aiming_Control_CS.Cast_Ray_Free(screenPos)` casts with `Camera.main.ScreenPointToRay`. The screen
position is the reticle's, and the game converts it into buffer pixels using
`General_Settings_CS.Render_Texture_Width` / `Render_Texture_Height` - **static fields initialised
to 854x480**, not read from the actual surface. Consequences, all confirmed in game:

| change | result |
|---|---|
| stock (buffer 854x480) | aim correct |
| resize the buffer to 1920x1080 | gun trails left/low of the crosshair, worse with distance from centre |
| render cameras to the screen, buffer canvas hidden, constants left alone | **same error** |
| render to the screen **and** set the constants to 1920x1080 | aim correct, picture sharp |

So any change to where the 3D is drawn has to be mirrored in those two fields. They are
`static readonly` in metadata but Il2CppInterop exposes them as writable, so the mod sets them
when `resolution.bypass_buffer` is on and restores them when it is turned off.

Confirmed end to end rather than by eye: a gun-sight kill at **247 m**, one shot, hit angle
39.1 deg, `Penetration 60.6/30.0 mm`, with the constants at 1920x1080 and the game's own results
screen recording `Hit_Ratio 100.0`. A scale error in those constants is metres wide at that range.

## Every camera that takes the buffer has to be covered (2026-11-05, fixed in 1.7.1)

The buffer is shared by more cameras than the two that are always running. Measured with the F8
geometry dump, `bypass_buffer` on, mod 1.7.0:

| state | camera | target before the fix | what it looked like |
|---|---|---|---|
| third person | `Camera (Background)`, `Camera (Main)` | screen (redirected) | correct, sharp |
| gun sight (right mouse) | `Gun_Camera`, fov 5.6 | **854x480** | dark scope disc, reticle, **no world** |
| map (middle mouse) | `Camera (Map)`, fov 30 | **854x480** | **blank screen** |

Both of those cameras are inactive until their mode is switched on, and an inactive camera is not
in `Camera.allCameras` - so a bypass applied once at scene load never sees them, and with the
canvas that displays the buffer hidden there is nothing left to show. 1.7.1 runs the redirect
every frame in `LateUpdate` instead and resets its tracking per scene, so the log names each
camera as it is caught:

    buffer bypass: camera 'Gun_Camera' now draws to the screen (had the 854x480 buffer), 3 redirected

Verified in the same mission: gun sight and map both render at native resolution, and the map's
markers stay on their tanks.

**Left over:** the buffer is found through the full-screen RawImage that displays it, and a
briefing screen has a second RawImage holding the **256x256 minimap** RenderTexture, so in that
scene the wrong texture can be picked (log: `camera 'Camera (Map)' ... (had the 256x256 buffer)`).
Battle scenes are unaffected. The robust identification is "the RenderTexture the world cameras
render into", i.e. never one below about 512 px.

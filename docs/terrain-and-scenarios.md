# Terrain, maps and custom scenarios

Notes from building a custom mission for Tank Battle Classic 1.2.0 by hand. The scenario work
used fierman's [TankBattleMissionEditor](https://github.com/fiermanb/TankBattleMissionEditor)
(GPL-3.0) - these are our own findings about the game, not its code.

## What a map is

Loose Unity SerializedFiles (no AssetBundles): `levelN` scenes, `sharedassetsN.assets` (+ `.resS`),
`globalgamemanagers` holding the scene list, `level2`/`sharedassets2.assets` holding the mission
catalogue. Art is full resolution; MonoBehaviour type trees are stripped, which is why any tool that
edits these files has to regenerate the field layouts from `GameAssembly.dll` plus
`global-metadata.dat` and validate them per object.

## The six terrains, measured

Height sampled from each `TerrainData` (1472 MB of assets, 1025^2 or 2049^2 int32 heights):

| terrain | extent | relief | mean slope | >3 deg | variance below 300 m |
|---|---|---|---|---|---|
| Mountain_01 | 1024 m | 15.6 m | 9.32 deg | 56.5 % | 84.9 % |
| City_03 | 1024 m | 25.4 m | 3.44 deg | 20.4 % | 43.0 % |
| Town_01 | 1024 m | 31.2 m | 4.09 deg | 28.7 % | 16.0 % |
| Forest_01 | 1024 m | 4.4 m | 1.20 deg | 4.9 % | 11.1 % |
| Open Field 01 | 2048 m | 9.4 m | 0.75 deg | 3.3 % | **2.4 %** |
| Tutorial_01 | 1024 m | 0.0 m | 0.00 deg | 0 % | 0 % |

Two archetypes only: flat plates (open field, forest, tutorial) and flat plates cut by linear
features (mountain gullies, city/town street cuts). The open field reads as a plain because 43.9 %
of its variance is at wavelengths longer than the map itself and only 2.4 % is under 300 m.

## The terrain size is two floats, not baked art

`m_Heightmap.m_Scale` is `(cell_size_x, height_range_y, cell_size_z)`; world extent is
`(resolution - 1) * scale.x` by `(resolution - 1) * scale.z`. Changing x/z **stretches** the
heightmap: features get wider and every slope divides by the stretch factor, at zero art cost.
In the shipped Mountain terrain the triple `1.0, 128.0, 1.0` appears exactly once, so a 2.5x
stretch is an 8-byte patch - much safer than a UnityPy round-trip, which shrank a 15,286,000 byte
asset to 3,354,272 bytes in testing.

Stretching alone is not enough: the gully walls survive as long ramps (mean 4.5 deg but p95 29 deg
and max 55 deg after a 2.5x stretch), so the heights also need a light blur and an amplitude scale.
Measured band for "reads as rolling hills, not a plain" in this game's camera: **~2.5 deg mean slope
with ~9 m of relief over 2.5 km**. At 1.9 deg / 7 m the ground reads as flat.

## Custom scenarios, end to end

```
create from a mission         copies menu + battle scene, clones the mission asset,
                              registers both in BuildSettings, adds a Custom Missions row
give it its own terrain       the scenario gets a copy of the TerrainData + splat textures,
                              otherwise you would edit a terrain five stock missions share
stretch / reshape             patch m_Scale, and/or write new heights
content                       spawns are events with Event_Type 0; WayPoint_Pack points at the
                              pack GameObject; timer events are Trigger_Type 0 + Trigger_Time
rebuild the AI route map      required after terrain or object changes, else AI pathing is wrong
```

Verified: a 2.5x stretched, blurred Mountain terrain plays as rolling country, a four-waypoint patrol
kept an AI tank moving at ~11.8 m/s, and an 8 s timer message fired on schedule.

A game update replaces `level*`/`sharedassets*` and deletes custom scenarios; both the editor and
this workflow keep originals in a backups folder so they can be restored.

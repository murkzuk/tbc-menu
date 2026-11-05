// Vehicle overrides: drive the real per-tank values instead of the tuning tables.
// The Tuning_Value multiplier arrays are consumed once when a tank initialises, so
// scaling them later has no visible effect - these fields are what the drive and
// turret logic actually read while driving.
//
// What the game reads, per axis (from GameAssembly.dll 1.2.0, see docs/vehicle-model.md):
//
//   top speed  Drive_Wheel_Parent_CS.Max_Angular_Velocity = Speed_Rate * maxAngVelocity, and
//              Drive_Wheel_CS copies that to every wheel's Rigidbody.maxAngularVelocity.
//              maxAngVelocity is private and is computed ONCE, in Initialize(), from
//              Drive_Control_CS.Max_Speed / (2 * Radius). Writing Max_Speed alone therefore
//              does not move the top speed of a tank that has already spawned - the wheel
//              parents have to recompute it, which is what ApplyWheelCap() asks them to do.
//   accel      Drive_Control_CS.Torque -> Left_Torque/Right_Torque -> AddRelativeTorque
//              on the drive wheels. Torque does not cap speed; it only sets how hard the
//              tank pulls, so it is the acceleration knob.
//   traverse   Turret_Horizontal_CS.Speed_Mag, degrees per second. (Aiming_Control_CS.
//              Turret_Turn_Rate is the -1..1 input, not a rate, so it is left alone;
//              scaling it only overdrives the turret past Speed_Mag.)
//   elevation  Cannon_Vertical_CS.Speed_Mag, degrees per second.
//
// Special_Settings_CS re-applies the tuning multipliers to Max_Speed and Torque on
// spawn and on respawn, so every value here is written as an absolute target on a
// timer rather than as a one-off edit.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using Il2CppChobiAssets.PTM;

namespace TbcMenu
{
    public partial class MenuMod
    {
        // first-seen ("game") values for one spawned tank, so every override is computed
        // from the original rather than from whatever the previous tick wrote.
        private class DriveSnap
        {
            public float MaxSpeed, Torque, DefaultTorque, AccelTime, DecelTime, AppliedTorque;
            public float AcceleRate, DeceleRate;
            public bool AccelFlag, SyncSpeedRate;
            public float AppliedSpeed = float.NaN;
            public readonly List<Drive_Wheel_Parent_CS> Wheels = new List<Drive_Wheel_Parent_CS>();
        }

        private readonly Dictionary<int, DriveSnap> _driveSnaps = new Dictionary<int, DriveSnap>();
        private readonly Dictionary<int, float> _turretSpeedMag = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _cannonSpeedMag = new Dictionary<int, float>();

        // the tank being edited in the Vehicle tab
        private readonly List<string> _vehEditNames = new List<string>();
        private int _vehEditIndex;

        // ---------------------------------------------------------------- public bits

        // Writes every spawned vehicle's real drive and traverse values to UserData/TBC_Vehicles.json
        private void DumpVehicles()
        {
            try
            {
                var driveRows = new List<string>();
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null) continue;
                    string name = TankNameOf(drive.transform);
                    string spec = "";
                    try
                    {
                        var ids = drive.GetComponentInParent<ID_Settings_CS>();
                        if (ids != null && ids.Tank_Prop != null)
                            spec = string.Format(", specSpeed={0} specArmor={1} specPen={2} specReload={3}",
                                                  ids.Tank_Prop.Speed, ids.Tank_Prop.Armor, ids.Tank_Prop.Pen, ids.Tank_Prop.Reload);
                    }
                    catch { }

                    float mass = -1f;
                    try { var rb = drive.GetComponentInParent<Rigidbody>(); if (rb != null) mass = rb.mass; } catch { }

                    // world position, so movement can be measured from the dump alone
                    Vector3 pos = Vector3.zero;
                    try { pos = drive.transform.position; } catch { }

                    driveRows.Add("  { tank: \"" + name + "\", kind: \"drive\", maxSpeed_mps: " + drive.Max_Speed.ToString("F3") +
                                  ", torque: " + drive.Torque.ToString("F1") +
                                  ", defaultTorque: " + drive.defaultTorque.ToString("F1") +
                                  ", mass_kg: " + mass.ToString("F0") +
                                  ", accel_s: " + drive.Acceleration_Time.ToString("F2") +
                                  ", decel_s: " + drive.Deceleration_Time.ToString("F2") +
                                  ", speedRate: " + drive.Speed_Rate.ToString("F3") +
                                  ", velocity_mps: " + drive.Current_Velocity.ToString("F3") +
                                  ", posX: " + pos.x.ToString("F2") + ", posZ: " + pos.z.ToString("F2") +
                                  ", accelFlag: " + (drive.Acceleration_Flag ? "on" : "off") +
                                  ", syncSpeedRate: " + (drive.Sync_Speed_Rate ? "on" : "off") +
                                  ", torqueLimitter: " + (drive.Torque_Limitter ? "on" : "off") +
                                  ", antiSlip: " + (drive.Use_AntiSlip ? "on" : "off") + spec + " }");
                }

                foreach (var wp in UnityEngine.Object.FindObjectsOfType<Drive_Wheel_Parent_CS>())
                {
                    if (wp == null) continue;
                    driveRows.Add("  { tank: \"" + TankNameOf(wp.transform) + "\", kind: \"wheelParent\", radius_m: " +
                                  wp.Radius.ToString("F3") + ", maxAngVelocity: " + wp.maxAngVelocity.ToString("F2") +
                                  ", maxAngularVelocity: " + wp.Max_Angular_Velocity.ToString("F2") +
                                  ", brakeTurn: " + (wp.Use_BrakeTurn ? "yes" : "no") + " }");
                }

                foreach (var tur in UnityEngine.Object.FindObjectsOfType<Turret_Horizontal_CS>())
                {
                    if (tur == null) continue;
                    driveRows.Add("  { tank: \"" + TankNameOf(tur.transform) + "\", kind: \"turret\", speedMag_degps: " + tur.Speed_Mag.ToString("F2") +
                                  ", accel_s: " + tur.Acceleration_Time.ToString("F2") +
                                  ", decel_s: " + tur.Deceleration_Time.ToString("F2") +
                                  ", maxLeft_deg: " + tur.Max_Left.ToString("F1") +
                                  ", maxRight_deg: " + tur.Max_Right.ToString("F1") + " }");
                }

                foreach (var cv in UnityEngine.Object.FindObjectsOfType<Cannon_Vertical_CS>())
                {
                    if (cv == null) continue;
                    driveRows.Add("  { tank: \"" + TankNameOf(cv.transform) + "\", kind: \"cannon\", speedMag_degps: " + cv.Speed_Mag.ToString("F2") +
                                  ", maxElevation_deg: " + cv.Max_Elevation.ToString("F1") +
                                  ", maxDepression_deg: " + cv.Max_Depression.ToString("F1") + " }");
                }

                // every vehicle the build knows about, with the exact Tank_Name key the
                // per-tank table needs - available outside a mission too.
                var typeRows = new List<string>();
                try
                {
                    foreach (var prop in Resources.FindObjectsOfTypeAll<Tank_Prop_SO>())
                    {
                        if (prop == null) continue;
                        string nm = null;
                        try { nm = prop.Tank_Name; } catch { }
                        if (string.IsNullOrEmpty(nm)) continue;
                        typeRows.Add("  { name: \"" + nm + "\", speed_kmh: " + prop.Speed + ", armor: " + prop.Armor +
                                     ", pen: " + prop.Pen + ", reload: " + prop.Reload + " }");
                    }
                }
                catch (Exception e) { typeRows.Add("  { error: \"" + e.Message.Replace("\"", "'") + "\" }"); }

                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                string json = "{\n  scene: \"" + scene + "\",\n" +
                              "  note: \"speeds in metres per second (1 unit = 1 m assumed); traverse and elevation in degrees per second\",\n" +
                              "  entries: [\n" + string.Join(",\n", driveRows) + "\n  ],\n" +
                              "  tankTypes: [\n" + string.Join(",\n", typeRows) + "\n  ]\n}\n";
                string path = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "TBC_Vehicles.json");
                File.WriteAllText(path, json);
                LoggerInstance.Msg("vehicle dump -> " + path + " (" + driveRows.Count + " spawned parts, " +
                                   typeRows.Count + " tank types)");
            }
            catch (Exception e) { LoggerInstance.Warning("vehicle dump failed: " + e.Message); }
        }

        private string TankNameOf(Transform t)
        {
            try
            {
                var ids = t.GetComponentInParent<ID_Settings_CS>();
                if (ids != null)
                {
                    try
                    {
                        if (ids.Tank_Prop != null && !string.IsNullOrEmpty(ids.Tank_Prop.Tank_Name)) return ids.Tank_Prop.Tank_Name;
                    }
                    catch { }
                    string n0 = ids.gameObject.name;
                    int k0 = n0.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
                    return k0 > 0 ? n0.Substring(0, k0).Trim() : n0;
                }
            }
            catch { }
            string n = t.root.name;
            int k = n.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            return k > 0 ? n.Substring(0, k).Trim() : n;
        }

        private Transform TankRootOf(Transform t)
        {
            try
            {
                var ids = t.GetComponentInParent<ID_Settings_CS>();
                if (ids != null) return ids.transform;
            }
            catch { }
            return t.root;
        }

        private Transform PlayerTankRoot()
        {
            try
            {
                foreach (var p in UnityEngine.Object.FindObjectsOfType<Drive_Control_Input_01_Player_CS>())
                {
                    if (p == null) continue;
                    return TankRootOf(p.transform);
                }
            }
            catch { }
            return null;
        }

        private void ResetVehicleSnaps()
        {
            _driveSnaps.Clear();
            _turretSpeedMag.Clear();
            _cannonSpeedMag.Clear();
        }

        // ---------------------------------------------------------------- apply

        private void ApplyVehicleOverrides()
        {
            var v = _cfg.vehicle;
            if (v == null) return;
            if (!v.enabled) return;

            bool perTank = v.per_tank != null && v.per_tank.Count > 0;
            bool global = Math.Abs(v.speed_scale - 1f) > 0.001f
                       || Math.Abs(v.torque_scale - 1f) > 0.001f
                       || Math.Abs(v.traverse_scale - 1f) > 0.001f;
            if (!perTank && !global) return;

            try
            {
                Transform playerRoot = null;
                if (v.player_only)
                {
                    playerRoot = PlayerTankRoot();
                    if (playerRoot == null) return;
                }

                var drives = UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>();
                var turrets = UnityEngine.Object.FindObjectsOfType<Turret_Horizontal_CS>();
                var cannons = UnityEngine.Object.FindObjectsOfType<Cannon_Vertical_CS>();
                var wheels = UnityEngine.Object.FindObjectsOfType<Drive_Wheel_Parent_CS>();

                foreach (var drive in drives)
                {
                    if (drive == null) continue;
                    if (v.player_only && !drive.transform.IsChildOf(playerRoot)) continue;
                    ApplyOneVehicle(drive, turrets, cannons, wheels);
                }
            }
            catch (Exception e) { LoggerInstance.Warning("vehicle override: " + e.Message); }
        }

        // Returns how many axes were written (0 = this tank needed nothing).
        private int ApplyOneVehicle(Drive_Control_CS drive, Turret_Horizontal_CS[] turrets,
                                    Cannon_Vertical_CS[] cannons, Drive_Wheel_Parent_CS[] wheels)
        {
            var v = _cfg.vehicle;
            string name = TankNameOf(drive.transform);
            TankVehicleCfg e = PerTankEntry(name);

            float speedScale = Mathf.Clamp(v.speed_scale, 0.05f, 5f);
            float torqueScale = Mathf.Clamp(v.torque_scale, 0.05f, 5f);
            float traverseScale = Mathf.Clamp(v.traverse_scale, 0.05f, 5f);
            float? absSpeed = null, absTraverse = null, absElevation = null;

            if (e != null)
            {
                if (e.torque_scale.HasValue) torqueScale = Mathf.Clamp(e.torque_scale.Value, 0.05f, 5f);
                if (e.max_speed.HasValue && e.max_speed.Value > 0f) absSpeed = e.max_speed.Value;
                if (e.traverse.HasValue && e.traverse.Value > 0f) absTraverse = e.traverse.Value;
                if (e.elevation.HasValue && e.elevation.Value > 0f) absElevation = e.elevation.Value;
            }

            Transform root = TankRootOf(drive.transform);
            int id = drive.GetInstanceID();
            if (!_driveSnaps.TryGetValue(id, out var snap) || snap == null)
            {
                snap = new DriveSnap
                {
                    MaxSpeed = drive.Max_Speed,
                    Torque = drive.Torque,
                    DefaultTorque = drive.defaultTorque,
                    AccelTime = drive.Acceleration_Time,
                    DecelTime = drive.Deceleration_Time,
                    AccelFlag = drive.Acceleration_Flag,
                    SyncSpeedRate = drive.Sync_Speed_Rate,
                    AcceleRate = drive.acceleRate,
                    DeceleRate = drive.deceleRate,
                };
                _driveSnaps[id] = snap;
            }

            int written = 0;

            // ---- speed + torque ------------------------------------------------
            float targetSpeed = Mathf.Clamp(absSpeed ?? snap.MaxSpeed * speedScale, 0.2f, 60f);
            float targetTorque = Mathf.Max(1f, snap.Torque * torqueScale);

            // Acceleration is not torque-limited in this game: the drive ramps Speed_Rate to 1.0
            // over Acceleration_Time and the wheel cap follows it, so the ramp - not the torque -
            // is what sets 0-100% time. Torque only matters when traction or a slope binds.
            float targetAccel = Mathf.Clamp(e != null && e.accel_time.HasValue ? e.accel_time.Value : snap.AccelTime, 0.5f, 120f);
            float targetDecel = Mathf.Clamp(e != null && e.decel_time.HasValue ? e.decel_time.Value : snap.DecelTime, 0.05f, 120f);

            bool speedDrifted = Mathf.Abs(drive.Max_Speed - targetSpeed) > 0.01f;
            bool torqueChanged = float.IsNaN(snap.AppliedTorque) || Mathf.Abs(snap.AppliedTorque - targetTorque) > 0.5f;
            drive.Max_Speed = targetSpeed;
            drive.Torque = targetTorque;
            try { drive.defaultTorque = targetTorque; } catch { }
            drive.Acceleration_Time = targetAccel;
            drive.Deceleration_Time = targetDecel;

            if (e != null && e.accel_time.HasValue)
            {
                // Three flags have to line up before Acceleration_Time does anything:
                //   Acceleration_Flag  - otherwise the input is applied instantly;
                //   Sync_Speed_Rate    - otherwise Speed_Rate is continuously re-synced to the
                //                        measured speed, which overrides the ramp entirely;
                //   acceleRate         - a private field cached from Acceleration_Time in
                //                        Initialize(), so writing the time alone is not enough.
                drive.Acceleration_Flag = true;
                drive.Sync_Speed_Rate = false;
                try
                {
                    drive.acceleRate = 1f / targetAccel;
                    drive.deceleRate = 1f / targetDecel;
                }
                catch { }
            }

            bool speedChanged = speedDrifted
                             || float.IsNaN(snap.AppliedSpeed)
                             || Mathf.Abs(snap.AppliedSpeed - targetSpeed) > 0.001f;
            if (speedChanged)
            {
                float prevSpeed = (float.IsNaN(snap.AppliedSpeed) || snap.AppliedSpeed <= 0.01f)
                                ? snap.MaxSpeed : snap.AppliedSpeed;
                ApplyWheelCap(root, snap, wheels, prevSpeed, targetSpeed);
                snap.AppliedSpeed = targetSpeed;
                written++;
            }
            if (torqueChanged) { snap.AppliedTorque = targetTorque; written++; }

            if (speedChanged || torqueChanged)
            {
                LoggerInstance.Msg($"vehicle '{name}': speed {snap.MaxSpeed:F2} -> {targetSpeed:F2} m/s " +
                                   $"({targetSpeed * 3.6f:F0} km/h), torque {snap.Torque:F0} -> {targetTorque:F0}" +
                                   $", accel {snap.AccelTime:F1} -> {targetAccel:F1} s" +
                                   (speedChanged ? $", wheelCap {WheelCapOf(snap):F2}" : ""));
            }

            // ---- turret traverse ----------------------------------------------
            if (absTraverse.HasValue || Math.Abs(traverseScale - 1f) > 0.001f)
            {
                foreach (var tur in turrets)
                {
                    if (tur == null || !tur.transform.IsChildOf(root)) continue;
                    int tid = tur.GetInstanceID();
                    if (!_turretSpeedMag.TryGetValue(tid, out float orig))
                    {
                        orig = tur.Speed_Mag;
                        _turretSpeedMag[tid] = orig;
                    }
                    float target = Mathf.Clamp(absTraverse ?? orig * traverseScale, 0.5f, 180f);
                    if (Mathf.Abs(tur.Speed_Mag - target) > 0.001f) tur.Speed_Mag = target;
                }
            }

            // ---- gun elevation -------------------------------------------------
            if (absElevation.HasValue)
            {
                foreach (var cv in cannons)
                {
                    if (cv == null || !cv.transform.IsChildOf(root)) continue;
                    int cid = cv.GetInstanceID();
                    if (!_cannonSpeedMag.ContainsKey(cid)) _cannonSpeedMag[cid] = cv.Speed_Mag;
                    float target = Mathf.Clamp(absElevation.Value, 0.5f, 120f);
                    if (Mathf.Abs(cv.Speed_Mag - target) > 0.001f) cv.Speed_Mag = target;
                }
            }

            return written;
        }

        // Every wheel parent derives its cap from Max_Speed once, in Initialize(). Re-run it
        // whenever the target speed changes, otherwise the tank keeps its old top speed.
        private void ApplyWheelCap(Transform root, DriveSnap snap, Drive_Wheel_Parent_CS[] wheels,
                                   float prevSpeed, float targetSpeed)
        {
            try
            {
                if (snap.Wheels.Count == 0)
                    foreach (var wp in wheels)
                        if (wp != null && wp.transform.IsChildOf(root)) snap.Wheels.Add(wp);

                // the cap is linear in Max_Speed, so a ratio is enough when Initialize() is
                // unavailable - prevSpeed is whatever the cap was last built from.
                float ratio = prevSpeed > 0.01f ? targetSpeed / prevSpeed : 1f;

                foreach (var wp in snap.Wheels)
                {
                    if (wp == null) continue;
                    float before = wp.maxAngVelocity;
                    bool ok = false;
                    try { wp.Initialize(); ok = wp.maxAngVelocity > 0.0001f; } catch { }
                    if (!ok) { try { wp.maxAngVelocity = before * ratio; } catch { } }
                }
            }
            catch (Exception e) { LoggerInstance.Warning("wheel cap: " + e.Message); }
        }

        private float WheelCapOf(DriveSnap snap)
        {
            foreach (var wp in snap.Wheels)
                if (wp != null) return wp.maxAngVelocity;
            return 0f;
        }

        private TankVehicleCfg PerTankEntry(string tankName)
        {
            var v = _cfg.vehicle;
            if (v == null || v.per_tank == null || string.IsNullOrEmpty(tankName)) return null;
            return v.per_tank.TryGetValue(tankName, out var e) ? e : null;
        }

        // Puts back what the game had, so turning the tab off is not a one-way door.
        private void RestoreVehicleOriginals()
        {
            try
            {
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null) continue;
                    if (!_driveSnaps.TryGetValue(drive.GetInstanceID(), out var snap) || snap == null) continue;

                    drive.Max_Speed = snap.MaxSpeed;
                    drive.Torque = snap.Torque;
                    drive.Acceleration_Time = snap.AccelTime;
                    drive.Deceleration_Time = snap.DecelTime;
                    drive.Acceleration_Flag = snap.AccelFlag;
                    drive.Sync_Speed_Rate = snap.SyncSpeedRate;
                    try { drive.acceleRate = snap.AcceleRate; drive.deceleRate = snap.DeceleRate; } catch { }
                    try { drive.defaultTorque = snap.DefaultTorque; } catch { }

                    snap.AppliedSpeed = snap.MaxSpeed;
                    foreach (var wp in snap.Wheels) { if (wp == null) continue; try { wp.Initialize(); } catch { } }
                }

                foreach (var tur in UnityEngine.Object.FindObjectsOfType<Turret_Horizontal_CS>())
                {
                    if (tur == null) continue;
                    if (_turretSpeedMag.TryGetValue(tur.GetInstanceID(), out float orig)) tur.Speed_Mag = orig;
                }
                foreach (var cv in UnityEngine.Object.FindObjectsOfType<Cannon_Vertical_CS>())
                {
                    if (cv == null) continue;
                    if (_cannonSpeedMag.TryGetValue(cv.GetInstanceID(), out float orig)) cv.Speed_Mag = orig;
                }

                LoggerInstance.Msg("vehicle overrides disabled - original values restored");
                ResetVehicleSnaps();
            }
            catch (Exception e) { LoggerInstance.Warning("vehicle restore: " + e.Message); }
        }

        // ---------------------------------------------------------------- menu support

        private void RefreshEditTankList()
        {
            _vehEditNames.Clear();
            try
            {
                Transform playerRoot = PlayerTankRoot();
                if (playerRoot != null)
                {
                    string pn = TankNameOf(playerRoot);
                    if (!string.IsNullOrEmpty(pn)) _vehEditNames.Add(pn);
                }
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null) continue;
                    string n = TankNameOf(drive.transform);
                    if (!string.IsNullOrEmpty(n) && !_vehEditNames.Contains(n)) _vehEditNames.Add(n);
                }
            }
            catch { }

            if (_cfg.vehicle.per_tank != null)
                foreach (var kv in _cfg.vehicle.per_tank)
                    if (!string.IsNullOrEmpty(kv.Key) && !_vehEditNames.Contains(kv.Key)) _vehEditNames.Add(kv.Key);

            if (_vehEditNames.Count == 0) _vehEditNames.Add("none spawned");
            _vehEditIndex = Mathf.Clamp(_vehEditIndex, 0, _vehEditNames.Count - 1);
        }

        private string EditTankName =>
            _vehEditNames.Count == 0 ? "" : _vehEditNames[Mathf.Clamp(_vehEditIndex, 0, _vehEditNames.Count - 1)];

        private void CycleEditTank(int dir)
        {
            if (_vehEditNames.Count == 0) return;
            _vehEditIndex = (_vehEditIndex + dir + _vehEditNames.Count) % _vehEditNames.Count;
        }

        private TankVehicleCfg EditEntry(bool create)
        {
            var v = _cfg.vehicle;
            if (v.per_tank == null) v.per_tank = new Dictionary<string, TankVehicleCfg>();
            string key = EditTankName;
            if (string.IsNullOrEmpty(key) || key == "none spawned") return null;
            if (v.per_tank.TryGetValue(key, out var e) && e != null) return e;
            if (!create) return null;
            e = new TankVehicleCfg();
            v.per_tank[key] = e;
            return e;
        }

        // the tank's current in-game speed, used as a starting point for a new entry
        private float? GameMaxSpeedOf(string tankName)
        {
            try
            {
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null) continue;
                    if (TankNameOf(drive.transform) != tankName) continue;
                    if (_driveSnaps.TryGetValue(drive.GetInstanceID(), out var snap) && snap != null) return snap.MaxSpeed;
                    return drive.Max_Speed;
                }
            }
            catch { }
            return null;
        }

        // the tank's current in-game traverse rate, used as a starting point for a new entry
        private float? GameTraverseOf(string tankName)
        {
            try
            {
                foreach (var tur in UnityEngine.Object.FindObjectsOfType<Turret_Horizontal_CS>())
                {
                    if (tur == null) continue;
                    if (TankNameOf(tur.transform) != tankName) continue;
                    if (_turretSpeedMag.TryGetValue(tur.GetInstanceID(), out float orig)) return orig;
                    return tur.Speed_Mag;
                }
            }
            catch { }
            return null;
        }

        private float? GameAccelTimeOf(string tankName)
        {
            try
            {
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null || TankNameOf(drive.transform) != tankName) continue;
                    return _driveSnaps.TryGetValue(drive.GetInstanceID(), out var s) && s != null ? s.AccelTime : drive.Acceleration_Time;
                }
            }
            catch { }
            return null;
        }

        private float? GameDecelTimeOf(string tankName)
        {
            try
            {
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null || TankNameOf(drive.transform) != tankName) continue;
                    return _driveSnaps.TryGetValue(drive.GetInstanceID(), out var s) && s != null ? s.DecelTime : drive.Deceleration_Time;
                }
            }
            catch { }
            return null;
        }

        // live readout for checking an override against the speedometer instead of the log
        private string PlayerSpeedLabel()
        {
            try
            {
                Transform root = PlayerTankRoot();
                if (root == null) return "no tank";
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null || !drive.transform.IsChildOf(root)) continue;
                    string txt = (drive.Current_Velocity * 3.6f).ToString("F0") + " km/h";
                    if (_driveSnaps.TryGetValue(drive.GetInstanceID(), out var s) && s != null)
                        txt += "   target " + (drive.Max_Speed * 3.6f).ToString("F0") + " (game " +
                               (s.MaxSpeed * 3.6f).ToString("F0") + ")";
                    return txt;
                }
            }
            catch { }
            return "-";
        }

        // ---------------------------------------------------------------- realism preset

        // Road top speed (m/s), turret traverse (deg/s) and the time to reach that speed in
        // seconds. accel_time is the column that matters: measured in game, halving or doubling
        // Torque changes nothing on flat ground, because the drive ramps Speed_Rate over
        // Acceleration_Time and the wheels follow that ramp. Torque is left at the game value.
        private static readonly (string name, float mps, float traverse, float accel)[] RealismTable =
        {
            ("Pz. III N",   11.1f, 16f, 18f),
            ("Jagdpanther", 12.8f,  5f, 22f),
            ("T-34/76",     14.7f,  5f, 24f),
            ("Tiger I",     12.7f,  8f, 26f),
            ("Hetzer",      11.7f,  6f, 26f),
            ("Tiger II",    11.5f,  9f, 30f),
        };

        private void LoadRealismTable()
        {
            var v = _cfg.vehicle;
            if (v.per_tank == null) v.per_tank = new Dictionary<string, TankVehicleCfg>();
            foreach (var row in RealismTable)
            {
                v.per_tank[row.name] = new TankVehicleCfg
                {
                    max_speed = row.mps,
                    traverse = row.traverse,
                    accel_time = row.accel,
                };
            }
            ApplyVehicleOverrides();
            _status = "realism table written (" + RealismTable.Length + " tanks) - press Save";
            LoggerInstance.Msg("realism table: " + RealismTable.Length + " tank entries written to config");
        }
    }
}

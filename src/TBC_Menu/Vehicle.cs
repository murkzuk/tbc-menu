// Vehicle overrides: drive the real per-tank values instead of the tuning tables.
// The Tuning_Value multiplier arrays are consumed once when a tank initialises, so
// scaling them later has no visible effect - these fields are what the drive and
// turret logic actually read while driving.
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
        private class VehicleSnap
        {
            public float MaxSpeed, Torque, SpeedMag, TurnRate, TurretTurnRate, CannonTurnRate;
        }

        private readonly Dictionary<int, VehicleSnap> _vehSnaps = new Dictionary<int, VehicleSnap>();

        // Writes every spawned vehicle's real drive and traverse values to UserData/TBC_Vehicles.json
        private void DumpVehicles()
        {
            try
            {
                var rows = new List<string>();

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

                    rows.Add("  { tank: \"" + name + "\", kind: \"drive\", maxSpeed_mps: " + drive.Max_Speed.ToString("F3") +
                             ", torque: " + drive.Torque.ToString("F1") +
                             ", accel_s: " + drive.Acceleration_Time.ToString("F2") +
                             ", decel_s: " + drive.Deceleration_Time.ToString("F2") +
                             ", speedRate: " + drive.Speed_Rate.ToString("F3") + spec + " }");
                }

                foreach (var tur in UnityEngine.Object.FindObjectsOfType<Turret_Horizontal_CS>())
                {
                    if (tur == null) continue;
                    rows.Add("  { tank: \"" + TankNameOf(tur.transform) + "\", kind: \"turret\", speedMag: " + tur.Speed_Mag.ToString("F2") +
                             ", accel_s: " + tur.Acceleration_Time.ToString("F2") +
                             ", decel_s: " + tur.Deceleration_Time.ToString("F2") +
                             ", maxLeft_deg: " + tur.Max_Left.ToString("F1") +
                             ", maxRight_deg: " + tur.Max_Right.ToString("F1") + " }");
                }

                foreach (var aim in UnityEngine.Object.FindObjectsOfType<Aiming_Control_CS>())
                {
                    if (aim == null) continue;
                    rows.Add("  { tank: \"" + TankNameOf(aim.transform) + "\", kind: \"aiming\", turretTurnRate: " + aim.Turret_Turn_Rate.ToString("F3") +
                             ", cannonTurnRate: " + aim.Cannon_Turn_Rate.ToString("F3") +
                             ", turretSpeedMultiplier: " + aim.Turret_Speed_Multiplier.ToString("F3") +
                             ", leadMultiplier: " + aim.Lead_Multiplier.ToString("F3") + " }");
                }

                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                string json = "{\n  scene: \"" + scene + "\",\n  note: \"speeds in metres per second (1 unit = 1 m assumed)\",\n  entries: [\n" +
                              string.Join(",\n", rows) + "\n  ]\n}\n";
                string path = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "TBC_Vehicles.json");
                File.WriteAllText(path, json);
                LoggerInstance.Msg("vehicle dump -> " + path + " (" + rows.Count + " entries)");
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

        private void ApplyVehicleOverrides()
        {
            var v = _cfg.vehicle;
            if (v == null || !v.enabled) return;
            if (Math.Abs(v.speed_scale - 1f) < 0.001f
                && Math.Abs(v.torque_scale - 1f) < 0.001f
                && Math.Abs(v.traverse_scale - 1f) < 0.001f) return;

            try
            {
                Transform playerRoot = null;
                if (v.player_only)
                {
                    foreach (var p in UnityEngine.Object.FindObjectsOfType<Drive_Control_Input_01_Player_CS>())
                    {
                        if (p == null) continue;
                        var ids = p.GetComponentInParent<ID_Settings_CS>();
                        playerRoot = ids != null ? ids.transform : p.transform.root;
                        break;
                    }
                    if (playerRoot == null) return;
                }

                int n = 0;
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null) continue;
                    if (v.player_only && playerRoot != null && !drive.transform.IsChildOf(playerRoot)) continue;

                    int id = drive.GetInstanceID();
                    if (!_vehSnaps.TryGetValue(id, out var snap))
                    {
                        snap = new VehicleSnap { MaxSpeed = drive.Max_Speed, Torque = drive.Torque };
                        _vehSnaps[id] = snap;
                    }
                    drive.Max_Speed = Mathf.Max(0.1f, snap.MaxSpeed * Mathf.Clamp(v.speed_scale, 0.05f, 5f));
                    drive.Torque = Mathf.Max(1f, snap.Torque * Mathf.Clamp(v.torque_scale, 0.05f, 5f));
                    n++;
                }

                if (Math.Abs(v.traverse_scale - 1f) > 0.001f)
                {
                    foreach (var turret in UnityEngine.Object.FindObjectsOfType<Turret_Horizontal_CS>())
                    {
                        if (turret == null) continue;
                        if (v.player_only && playerRoot != null && !turret.transform.IsChildOf(playerRoot)) continue;
                        int id = turret.GetInstanceID() + 7777777;
                        if (!_vehSnaps.TryGetValue(id, out var snap))
                        {
                            snap = new VehicleSnap { SpeedMag = turret.Speed_Mag, TurnRate = turret.Turn_Rate };
                            _vehSnaps[id] = snap;
                        }
                        turret.Speed_Mag = snap.SpeedMag * Mathf.Clamp(v.traverse_scale, 0.05f, 5f);
                        turret.Turn_Rate = snap.TurnRate * Mathf.Clamp(v.traverse_scale, 0.05f, 5f);
                    }
                    foreach (var aim in UnityEngine.Object.FindObjectsOfType<Aiming_Control_CS>())
                    {
                        if (aim == null) continue;
                        if (v.player_only && playerRoot != null && !aim.transform.IsChildOf(playerRoot)) continue;
                        int id = aim.GetInstanceID() + 5555555;
                        if (!_vehSnaps.TryGetValue(id, out var snap))
                        {
                            snap = new VehicleSnap { TurretTurnRate = aim.Turret_Turn_Rate, CannonTurnRate = aim.Cannon_Turn_Rate };
                            _vehSnaps[id] = snap;
                        }
                        aim.Turret_Turn_Rate = snap.TurretTurnRate * Mathf.Clamp(v.traverse_scale, 0.05f, 5f);
                        aim.Cannon_Turn_Rate = snap.CannonTurnRate * Mathf.Clamp(v.traverse_scale, 0.05f, 5f);
                    }
                }

                if (n > 0)
                {
                    var first = UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>();
                    foreach (var d in first)
                    {
                        if (d == null) continue;
                        var snap = _vehSnaps[d.GetInstanceID()];
                        LoggerInstance.Msg($"vehicle: Max_Speed {snap.MaxSpeed:F2} -> {d.Max_Speed:F2}, " +
                                           $"Torque {snap.Torque:F0} -> {d.Torque:F0}, vel={d.Current_Velocity:F2}");
                        break;
                    }
                }
            }
            catch (Exception e) { LoggerInstance.Warning("vehicle override: " + e.Message); }
        }
    }
}
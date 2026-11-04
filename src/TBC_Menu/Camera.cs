// Commander view: a first-person seat on the turret, alongside the game's
// third-person camera and gun sight. The game has no such mode, so this
// detaches Camera (Main) from its pivot and drives it directly.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2CppChobiAssets.PTM;

namespace TbcMenu
{
    public partial class MenuMod
    {
        private bool _commanderActive;
        private Camera _cmdCamera;
        private GameObject _cmdAnchor;
        private Transform _cmdOriginalParent;
        private Vector3 _cmdOriginalLocalPos;
        private Quaternion _cmdOriginalLocalRot;
        private float _cmdOriginalFov;
        private float _cmdYaw;
        private float _cmdPitch;
        private readonly List<Behaviour> _cmdMuted = new List<Behaviour>();

        private void ToggleCommanderView()
        {
            if (_commanderActive) ExitCommanderView();
            else EnterCommanderView();
        }

        private void EnterCommanderView()
        {
            if (_commanderActive) return;
            try
            {
                var cfg = _cfg.camera;
                if (cfg == null) return;

                // player tank -> turret
                Transform tankRoot = null;
                foreach (var p in UnityEngine.Object.FindObjectsOfType<Drive_Control_Input_01_Player_CS>())
                {
                    if (p == null) continue;
                    var ids = p.GetComponentInParent<ID_Settings_CS>();
                    tankRoot = ids != null ? ids.transform : p.transform.root;
                    break;
                }
                if (tankRoot == null) { LoggerInstance.Warning("commander view: no player tank found"); return; }

                // prefer the turret so the view turns with it, then the gun, then the hull
                Transform mount = FirstTransform<Turret_Base_CS>(tankRoot);
                if (mount == null) mount = FirstTransform<Turret_Horizontal_CS>(tankRoot);
                if (mount == null) mount = FirstTransform<Turret_Base_CS>(tankRoot, true);
                if (mount == null) mount = FirstTransform<Cannon_Base_CS>(tankRoot);
                if (mount == null) mount = tankRoot;

                // the 3D camera is the one carrying the letterbox script
                Camera cam = null;
                foreach (var c in Camera.allCameras)
                {
                    if (c == null) continue;
                    if (c.GetComponent<Fixed_Aspect_Camera_CS>() != null) { cam = c; break; }
                }
                if (cam == null) cam = Camera.main;
                if (cam == null) { LoggerInstance.Warning("commander view: no main camera"); return; }

                // leave the gun sight if the player is in it
                try
                {
                    var mgr = Camera_Manager_CS.Instance;
                    if (mgr != null && mgr.Camera_Mode != 0) Gun_Camera_CS.Instance?.Switch_Gun_Camera(false, false);
                }
                catch { }

                _cmdAnchor = new GameObject("TBC_CommanderPoint");
                _cmdAnchor.transform.SetParent(mount, false);
                _cmdAnchor.transform.localPosition = new Vector3(0f, Mathf.Clamp(cfg.commander_height, 0.2f, 4f),
                                                                 Mathf.Clamp(cfg.commander_forward, -2f, 4f));
                _cmdAnchor.transform.localRotation = Quaternion.identity;

                _cmdCamera = cam;
                _cmdOriginalParent = cam.transform.parent;
                _cmdOriginalLocalPos = cam.transform.localPosition;
                _cmdOriginalLocalRot = cam.transform.localRotation;
                _cmdOriginalFov = cam.fieldOfView;
                _cmdYaw = 0f;
                _cmdPitch = 0f;

                MuteCameraScripts(true);

                cam.transform.SetParent(_cmdAnchor.transform, false);
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.Euler(_cmdPitch, _cmdYaw, 0f);
                cam.fieldOfView = Mathf.Clamp(cfg.commander_fov, 30f, 110f);

                _commanderActive = true;
                LoggerInstance.Msg($"commander view on (tank='{tankRoot.name}', mount='{mount.name}')");
            }
            catch (Exception e) { LoggerInstance.Warning("commander view failed: " + e.Message); }
        }

        private void ExitCommanderView()
        {
            if (!_commanderActive) return;
            try
            {
                if (_cmdCamera != null)
                {
                    _cmdCamera.transform.SetParent(_cmdOriginalParent, false);
                    _cmdCamera.transform.localPosition = _cmdOriginalLocalPos;
                    _cmdCamera.transform.localRotation = _cmdOriginalLocalRot;
                    _cmdCamera.fieldOfView = _cmdOriginalFov;
                }
            }
            catch (Exception e) { LoggerInstance.Warning("commander restore failed: " + e.Message); }

            MuteCameraScripts(false);

            try { if (_cmdAnchor != null) UnityEngine.Object.Destroy(_cmdAnchor); } catch { }
            _cmdAnchor = null;
            _cmdCamera = null;
            _commanderActive = false;
            LoggerInstance.Msg("commander view off");
        }

        private Transform FirstTransform<T>(Transform root, bool includeInactive = false) where T : Component
        {
            try
            {
                var comps = root.GetComponentsInChildren<T>(includeInactive || true);
                foreach (var c in comps)
                    if (c != null) return c.transform;
            }
            catch { }
            return null;
        }

        private void MuteCameraScripts(bool mute)
        {
            if (mute)
            {
                _cmdMuted.Clear();
                AddMuted<Camera_Pivot_CS>();
                AddMuted<Camera_Rotation_CS>();
                AddMuted<Camera_Avoid_Obstacle_CS>();
                AddMuted<Camera_Zoom_CS>();
                // free look: the mouse drives the view, so stop it from also training the gun.
                // Firing still works - that is a separate input script.
                if (_cfg.camera != null && _cfg.camera.commander_free_look) AddMuted<Aiming_Control_CS>();
            }
            else
            {
                foreach (var b in _cmdMuted)
                    if (b != null) b.enabled = true;
                _cmdMuted.Clear();
            }
        }

        private void AddMuted<T>() where T : Behaviour
        {
            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<T>())
                {
                    if (b == null || !b.enabled) continue;
                    b.enabled = false;
                    _cmdMuted.Add(b);
                }
            }
            catch (Exception e) { LoggerInstance.Warning("mute " + typeof(T).Name + ": " + e.Message); }
        }

        private void UpdateCommanderView()
        {
            if (!_commanderActive) return;
            try
            {
                if (_cmdCamera == null || _cmdAnchor == null) { _commanderActive = false; return; }

                // if the game switched to the gun sight, hand the camera back
                try
                {
                    var mgr = Camera_Manager_CS.Instance;
                    if (mgr != null && mgr.Camera_Mode != 0) { ExitCommanderView(); return; }
                }
                catch { }

                var cfg = _cfg.camera;
                bool free = cfg != null && cfg.commander_free_look;

                if (free)
                {
                    float sens = cfg == null ? 2f : Mathf.Clamp(cfg.commander_sensitivity, 0.2f, 10f);
                    Vector2 d = ReadMouseDelta();
                    if (d.x != 0f || d.y != 0f)
                    {
                        _cmdYaw += d.x * sens;
                        _cmdPitch = Mathf.Clamp(_cmdPitch - d.y * sens, -85f, 85f);
                    }
                    _cmdCamera.transform.localRotation = Quaternion.Euler(_cmdPitch, _cmdYaw, 0f);
                }
                else
                {
                    // locked view: the turret carries the camera, no extra input
                    _cmdCamera.transform.localRotation = Quaternion.identity;
                }

                float fov = cfg == null ? 70f : Mathf.Clamp(cfg.commander_fov, 30f, 110f);
                if (Mathf.Abs(_cmdCamera.fieldOfView - fov) > 0.01f) _cmdCamera.fieldOfView = fov;
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("commander update: " + e.Message);
                _commanderActive = false;
            }
        }

        private Vector2 ReadMouseDelta()
        {
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    var v = mouse.delta.ReadValue();
                    return new Vector2(v.x, v.y) * 0.05f;
                }
            }
            catch { }
            try { return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")); }
            catch { }
            return Vector2.zero;
        }
    }
}

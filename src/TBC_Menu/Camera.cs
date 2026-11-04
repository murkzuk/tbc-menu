// Commander view: a first-person seat on the turret, alongside the game's
// third-person camera and gun sight.
//
// Important: the camera must NOT be parented to the turret. This game slaves the
// turret to the camera's forward direction (Aiming_Control_CS), so a
// turret-parented camera creates a feedback loop and the turret spins forever.
// Instead the camera keeps its original parent and we drive its world pose
// directly each LateUpdate.
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
        private Vector3 _cmdOriginalLocalPos;
        private Quaternion _cmdOriginalLocalRot;
        private float _cmdOriginalFov;
        private float _cmdBaseYaw;
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

                Transform tankRoot = null;
                foreach (var p in UnityEngine.Object.FindObjectsOfType<Drive_Control_Input_01_Player_CS>())
                {
                    if (p == null) continue;
                    var ids = p.GetComponentInParent<ID_Settings_CS>();
                    tankRoot = ids != null ? ids.transform : p.transform.root;
                    break;
                }
                if (tankRoot == null) { LoggerInstance.Warning("commander view: no player tank found"); return; }

                Transform turret = FirstTransform<Turret_Base_CS>(tankRoot);
                if (turret == null) turret = FirstTransform<Turret_Horizontal_CS>(tankRoot);
                Transform mount = (cfg.commander_follow_turret && turret != null) ? turret : tankRoot;

                Camera cam = null;
                foreach (var c in Camera.allCameras)
                {
                    if (c == null) continue;
                    if (c.GetComponent<Fixed_Aspect_Camera_CS>() != null) { cam = c; break; }
                }
                if (cam == null) cam = Camera.main;
                if (cam == null) { LoggerInstance.Warning("commander view: no main camera"); return; }

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
                _cmdOriginalLocalPos = cam.transform.localPosition;
                _cmdOriginalLocalRot = cam.transform.localRotation;
                _cmdOriginalFov = cam.fieldOfView;

                // start looking where the third-person camera was looking
                _cmdBaseYaw = cam.transform.eulerAngles.y;
                _cmdYaw = 0f;
                _cmdPitch = 0f;

                MuteCameraScripts(true);

                cam.fieldOfView = Mathf.Clamp(cfg.commander_fov, 30f, 110f);
                PlaceCommanderCamera();

                _commanderActive = true;
                LoggerInstance.Msg($"commander view on (tank='{tankRoot.name}', mount='{mount.name}', turret='{(turret == null ? "none" : turret.name)}')");
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

        private void PlaceCommanderCamera()
        {
            if (_cmdCamera == null || _cmdAnchor == null) return;
            _cmdCamera.transform.SetPositionAndRotation(_cmdAnchor.transform.position,
                                                        Quaternion.Euler(_cmdPitch, _cmdBaseYaw + _cmdYaw, 0f));
        }

        private void MuteCameraScripts(bool mute)
        {
            if (mute)
            {
                _cmdMuted.Clear();
                AddMuted<Camera_Pivot_CS>();          // stops it moving the camera
                AddMuted<Camera_Rotation_CS>();       // we drive the yaw ourselves
                AddMuted<Camera_Avoid_Obstacle_CS>(); // no pull-back in first person
                AddMuted<Camera_Zoom_CS>();           // we own the FOV
                if (_cfg.camera != null && _cfg.camera.commander_free_look)
                    AddMuted<Aiming_Control_CS>();    // free look: gun stays put, firing still works
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

        private Transform FirstTransform<T>(Transform root) where T : Component
        {
            try
            {
                foreach (var c in root.GetComponentsInChildren<T>(true))
                    if (c != null) return c.transform;
            }
            catch { }
            return null;
        }

        private void UpdateCommanderView()
        {
            if (!_commanderActive) return;
            try
            {
                if (_cmdCamera == null || _cmdAnchor == null) { _commanderActive = false; return; }

                try
                {
                    var mgr = Camera_Manager_CS.Instance;
                    if (mgr != null && mgr.Camera_Mode != 0) { ExitCommanderView(); return; }
                }
                catch { }

                var cfg = _cfg.camera;
                float sens = cfg == null ? 2f : Mathf.Clamp(cfg.commander_sensitivity, 0.2f, 10f);
                Vector2 d = ReadMouseDelta();
                if (d.x != 0f || d.y != 0f)
                {
                    _cmdYaw += d.x * sens;
                    _cmdPitch = Mathf.Clamp(_cmdPitch - d.y * sens, -85f, 85f);
                }

                PlaceCommanderCamera();

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

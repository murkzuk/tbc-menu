// Commander view: a first-person seat on the turret, alongside the game's
// third-person camera and gun sight.
//
// Important: the camera must NOT be parented to the turret. This game slaves the
// turret to the camera's forward direction (Aiming_Control_CS), so a
// turret-parented camera creates a feedback loop and the turret spins forever.
// Instead the camera keeps its original parent and we drive its world pose
// directly each LateUpdate.
//
// The seat is a *request* (_cmdWanted), not a one-shot action, because two
// situations need it to outlive the moment F6 was pressed:
//   - Camera_Manager_CS may be in the gun sight or the map (Camera_Mode != 0).
//     Entering from there leaves the seat, so pressing F6 while the gun sight is
//     up now pulls the game back to third person and takes the seat (the manager
//     is asked to leave its current mode instead of the view giving up).
//   - the tank or the camera may not exist yet (scene transitions, the result
//     screen). The request then retries for a few seconds instead of failing on
//     the frame F6 happened to land.
// If the player deliberately switches to the gun sight while seated, the seat
// releases the camera and resumes by itself when the mode goes back to 0.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2CppChobiAssets.PTM;

namespace TbcMenu
{
    public partial class MenuMod
    {
        private bool _commanderActive;        // the seat is driving the camera right now
        private bool _cmdWanted;              // the user wants the seat
        private bool _cmdForceThirdView;      // the next attempt may pull the game out of gun sight/map
        private bool _cmdWaitLogged;
        private float _cmdRetryAt;
        private float _cmdGiveUpAt;
        private Camera _cmdCamera;
        private GameObject _cmdAnchor;
        private Vector3 _cmdOriginalLocalPos;
        private Quaternion _cmdOriginalLocalRot;
        private float _cmdOriginalFov;
        private SeatCfg _cmdSeat;             // the seat being used / edited right now
        private string _cmdTankKey;
        private float _cmdBaseYaw;
        private float _cmdYaw;
        private float _cmdPitch;
        private readonly List<Behaviour> _cmdMuted = new List<Behaviour>();

        private bool CommanderWanted => _cmdWanted;

        // menu accessors: edit the current tank's seat when the view is open, else the defaults
        private float SeatHeight
        {
            get => _cmdSeat != null ? _cmdSeat.height : _cfg.camera.commander_height;
            set { if (_cmdSeat != null) _cmdSeat.height = value; else _cfg.camera.commander_height = value; }
        }

        private float SeatForward
        {
            get => _cmdSeat != null ? _cmdSeat.forward : _cfg.camera.commander_forward;
            set { if (_cmdSeat != null) _cmdSeat.forward = value; else _cfg.camera.commander_forward = value; }
        }

        private float SeatRight
        {
            get => _cmdSeat != null ? _cmdSeat.right : _cfg.camera.commander_right;
            set { if (_cmdSeat != null) _cmdSeat.right = value; else _cfg.camera.commander_right = value; }
        }

        private float SeatFov
        {
            get => _cmdSeat != null ? _cmdSeat.fov : _cfg.camera.commander_fov;
            set { if (_cmdSeat != null) _cmdSeat.fov = value; else _cfg.camera.commander_fov = value; }
        }

        private string SeatLabel => _commanderActive
            ? "seat: " + _cmdTankKey
            : _cmdWanted ? "seat: waiting for a tank" : "seat: defaults (all tanks)";

        // ---------------------------------------------------------------- toggle

        private void ToggleCommanderView()
        {
            if (_cmdWanted) { ExitCommanderView(); return; }

            _cmdWanted = true;
            _cmdForceThirdView = true;
            _cmdRetryAt = 0f;
            _cmdGiveUpAt = Time.time + 8f;
            _cmdWaitLogged = false;
            if (!TryEnterCommanderView())
                LoggerInstance.Msg("commander view: nothing to sit on yet - retrying");
        }

        private void ExitCommanderView()
        {
            bool wasOn = _commanderActive || _cmdWanted;
            _cmdWanted = false;
            _cmdForceThirdView = false;
            ReleaseCommanderCamera();
            if (wasOn) LoggerInstance.Msg("commander view off");
        }

        // A scene change destroys the tank, the camera and the anchor, but the request
        // survives so an F6 pressed during the transition still lands in the next mission.
        private void OnSceneChangedCommander()
        {
            ReleaseCommanderCamera();
            _cmdSeat = null;
            _cmdTankKey = null;

            if (_cfg.camera != null && _cfg.camera.commander_view && !_cmdWanted)
            {
                _cmdWanted = true;          // "auto enter on a mission scene"
                _cmdForceThirdView = true;
            }
            if (_cmdWanted) { _cmdRetryAt = 0f; _cmdGiveUpAt = Time.time + 8f; _cmdWaitLogged = false; }
        }

        // Puts the game's camera back the way we found it, without cancelling the request.
        private void ReleaseCommanderCamera()
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
            _cmdSeat = null;
            _commanderActive = false;
        }

        // ---------------------------------------------------------------- enter

        private Camera FindCommanderCamera()
        {
            try
            {
                foreach (var c in Camera.allCameras)
                {
                    if (c == null) continue;
                    if (c.GetComponent<Fixed_Aspect_Camera_CS>() != null) return c;
                }
            }
            catch { }
            try { return Camera.main; }
            catch { }
            return null;
        }

        // Leaves the gun sight / map and asks the manager to put the main camera back.
        // Camera_Mode is read by Switch_Camera_Mode() to decide what to switch *away*
        // from, so it has to be cleared first.
        private void SwitchToThirdPerson(Camera_Manager_CS mgr)
        {
            try
            {
                int was = mgr.Camera_Mode;
                mgr.Camera_Mode = 0;
                mgr.Switch_Camera_Mode(false);      // private: main camera on, gun + map off
                LoggerInstance.Msg("commander view: left camera mode " + was + " and took the seat");
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("commander view: could not reset the camera mode (" + e.Message + "), hiding the gun sight instead");
                try { Gun_Camera_CS.Instance?.Switch_Gun_Camera(false, false); } catch { }
            }
        }

        private bool TryEnterCommanderView()
        {
            if (_commanderActive) return true;

            bool force = _cmdForceThirdView;
            _cmdForceThirdView = false;

            try
            {
                var cfg = _cfg.camera;
                if (cfg == null) return false;

                Transform tankRoot = PlayerTankRoot();
                if (tankRoot == null) return false;

                Camera_Manager_CS mgr = null;
                try { mgr = Camera_Manager_CS.Instance; } catch { }

                if (mgr != null && mgr.Camera_Mode != 0)
                {
                    // someone else owns the camera: F6 takes it, a resume waits for it
                    if (!force) return false;
                    SwitchToThirdPerson(mgr);
                }

                Camera cam = FindCommanderCamera();
                if (cam == null) return false;

                Transform turret = FirstTransform<Turret_Base_CS>(tankRoot);
                if (turret == null) turret = FirstTransform<Turret_Horizontal_CS>(tankRoot);
                Transform mount = (cfg.commander_follow_turret && turret != null) ? turret : tankRoot;

                _cmdAnchor = new GameObject("TBC_CommanderPoint");
                _cmdAnchor.transform.SetParent(mount, false);
                _cmdAnchor.transform.localPosition = new Vector3(0f, Mathf.Clamp(cfg.commander_height, 0.2f, 4f),
                                                                 Mathf.Clamp(cfg.commander_forward, -2f, 4f));
                _cmdAnchor.transform.localRotation = Quaternion.identity;

                _cmdTankKey = TankKey(tankRoot);
                _cmdSeat = ResolveSeat(_cmdTankKey);
                LoggerInstance.Msg($"commander seat for '{_cmdTankKey}': Y={_cmdSeat.height:F2} Z={_cmdSeat.forward:F2} X={_cmdSeat.right:F2} fov={_cmdSeat.fov:F0}");

                _cmdCamera = cam;
                _cmdOriginalLocalPos = cam.transform.localPosition;
                _cmdOriginalLocalRot = cam.transform.localRotation;
                _cmdOriginalFov = cam.fieldOfView;

                // start looking where the third-person camera was looking
                _cmdBaseYaw = cam.transform.eulerAngles.y;
                _cmdYaw = 0f;
                _cmdPitch = 0f;

                MuteCameraScripts(true);

                cam.fieldOfView = Mathf.Clamp(_cmdSeat.fov, 30f, 110f);
                PlaceCommanderCamera();

                _commanderActive = true;
                LoggerInstance.Msg($"commander view on (tank='{tankRoot.name}', mount='{mount.name}', turret='{(turret == null ? "none" : turret.name)}')");
                return true;
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("commander view failed: " + e.Message);
                return false;
            }
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

        // ---------------------------------------------------------------- update

        private void UpdateCommanderView()
        {
            try
            {
                if (_cmdWanted && !_commanderActive)
                {
                    if (Time.time < _cmdRetryAt) return;
                    _cmdRetryAt = Time.time + 0.25f;

                    if (TryEnterCommanderView()) return;

                    // a tank exists but the camera is elsewhere: just wait, quietly, for as
                    // long as the player wants. No tank at all: give up after a few seconds.
                    bool haveTank = PlayerTankRoot() != null;
                    if (!haveTank && Time.time > _cmdGiveUpAt)
                    {
                        LoggerInstance.Warning("commander view: no player tank appeared - giving up");
                        ExitCommanderView();
                        return;
                    }
                    if (!_cmdWaitLogged)
                    {
                        _cmdWaitLogged = true;
                        LoggerInstance.Msg(haveTank
                            ? "commander view: waiting for the game's camera to come back"
                            : "commander view: waiting for the tank to appear");
                    }
                    return;
                }

                if (!_commanderActive) return;
                if (_cmdCamera == null || _cmdAnchor == null) { ReleaseCommanderCamera(); return; }

                Camera_Manager_CS mgr = null;
                try { mgr = Camera_Manager_CS.Instance; } catch { }

                if (mgr != null && mgr.Camera_Mode != 0)
                {
                    // the gun sight or the map took the camera: hand it over, stay ready to resume
                    if (!mgr.Is_Living)
                    {
                        LoggerInstance.Msg("commander view: tank is gone - off");
                        ExitCommanderView();
                        return;
                    }
                    LoggerInstance.Msg("commander view: gun sight/map took the camera - paused");
                    ReleaseCommanderCamera();
                    _cmdRetryAt = Time.time + 0.25f;
                    return;
                }

                var cfg = _cfg.camera;
                ApplyCommanderOffsets(Time.deltaTime);
                float sens = cfg == null ? 2f : Mathf.Clamp(cfg.commander_sensitivity, 0.2f, 10f);
                Vector2 d = _open ? Vector2.zero : ReadMouseDelta();   // the menu owns the mouse
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
                ReleaseCommanderCamera();
            }
        }

        // Applies the offsets every frame, so menu edits and in-game nudging take effect live.
        private void ApplyCommanderOffsets(float dt)
        {
            var cfg = _cfg.camera;
            if (cfg == null || _cmdAnchor == null || _cmdSeat == null) return;

            float step = Mathf.Clamp(cfg.commander_nudge_speed, 0.1f, 10f) * dt;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                if (Input.GetKey(KeyCode.UpArrow)) _cmdSeat.forward += step;
                if (Input.GetKey(KeyCode.DownArrow)) _cmdSeat.forward -= step;
                if (Input.GetKey(KeyCode.LeftArrow)) _cmdSeat.right -= step;
                if (Input.GetKey(KeyCode.RightArrow)) _cmdSeat.right += step;
                if (Input.GetKey(KeyCode.PageUp)) _cmdSeat.height += step;
                if (Input.GetKey(KeyCode.PageDown)) _cmdSeat.height -= step;

                float scroll = 0f;
                try { scroll = Input.mouseScrollDelta.y; } catch { }
                if (Mathf.Abs(scroll) > 0.01f) _cmdSeat.fov = Mathf.Clamp(_cmdSeat.fov - scroll * 3f, 30f, 110f);

                if (Input.GetKeyDown(KeyCode.S))
                {
                    Save();
                    LoggerInstance.Msg($"saved seat for '{_cmdTankKey}' (Y={_cmdSeat.height:F2} Z={_cmdSeat.forward:F2} X={_cmdSeat.right:F2} fov={_cmdSeat.fov:F0})");
                }
            }

            _cmdSeat.right = Mathf.Clamp(_cmdSeat.right, -4f, 4f);
            _cmdSeat.forward = Mathf.Clamp(_cmdSeat.forward, -4f, 4f);
            _cmdSeat.height = Mathf.Clamp(_cmdSeat.height, 0.1f, 5f);

            _cmdAnchor.transform.localPosition = new Vector3(_cmdSeat.right, _cmdSeat.height, _cmdSeat.forward);
        }

        // stable per-tank key: the vehicle's own tank name, else the object name without "(Clone)"
        private string TankKey(Transform tankRoot)
        {
            try
            {
                var ids = tankRoot.GetComponentInChildren<ID_Settings_CS>(true);
                if (ids != null && ids.Tank_Prop != null && !string.IsNullOrEmpty(ids.Tank_Prop.Tank_Name))
                    return ids.Tank_Prop.Tank_Name;
            }
            catch { }
            string n = tankRoot.name ?? "unknown";
            int i = n.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            return (i > 0 ? n.Substring(0, i) : n).Trim();
        }

        private SeatCfg ResolveSeat(string key)
        {
            var cfg = _cfg.camera;
            if (cfg.per_tank != null && !string.IsNullOrEmpty(key) && cfg.per_tank.TryGetValue(key, out var seat) && seat != null)
                return seat;
            var fresh = new SeatCfg
            {
                height = cfg.commander_height,
                forward = cfg.commander_forward,
                right = cfg.commander_right,
                fov = cfg.commander_fov,
            };
            if (cfg.per_tank == null) cfg.per_tank = new System.Collections.Generic.Dictionary<string, SeatCfg>();
            if (!string.IsNullOrEmpty(key)) cfg.per_tank[key] = fresh;
            return fresh;
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

// TBC Menu - runtime tweaks for Tank Battle Classic.
// Entry point, state and lifecycle. See Config.cs, Apply.cs and MenuUi.cs for the rest.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Il2CppChobiAssets.PTM;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

[assembly: MelonInfo(typeof(TbcMenu.MenuMod), "TBC Menu", "1.7.7", "murkzuk")]
[assembly: MelonAdditionalCredits("Built by DeepSeek at murkzuk's request")]
[assembly: MelonGame(null, null)]

namespace TbcMenu
{
    public partial class MenuMod : MelonMod
    {
        private const string ConfigName = "TBC_Menu.json";
        private MenuConfig _cfg = new MenuConfig();
        private string _configPath;

        private bool _open;
        private int _tab;
        private CursorLockMode _prevLock;
        private bool _prevVisible;

        private readonly Dictionary<string, float[]> _floatOriginals = new Dictionary<string, float[]>();
        private readonly Dictionary<int, float> _fogOriginal = new Dictionary<int, float>();
        private readonly HashSet<int> _assetsDone = new HashSet<int>();
        private readonly Dictionary<int, AiSnap> _aiSnaps = new Dictionary<int, AiSnap>();
        private readonly Dictionary<int, int[]> _spIndexSnaps = new Dictionary<int, int[]>();
        private bool _tuningApplied;
        private float _nextTick;
        private string _status = "";

        private class AiSnap
        {
            public float Lead, OpenFire, Visibility, CombatSpeed;
            public bool NoAttack;
        }


        // ---- lifecycle ----------------------------------------------------

        public override void OnInitializeMelon()
        {
            _configPath = Path.Combine(MelonEnvironment.UserDataDirectory, ConfigName);
            Load();
            ApplyAll("startup");
        }

        public override void OnLateUpdate()
        {
            UpdateCommanderView();
            MaintainBufferBypass();   // the gun sight and map cameras only appear when their mode is on
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            OnSceneChangedCommander(); // scene objects are gone; the request survives if it was open
            ResetBufferTracking();     // new scene, new buffer, new cameras
            ResetVehicleSnaps();       // new tanks, new first-seen values
            ResetTuningSnaps();        // new AIs, new first-seen values
            ResetDiag();               // fresh three dumps for each scene
            // A new scene brings new fire/camera scripts and the old ones died with the old scene:
            // re-suspend against the new ones, or the menu stays open over a live gun.
            if (_open) { SetGameInputSuspended(false); SetGameInputSuspended(true); }
            if (!_cfg.enabled) return;
            ApplyAll("scene " + sceneName);
            _nextTick = 0f;
        }

        public override void OnUpdate()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F7)) Toggle();
                if (Input.GetKeyDown(KeyCode.F6)) ToggleCommanderView();
                if (Input.GetKeyDown(KeyCode.F8)) DumpUiGeometry();
            }
            catch (Exception e) { LoggerInstance.Warning("hotkey: " + e.Message); }

            UpdateMenu();

            if (_open)
            {
                try { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } catch { }
            }

            if (!_cfg.enabled) return;
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 2f;
            ApplyVisuals("tick");
            ApplyEffects("tick");
            ApplyTuningRuntime("tick");
            ApplyVehicleOverrides();
            if (_open) EnsurePlayerFireBlocked();    // a respawn or a new mission brings a fresh cannon
            SyncBuffer("tick");
            AutoDiag();
        }

        private void Toggle()
        {
            _open = !_open;
            LoggerInstance.Msg("menu " + (_open ? "open" : "closed"));
            if (_open)
            {
                BuildUI();
                _prevLock = Cursor.lockState;
                _prevVisible = Cursor.visible;
                _tab = 0;
                _builtTab = -1;            // force rebuild
                UpdateMenu();
                SetGameInputSuspended(true);
            }
            else
            {
                try { Cursor.lockState = _prevLock; Cursor.visible = _prevVisible; } catch { }
                SetGameInputSuspended(false);
                if (_root != null) _root.SetActive(false);
            }
        }

    }
}
// Everything that mutates the running game: resolution, render buffer, post effects,
// URP quality settings, fog, textures and gameplay tuning.
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

namespace TbcMenu
{
    public partial class MenuMod
    {
        // ---- apply --------------------------------------------------------

        private void ApplyAll(string why)
        {
            if (!_cfg.enabled) return;
            ApplyResolution();
            SyncBuffer(why);
            ApplyVisuals(why);
            ApplyAssets(why);
            ApplyFog(why);
            ApplyQuality(why);
            ApplyEffects(why);
            ApplyTuningRuntime(why);
        }

        private void ApplyResolution()
        {
            try
            {
                var r = _cfg.resolution;
                var mode = (FullScreenMode)Mathf.Clamp(r.fullscreen_mode, 0, 3);
                Screen.SetResolution(r.width, r.height, mode);
            }
            catch (Exception e) { LoggerInstance.Warning("resolution: " + e.Message); }
            ApplyBufferBypass();
        }

        // ---- render straight to the screen instead of into the 854x480 buffer ---------------
        //
        // The game draws the 3D view into a small RenderTexture and puts that texture on screen
        // through a full screen RawImage ("Canvas (Main RT)"). More than one camera shares the
        // buffer, but not all at the same time: the third person camera and the background camera
        // are always there, while Gun_Camera (gun sight) and Camera (Map) only exist while their
        // mode is switched on - the game gives them the buffer at that moment. An inactive camera
        // is not in Camera.allCameras, so a single pass at scene load misses them, and the gun
        // sight and the map then render into a buffer whose canvas is hidden: a blank screen with
        // the reticle drawn over it (measured in game 1.2.0 / mod 1.7.0). So the bypass is
        // *maintained* every frame instead of applied once.
        private readonly System.Collections.Generic.Dictionary<int, RenderTexture> _bypassed =
            new System.Collections.Generic.Dictionary<int, RenderTexture>();
        private GameObject _bufferCanvas;
        private RenderTexture _bufferTex;
        private float _nextCanvasFind, _nextConstantsLog, _nextBufferFind, _nextWarnLog;
        private int _origRTW = -1, _origRTH = -1;
        private const string BufferCanvasName = "Canvas (Main RT)";

        private void ApplyBufferBypass()
        {
            var r = _cfg.resolution;
            if (r == null || !r.bypass_buffer) { RestoreBufferBypass(); return; }
            MaintainBufferBypass();
        }

        // Runs every frame from OnLateUpdate: a handful of cameras, and it only logs on a change.
        private void MaintainBufferBypass()
        {
            try
            {
                if (_cfg == null || !_cfg.enabled || _cfg.resolution == null || !_cfg.resolution.bypass_buffer) return;

                // The buffer reference is sticky: it is re-found (twice a second at most) only when
                // there is none, or when the one we hold has been destroyed by a scene change. A
                // scene transition leaves the canvas briefly absent, and forgetting the buffer then
                // would put the 3D view back into the hidden buffer for a frame or two.
                if (_bufferTex != null && !BufferTexAlive()) _bufferTex = null;
                if (_bufferTex == null && Time.unscaledTime >= _nextBufferFind)
                {
                    _nextBufferFind = Time.unscaledTime + 0.5f;
                    _bufferTex = FindBuffer();
                }
                if (_bufferTex == null) return;

                foreach (var cam in Camera.allCameras)
                {
                    if (cam == null) continue;
                    var tex = cam.targetTexture;
                    if (tex == null || tex != _bufferTex) continue;
                    int id = cam.GetInstanceID();
                    bool isNew = !_bypassed.ContainsKey(id);
                    _bypassed[id] = tex;
                    cam.targetTexture = null;
                    if (isNew)
                        LoggerInstance.Msg($"buffer bypass: camera '{cam.name}' now draws to the screen " +
                                           $"(had the {tex.width}x{tex.height} buffer), {_bypassed.Count} redirected");
                }

                // Hide the RawImage canvas that stretches the buffer, and keep it hidden: the game
                // re-enables it whenever it switches cameras.
                if (_bufferCanvas == null && Time.unscaledTime >= _nextCanvasFind)
                {
                    _nextCanvasFind = Time.unscaledTime + 0.5f;
                    _bufferCanvas = FindBufferCanvas();
                }
                if (_bufferCanvas != null)
                {
                    try { if (_bufferCanvas.activeSelf) _bufferCanvas.SetActive(false); }
                    catch { _bufferCanvas = null; }
                }

                // Tell the game what surface it is really drawing into. Its aim maths converts the
                // reticle position into buffer pixels using these constants, so leaving them at
                // 854x480 while the camera renders full-screen puts the ray off by the ratio.
                if (_origRTW < 0)
                {
                    try
                    {
                        _origRTW = General_Settings_CS.Render_Texture_Width;
                        _origRTH = General_Settings_CS.Render_Texture_Height;
                    }
                    catch { _origRTW = 854; _origRTH = 480; }
                }
                if (General_Settings_CS.Render_Texture_Width != Screen.width ||
                    General_Settings_CS.Render_Texture_Height != Screen.height)
                {
                    General_Settings_CS.Render_Texture_Width = Screen.width;
                    General_Settings_CS.Render_Texture_Height = Screen.height;
                    if (Time.unscaledTime >= _nextConstantsLog)
                    {
                        _nextConstantsLog = Time.unscaledTime + 2f;
                        LoggerInstance.Msg($"render buffer constants -> {Screen.width}x{Screen.height} " +
                                           $"(originals {_origRTW}x{_origRTH})");
                    }
                }
            }
            catch (Exception e) { WarnThrottled("buffer bypass: " + e.Message); }
        }

        // This path can run every frame, so a failing call must not fill the log.
        private void WarnThrottled(string message)
        {
            if (Time.unscaledTime < _nextWarnLog) return;
            _nextWarnLog = Time.unscaledTime + 5f;
            LoggerInstance.Warning(message);
        }

        private bool BufferTexAlive()
        {
            try { return _bufferTex.width > 0; } catch { return false; }
        }

        private void RestoreBufferBypass()
        {
            if (_bypassed.Count == 0 && _bufferCanvas == null && _origRTW < 0) return;   // nothing to undo
            try
            {
                // Camera.allCameras only lists enabled cameras, and the gun sight / map cameras are
                // inactive most of the time, so search every camera before giving the buffer back.
                foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
                {
                    if (cam == null) continue;
                    if (!_bypassed.TryGetValue(cam.GetInstanceID(), out var tex) || tex == null) continue;
                    if (cam.targetTexture != null) continue;   // the game has already given it one
                    cam.targetTexture = tex;
                }
                _bypassed.Clear();
                _bufferTex = null;
                if (_bufferCanvas != null)
                {
                    try { _bufferCanvas.SetActive(true); } catch { }
                    _bufferCanvas = null;
                }
                if (_origRTW > 0)
                {
                    try
                    {
                        General_Settings_CS.Render_Texture_Width = _origRTW;
                        General_Settings_CS.Render_Texture_Height = _origRTH;
                    }
                    catch { }
                }
                LoggerInstance.Msg($"buffer bypass off - the game's buffer is back at {_origRTW}x{_origRTH}");
            }
            catch (Exception e) { LoggerInstance.Warning("buffer bypass off: " + e.Message); }
        }

        // A scene change replaces the buffer, the cameras and the canvas.
        private void ResetBufferTracking()
        {
            _bypassed.Clear();
            _bufferTex = null;
            _bufferCanvas = null;
            _nextCanvasFind = 0f;
        }

        private GameObject FindBufferCanvas()
        {
            try
            {
                foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
                {
                    if (canvas == null || canvas.name != BufferCanvasName) continue;
                    return canvas.gameObject;
                }
            }
            catch { }
            return null;
        }

        // Which RenderTexture is *the* view buffer? It is the one the full screen RawImage on
        // "Canvas (Main RT)" displays, so that canvas is the primary anchor. Searching for "any
        // RenderTexture a RawImage holds" is not good enough: a briefing screen also carries the
        // 256x256 minimap, and mistaking that for the buffer redirects the minimap camera to the
        // screen while the real view buffer stays hidden (seen in game, 1.7.1).
        private RenderTexture FindBuffer()
        {
            try
            {
                foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
                {
                    if (canvas == null || canvas.name != BufferCanvasName) continue;
                    var raw = canvas.GetComponentsInChildren<RawImage>(true);
                    if (raw == null) continue;
                    foreach (var ri in raw)
                    {
                        if (ri == null || ri.texture == null) continue;
                        var rt = ri.texture.TryCast<RenderTexture>();
                        if (rt != null) return rt;
                    }
                }
            }
            catch { }
            // Fallback: a screen sized RenderTexture anywhere. The floor of 512 px wide keeps the
            // 256x256 minimap out of it even if the canvas is named differently in a later build.
            try
            {
                foreach (var ri in Resources.FindObjectsOfTypeAll<RawImage>())
                {
                    if (ri == null || ri.texture == null) continue;
                    var rt = ri.texture.TryCast<RenderTexture>();
                    if (rt == null || rt.width < 512) continue;
                    return rt;
                }
            }
            catch { }
            try
            {
                foreach (var c in Camera.allCameras)
                {
                    if (c == null || c.targetTexture == null) continue;
                    if (c.targetTexture.width < 512) continue;
                    return c.targetTexture;
                }
            }
            catch { }
            return null;
        }

        // Note: this changes the size of the buffer the game renders into. The game maps a screen
        // position to the aim ray with Camera.ScreenPointToRay and keeps the reticle lined up by
        // letterboxing the camera and scaling the reticle transform for Screen.width/height; a
        // substituted buffer size puts the reticle and the ray out of step and the gun visibly
        // trails the crosshair. internal_scale defaults to 0 (leave the buffer alone) for that
        // reason - quality.render_scale is the safe way to supersample.
        private void SyncBuffer(string why)
        {
            try
            {
                var r = _cfg.resolution;
                if (r.internal_scale <= 0f) return;
                if (Screen.width <= 0 || Screen.height <= 0) return;
                var rt = FindBuffer();
                if (rt == null) return;
                float s = Mathf.Clamp(r.internal_scale, 0.1f, 4f);
                int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * s));
                int h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * s));
                if (rt.width == w && rt.height == h) return;
                int msaa = Mathf.Clamp(_cfg.quality == null ? 1 : _cfg.quality.msaa, 1, 8);
                rt.Release();
                rt.width = w;
                rt.height = h;
                try { rt.antiAliasing = msaa; } catch { }
                if (s > 1.01f) try { rt.filterMode = FilterMode.Bilinear; } catch { }
                rt.Create();
                LoggerInstance.Msg($"buffer {w}x{h} msaa={rt.antiAliasing} filter={rt.filterMode} [{why}]");
            }
            catch (Exception e) { LoggerInstance.Warning("buffer: " + e.Message); }
        }

        // A Volume exposes two profiles: sharedProfile (the asset) and profile (a runtime instance
        // URP instantiates from it on first access). Which one the pipeline blends is not obvious
        // from outside, and reading `profile` creates the clone - so write to both and it no longer
        // matters. Returns one or two distinct profiles.
        private System.Collections.Generic.List<VolumeProfile> ProfilesOf(Volume vol)
        {
            var list = new System.Collections.Generic.List<VolumeProfile>(2);
            try { var sp = vol.sharedProfile; if (sp != null) list.Add(sp); } catch { }
            try
            {
                var ip = vol.profile;
                if (ip != null && (list.Count == 0 || ip.GetInstanceID() != list[0].GetInstanceID())) list.Add(ip);
            }
            catch { }
            return list;
        }

        private void ApplyVisuals(string why)
        {
            var v = _cfg.visuals;
            try
            {
                foreach (var vol in Resources.FindObjectsOfTypeAll<Volume>())
                {
                    if (vol == null) continue;
                    foreach (var prof in ProfilesOf(vol))
                    {
                        if (prof == null || prof.components == null) continue;
                        foreach (var comp in prof.components)
                        {
                            if (comp == null) continue;
                            var fg = comp.TryCast<FilmGrain>();
                            if (fg != null) { fg.intensity.value = Mathf.Clamp01(v.film_grain); fg.active = v.film_grain > 0.001f; continue; }
                            var ca = comp.TryCast<ChromaticAberration>();
                            if (ca != null) { ca.intensity.value = Mathf.Clamp01(v.chromatic_aberration); ca.active = v.chromatic_aberration > 0.001f; continue; }
                            var vig = comp.TryCast<Vignette>();
                            if (vig != null) { vig.intensity.value = Mathf.Clamp01(v.vignette); vig.active = v.vignette > 0.001f; continue; }
                        }
                    }
                }
            }
            catch (Exception e) { LoggerInstance.Warning("volumes: " + e.Message); }

            try
            {
                var mode = ParseAa(v.antialiasing);
                foreach (var cam in Camera.allCameras)
                {
                    if (cam == null) continue;
                    var d = cam.GetUniversalAdditionalCameraData();
                    if (d == null) continue;
                    d.antialiasing = mode;
                    d.antialiasingQuality = AntialiasingQuality.High;
                    d.dithering = v.dithering;
                }
            }
            catch (Exception e) { LoggerInstance.Warning("cameras: " + e.Message); }
        }

        private void ApplyAssets(string why)
        {
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            if (_assetsDone.Contains(scene)) return;
            _assetsDone.Add(scene);
            if (_cfg.visuals.texture_aniso <= 0) return;
            try
            {
                int n = 0;
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                {
                    if (t == null || t.width < 128 || t.height < 128) continue;
                    t.anisoLevel = Mathf.Clamp(_cfg.visuals.texture_aniso, 0, 16);
                    n++;
                }
                LoggerInstance.Msg($"aniso {_cfg.visuals.texture_aniso} on {n} texture(s) [{why}]");
            }
            catch (Exception e) { LoggerInstance.Warning("aniso: " + e.Message); }
        }

        private void ApplyFog(string why)
        {
            var v = _cfg.visuals;
            try
            {
                if (!RenderSettings.fog) return;
                int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                if (!_fogOriginal.TryGetValue(scene, out var orig))
                {
                    orig = RenderSettings.fogDensity;
                    _fogOriginal[scene] = orig;
                }
                // No early-out for scale == 1: going back to 1.0 has to write the game value, or
                // the scene keeps whatever the last scaled density was.
                float want = orig * Mathf.Max(0f, v.fog_density_scale);
                if (Mathf.Abs(RenderSettings.fogDensity - want) < 0.000001f) return;
                RenderSettings.fogDensity = want;
                LoggerInstance.Msg($"fog density {want:F5} (game {orig:F5} x {v.fog_density_scale:F2}) [{why}]");
            }
            catch (Exception e) { LoggerInstance.Warning("fog: " + e.Message); }
        }

        // Tuning is a one-way door unless it is undone. Everything it writes outlives the switch:
        // the six Tuning_Value arrays are static and get copied into each vehicle at spawn by
        // Special_Settings_CS, the AI fields are live, and No_Attack latches. So this runs on the
        // transition rather than on every tick: apply while enabled, restore once when it goes off.
        private void ApplyTuningRuntime(string why)
        {
            var t = _cfg.tuning;
            if (t == null) return;
            if (t.enabled)
            {
                bool first = !_tuningApplied;
                _tuningApplied = true;
                ApplyTuningArrays();
                ApplyAiOverrides();
                if (t.max_levels) ApplyMaxLevels();
                ApplyRoster();
                if (first) LogTuningState("applied");   // once per transition, not every 2 s
            }
            else if (_tuningApplied)
            {
                RestoreTuning();
            }
        }

        // Puts back everything ApplyTuningRuntime wrote: the arrays (from the originals cached on
        // first use), the AI settings, and the player's tuning multiplier indices.
        private void RestoreTuning()
        {
            int aiCount = 0, indexCount = 0;
            try
            {
                RestoreTuningArrays();

                foreach (var ai in UnityEngine.Object.FindObjectsOfType<AI_Settings_CS>())
                {
                    if (ai == null) continue;
                    if (!_aiSnaps.TryGetValue(ai.GetInstanceID(), out var s) || s == null) continue;
                    ai.Max_Lead_Multiplier = s.Lead;
                    ai.OpenFire_Distance = s.OpenFire;
                    ai.Extended_Visibility = s.Visibility;
                    ai.Combat_Speed_Rate = s.CombatSpeed;
                    ai.No_Attack = s.NoAttack;
                    aiCount++;
                }

                foreach (var p in UnityEngine.Object.FindObjectsOfType<Drive_Control_Input_01_Player_CS>())
                {
                    if (p == null) continue;
                    var sp = p.GetComponentInParent<Special_Settings_CS>();
                    if (sp == null) continue;
                    if (!_spIndexSnaps.TryGetValue(sp.GetInstanceID(), out var idx) || idx == null || idx.Length < 6) continue;
                    sp.Attack_Multiplier_Index = idx[0];
                    sp.Defence_Multiplier_Index = idx[1];
                    sp.Drive_Speed_Multiplier_Index = idx[2];
                    sp.Turret_Speed_Multiplier_Index = idx[3];
                    sp.Reload_Speed_Multiplier_Index = idx[4];
                    sp.Bullet_Speed_Multiplier_Index = idx[5];
                    indexCount++;
                }
            }
            catch (Exception e) { LoggerInstance.Warning("tuning restore: " + e.Message); }

            LogTuningState("restored");
            LoggerInstance.Msg($"tuning off: {_floatOriginals.Count} multiplier array(s), " +
                               $"{aiCount} AI setting(s){(indexCount > 0 ? ", " + indexCount + " tank index set(s)" : "")} restored");
            _aiSnaps.Clear();
            _spIndexSnaps.Clear();
            _tuningApplied = false;
        }

        // So "did the toggle really move anything, and did it put it back?" is answerable from the
        // log alone: one line for the multiplier arrays, one per AI.
        private void LogTuningState(string tag)
        {
            try
            {
                LoggerInstance.Msg($"tuning [{tag}] multipliers["+1+"]: " +
                                   $"attack {SampleOf(() => Tuning_Value.Attack_Multipliers):F3}, " +
                                   $"defence {SampleOf(() => Tuning_Value.Defence_Multipliers):F3}, " +
                                   $"drive {SampleOf(() => Tuning_Value.Drive_Speed_Multipliers):F3}, " +
                                   $"turret {SampleOf(() => Tuning_Value.Turret_Speed_Multipliers):F3}, " +
                                   $"reload {SampleOf(() => Tuning_Value.Reload_Speed_Multipliers):F3}, " +
                                   $"bullet {SampleOf(() => Tuning_Value.Bullet_Speed_Multipliers):F3}");
            }
            catch (Exception e) { LoggerInstance.Warning("tuning state: " + e.Message); }
            try
            {
                foreach (var ai in UnityEngine.Object.FindObjectsOfType<AI_Settings_CS>())
                {
                    if (ai == null) continue;
                    string nm = "?";
                    try { nm = TankNameOf(ai.transform); } catch { }
                    LoggerInstance.Msg($"ai '{nm}' [{tag}]: lead {ai.Max_Lead_Multiplier:F3}, " +
                                       $"openFire {ai.OpenFire_Distance:F1}, visibility {ai.Extended_Visibility:F2}, " +
                                       $"combatSpeed {ai.Combat_Speed_Rate:F3}, passive {(ai.No_Attack ? "on" : "off")}");
                }
            }
            catch (Exception e) { LoggerInstance.Warning("ai state: " + e.Message); }
        }

        // Element 1 of a multiplier table: the first non-trivial step, and enough to tell a scaled
        // table from a restored one in the log.
        private static float SampleOf(Func<Il2CppStructArray<float>> get)
        {
            try
            {
                var a = get();
                if (a == null || a.Length == 0) return 0f;
                return a.Length > 1 ? a[1] : a[0];
            }
            catch { return 0f; }
        }

        // A new scene brings new tanks and new AIs; the old snapshots belong to destroyed objects.
        private void ResetTuningSnaps()
        {
            _aiSnaps.Clear();
            _spIndexSnaps.Clear();
        }

        private void ApplyTuningArrays()
        {
            var t = _cfg.tuning;
            ScaleArr("attack", () => Tuning_Value.Attack_Multipliers, t.attack);
            ScaleArr("defence", () => Tuning_Value.Defence_Multipliers, t.defence);
            ScaleArr("drive", () => Tuning_Value.Drive_Speed_Multipliers, t.drive);
            ScaleArr("turret", () => Tuning_Value.Turret_Speed_Multipliers, t.turret);
            ScaleArr("reload", () => Tuning_Value.Reload_Speed_Multipliers, t.reload);
            ScaleArr("bullet", () => Tuning_Value.Bullet_Speed_Multipliers, t.bullet);
        }

        // Writing the cached originals back with a factor of 1 is the restore: same accessors, so
        // there is no second list of array names to keep in step.
        private void RestoreTuningArrays()
        {
            ScaleArr("attack", () => Tuning_Value.Attack_Multipliers, 1f);
            ScaleArr("defence", () => Tuning_Value.Defence_Multipliers, 1f);
            ScaleArr("drive", () => Tuning_Value.Drive_Speed_Multipliers, 1f);
            ScaleArr("turret", () => Tuning_Value.Turret_Speed_Multipliers, 1f);
            ScaleArr("reload", () => Tuning_Value.Reload_Speed_Multipliers, 1f);
            ScaleArr("bullet", () => Tuning_Value.Bullet_Speed_Multipliers, 1f);
        }

        private void ScaleArr(string key, Func<Il2CppStructArray<float>> get, float scale)
        {
            try
            {
                var a = get();
                if (a == null) return;
                if (!_floatOriginals.TryGetValue(key, out var orig))
                {
                    orig = new float[a.Length];
                    for (int i = 0; i < a.Length; i++) orig[i] = a[i];
                    _floatOriginals[key] = orig;
                }
                for (int i = 0; i < a.Length && i < orig.Length; i++) a[i] = orig[i] * Mathf.Max(0f, scale);
            }
            catch (Exception e) { LoggerInstance.Warning("tuning " + key + ": " + e.Message); }
        }

        private void ApplyAiOverrides()
        {
            var t = _cfg.tuning;
            float pLead = 1f, pDist = 1f, pVis = 1f, pSpd = 1f;
            switch ((t.ai_preset ?? "off").ToLowerInvariant())
            {
                case "green": pLead = 0.75f; pDist = 0.80f; pVis = 0.85f; pSpd = 0.85f; break;
                case "veteran": break;
                case "ace": pLead = 1.35f; pDist = 1.20f; pVis = 1.25f; pSpd = 1.10f; break;
            }
            try
            {
                foreach (var ai in UnityEngine.Object.FindObjectsOfType<AI_Settings_CS>())
                {
                    if (ai == null) continue;
                    int id = ai.GetInstanceID();
                    if (!_aiSnaps.TryGetValue(id, out var s))
                    {
                        s = new AiSnap
                        {
                            Lead = ai.Max_Lead_Multiplier,
                            OpenFire = ai.OpenFire_Distance,
                            Visibility = ai.Extended_Visibility,
                            CombatSpeed = ai.Combat_Speed_Rate,
                            NoAttack = ai.No_Attack,
                        };
                        _aiSnaps[id] = s;
                    }
                    ai.Max_Lead_Multiplier = s.Lead * pLead * t.ai_lead_scale;
                    ai.OpenFire_Distance = s.OpenFire * pDist * t.ai_open_fire_scale;
                    ai.Extended_Visibility = s.Visibility * pVis * t.ai_visibility_scale;
                    ai.Combat_Speed_Rate = s.CombatSpeed * pSpd * t.ai_combat_speed_scale;
                    // OR, not assignment: the mod may make an AI passive, but it must never
                    // un-passify one the game (or another mod) had already silenced.
                    ai.No_Attack = t.ai_no_attack || s.NoAttack;
                }
            }
            catch (Exception e) { LoggerInstance.Warning("ai: " + e.Message); }
        }

        private void ApplyMaxLevels()
        {
            try
            {
                foreach (var p in UnityEngine.Object.FindObjectsOfType<Drive_Control_Input_01_Player_CS>())
                {
                    if (p == null) continue;
                    var sp = p.GetComponentInParent<Special_Settings_CS>();
                    if (sp == null) continue;
                    int spId = sp.GetInstanceID();
                    if (!_spIndexSnaps.ContainsKey(spId))
                    {
                        _spIndexSnaps[spId] = new int[]
                        {
                            sp.Attack_Multiplier_Index, sp.Defence_Multiplier_Index,
                            sp.Drive_Speed_Multiplier_Index, sp.Turret_Speed_Multiplier_Index,
                            sp.Reload_Speed_Multiplier_Index, sp.Bullet_Speed_Multiplier_Index,
                        };
                    }
                    sp.Attack_Multiplier_Index = Steps(Tuning_Value.Attack_Multipliers);
                    sp.Defence_Multiplier_Index = Steps(Tuning_Value.Defence_Multipliers);
                    sp.Drive_Speed_Multiplier_Index = Steps(Tuning_Value.Drive_Speed_Multipliers);
                    sp.Turret_Speed_Multiplier_Index = Steps(Tuning_Value.Turret_Speed_Multipliers);
                    sp.Reload_Speed_Multiplier_Index = Steps(Tuning_Value.Reload_Speed_Multipliers);
                    sp.Bullet_Speed_Multiplier_Index = Steps(Tuning_Value.Bullet_Speed_Multipliers);
                }
            }
            catch (Exception e) { LoggerInstance.Warning("max levels: " + e.Message); }
        }

        private static int Steps(Il2CppStructArray<float> a)
        {
            int n = a == null ? 0 : a.Length;
            return n > 0 ? n - 1 : 0;
        }

        private void ApplyRoster()
        {
            var t = _cfg.tuning;
            if (!t.unlock_all && t.points <= 0) return;
            try
            {
                var pd = General_Settings_CS.Current_Player_Data;
                if (pd == null) return;
                if (t.points > 0 && pd.Points < t.points) pd.Points = t.points;
                if (!t.unlock_all || pd.Owned_Tank_Names == null) return;
                foreach (var prop in Resources.FindObjectsOfTypeAll<Tank_Prop_SO>())
                {
                    if (prop == null) continue;
                    string nm = null;
                    try { nm = prop.Tank_Name; } catch { }
                    if (string.IsNullOrEmpty(nm)) continue;
                    bool has = false;
                    try { has = pd.Owned_Tank_Names.Contains(nm); } catch { }
                    if (!has) { try { pd.Owned_Tank_Names.Add(nm); } catch { } }
                }
            }
            catch (Exception e) { LoggerInstance.Warning("roster: " + e.Message); }
        }

        private static AntialiasingMode ParseAa(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "fxaa": return AntialiasingMode.FastApproximateAntialiasing;
                case "smaa": return AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                case "taa": return AntialiasingMode.TemporalAntiAliasing;
                default: return AntialiasingMode.None;
            }
        }



        // ---- quality (URP asset) -------------------------------------------

        private void ApplyQuality(string why)
        {
            var q = _cfg.quality;
            if (q == null) return;
            try
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                var a = rp == null ? null : rp.TryCast<UniversalRenderPipelineAsset>();
                if (a == null) return;

                try { a.msaaSampleCount = Mathf.Clamp(q.msaa, 1, 8); } catch (Exception e) { LoggerInstance.Warning("msaa: " + e.Message); }
                try { a.mainLightShadowmapResolution = Mathf.Clamp(q.main_light_shadow_res, 512, 4096); } catch (Exception e) { LoggerInstance.Warning("shadow res: " + e.Message); }
                try { a.shadowDistance = Mathf.Clamp(q.shadow_distance, 32f, 500f); } catch (Exception e) { LoggerInstance.Warning("shadow dist: " + e.Message); }
                try { a.renderScale = Mathf.Clamp(q.render_scale, 0.5f, 2f); } catch (Exception e) { LoggerInstance.Warning("render scale: " + e.Message); }
                try
                {
                    SoftShadowQuality sq;
                    switch ((q.soft_shadow_quality ?? "use").ToLowerInvariant())
                    {
                        case "low": sq = SoftShadowQuality.Low; break;
                        case "medium": sq = SoftShadowQuality.Medium; break;
                        case "high": sq = SoftShadowQuality.High; break;
                        default: sq = SoftShadowQuality.UsePipelineSettings; break;
                    }
                    a.softShadowQuality = sq;
                }
                catch (Exception e) { LoggerInstance.Warning("soft shadows: " + e.Message); }

                LoggerInstance.Msg($"quality: msaa={a.msaaSampleCount} shadowRes={a.mainLightShadowmapResolution} " +
                                   $"soft={a.softShadowQuality} dist={a.shadowDistance:F0} scale={a.renderScale:F2} [{why}]");
            }
            catch (Exception e) { LoggerInstance.Warning("quality pass: " + e.Message); }
        }


        // ---- post effects (own volume, overrides forced on) -----------------

        private string _effectsStamp;
        private Volume _fxVolume;
        private Bloom _fxBloom;
        private Tonemapping _fxTone;
        private ColorAdjustments _fxColor;

        // Earlier attempts (1.7.5/1.7.6) concluded this game ignores components a mod creates. The
        // missing piece was VolumeParameter.overrideState: URP ignores a parameter whose override
        // flag is false, and a component built with ScriptableObject.CreateInstance starts with every
        // flag false - while the components the game ships were authored with theirs true, which is
        // exactly why writing those (vignette turned the corners black) worked and writing ours did
        // nothing, however it was delivered.
        private void ForceOverrides(VolumeComponent comp)
        {
            if (comp == null) return;
            try { comp.SetAllOverridesTo(true); return; } catch { }
            try
            {
                var ps = comp.parameters;
                if (ps == null) return;
                for (int i = 0; i < ps.Count; i++)
                {
                    var p = ps[i];
                    if (p != null) p.overrideState = true;
                }
            }
            catch { }
        }

        // Attach our three components to the profile the pipeline actually blends: the game's own
        // volume, and its runtime profile instance. Overrides are forced on, because URP ignores a
        // parameter whose overrideState is false and a CreateInstance component starts with them all
        // false - that is why the components the game ships (authored, overrides true) responded and
        // ours did not.
        private void EnsureEffectVolume()
        {
            if (_fxVolume != null && _fxColor != null) return;
            try
            {
                if (VolumeManager.instance == null) return;
                Volume target = null;
                VolumeProfile prof = null;
                foreach (var v in Resources.FindObjectsOfTypeAll<Volume>())
                {
                    if (v == null) continue;
                    try { prof = v.profile; } catch { prof = null; }
                    if (prof == null || prof.components == null) { try { prof = v.sharedProfile; } catch { } }
                    if (prof == null || prof.components == null) continue;
                    target = v;
                    break;
                }
                if (target == null || prof == null) return;

                foreach (var c in prof.components)
                {
                    if (c == null) continue;
                    if (_fxBloom == null) _fxBloom = c.TryCast<Bloom>();
                    if (_fxTone == null) _fxTone = c.TryCast<Tonemapping>();
                    if (_fxColor == null) _fxColor = c.TryCast<ColorAdjustments>();
                }
                if (_fxBloom == null) { _fxBloom = ScriptableObject.CreateInstance<Bloom>(); ForceOverrides(_fxBloom); prof.components.Add(_fxBloom); }
                if (_fxTone == null) { _fxTone = ScriptableObject.CreateInstance<Tonemapping>(); ForceOverrides(_fxTone); prof.components.Add(_fxTone); }
                if (_fxColor == null) { _fxColor = ScriptableObject.CreateInstance<ColorAdjustments>(); ForceOverrides(_fxColor); prof.components.Add(_fxColor); }

                _fxVolume = target;
                _effectsStamp = null;
                LoggerInstance.Msg($"effects: attached to the game's volume '{target.name}' " +
                                   $"({prof.components.Count} components, overrides forced on)");
            }
            catch (Exception e) { LoggerInstance.Warning("effects volume: " + e.Message); }
        }

        private void ApplyEffects(string why)
        {
            var e = _cfg.effects;
            if (e == null) return;
            try
            {
                EnsureEffectVolume();
                if (_fxBloom == null || _fxTone == null || _fxColor == null) return;

                _fxBloom.threshold.value = Mathf.Clamp(e.bloom_threshold, 0f, 4f);
                _fxBloom.intensity.value = Mathf.Clamp(e.bloom, 0f, 4f);
                _fxBloom.scatter.value = 0.7f;
                try { _fxBloom.highQualityFiltering.value = true; } catch { }
                _fxBloom.active = e.bloom > 0.001f;

                string tm = (e.tonemapping ?? "none").ToLowerInvariant();
                _fxTone.mode.value = tm == "aces" ? TonemappingMode.ACES
                                  : tm == "neutral" ? TonemappingMode.Neutral
                                  : TonemappingMode.None;
                _fxTone.active = tm != "none";

                _fxColor.postExposure.value = Mathf.Clamp(e.exposure, -5f, 5f);
                _fxColor.contrast.value = Mathf.Clamp(e.contrast, -100f, 100f);
                _fxColor.saturation.value = Mathf.Clamp(e.saturation, -100f, 100f);
                _fxColor.active = Math.Abs(e.exposure) > 0.001f
                               || Math.Abs(e.contrast) > 0.001f
                               || Math.Abs(e.saturation) > 0.001f;

                string stamp = $"{e.tonemapping}|{e.bloom:F2}|{e.bloom_threshold:F2}|" +
                               $"{e.exposure:F2}|{e.contrast:F0}|{e.saturation:F0}";
                if (stamp == _effectsStamp) return;
                _effectsStamp = stamp;
                LoggerInstance.Msg($"effects [{why}]: tonemapping {e.tonemapping}, bloom {e.bloom:F2} @ " +
                                   $"{e.bloom_threshold:F2}, exposure {e.exposure:F2}, contrast {e.contrast:F0}, " +
                                   $"saturation {e.saturation:F0}");
            }
            catch (Exception ex) { LoggerInstance.Warning("effects pass: " + ex.Message); }
        }

    }
}

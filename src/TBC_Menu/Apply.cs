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
        private readonly System.Collections.Generic.Dictionary<int, RenderTexture> _bypassed =
            new System.Collections.Generic.Dictionary<int, RenderTexture>();
        private GameObject _bufferCanvas;
        private int _origRTW = -1, _origRTH = -1;

        private void ApplyBufferBypass()
        {
            var r = _cfg.resolution;
            bool want = r != null && r.bypass_buffer;
            try
            {
                var buf = FindBuffer();
                if (!want)
                {
                    if (_bypassed.Count == 0 && _bufferCanvas == null) return;
                    foreach (var cam in Camera.allCameras)
                    {
                        if (cam == null) continue;
                        if (_bypassed.TryGetValue(cam.GetInstanceID(), out var tex) && tex != null)
                            cam.targetTexture = tex;
                    }
                    _bypassed.Clear();
                    if (_bufferCanvas != null) _bufferCanvas.SetActive(true);
                    _bufferCanvas = null;
                    if (_origRTW > 0)
                    {
                        try
                        {
                            General_Settings_CS.Render_Texture_Width = _origRTW;
                            General_Settings_CS.Render_Texture_Height = _origRTH;
                        }
                        catch { }
                    }
                    LoggerInstance.Msg("buffer bypass off - restoring the game's buffer");
                    return;
                }
                if (buf == null) return;

                int n = 0;
                foreach (var cam in Camera.allCameras)
                {
                    if (cam == null || cam.targetTexture == null) continue;
                    if (cam.targetTexture != buf) continue;
                    int id = cam.GetInstanceID();
                    if (!_bypassed.ContainsKey(id)) _bypassed[id] = cam.targetTexture;
                    cam.targetTexture = null;
                    n++;
                }
                // hide the RawImage canvas that stretches the buffer, or it would cover the screen
                if (_bufferCanvas == null)
                {
                    try
                    {
                        foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
                        {
                            if (canvas == null || canvas.name != "Canvas (Main RT)") continue;
                            _bufferCanvas = canvas.gameObject;
                            break;
                        }
                    }
                    catch { }
                    if (_bufferCanvas != null) _bufferCanvas.SetActive(false);
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
                try
                {
                    LoggerInstance.Msg($"RT constants now {General_Settings_CS.Render_Texture_Width}x{General_Settings_CS.Render_Texture_Height}, " +
                                       $"screen {Screen.width}x{Screen.height}, saved originals {_origRTW}x{_origRTH}");
                    if (General_Settings_CS.Render_Texture_Width != Screen.width)
                    {
                        General_Settings_CS.Render_Texture_Width = Screen.width;
                        General_Settings_CS.Render_Texture_Height = Screen.height;
                        LoggerInstance.Msg($"render buffer constants -> {Screen.width}x{Screen.height} (were {_origRTW}x{_origRTH})");
                    }
                }
                catch (Exception e) { LoggerInstance.Warning("buffer constants: " + e.Message); }

                if (n > 0) LoggerInstance.Msg($"buffer bypass on: {n} camera(s) render to the screen, buffer canvas hidden");
            }
            catch (Exception e) { LoggerInstance.Warning("buffer bypass: " + e.Message); }
        }

        private RenderTexture FindBuffer()
        {
            try
            {
                foreach (var ri in Resources.FindObjectsOfTypeAll<RawImage>())
                {
                    if (ri == null || ri.texture == null) continue;
                    var rt = ri.texture.TryCast<RenderTexture>();
                    if (rt != null) return rt;
                }
            }
            catch { }
            try
            {
                foreach (var c in Camera.allCameras)
                    if (c != null && c.targetTexture != null) return c.targetTexture;
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

        private void ApplyVisuals(string why)
        {
            var v = _cfg.visuals;
            try
            {
                foreach (var vol in Resources.FindObjectsOfTypeAll<Volume>())
                {
                    if (vol == null) continue;
                    var prof = vol.sharedProfile != null ? vol.sharedProfile : vol.profile;
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
            if (Math.Abs(v.fog_density_scale - 1f) < 0.0001f) return;
            try
            {
                if (!RenderSettings.fog) return;
                int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                if (!_fogOriginal.TryGetValue(scene, out var orig))
                {
                    orig = RenderSettings.fogDensity;
                    _fogOriginal[scene] = orig;
                }
                RenderSettings.fogDensity = orig * Mathf.Max(0f, v.fog_density_scale);
            }
            catch (Exception e) { LoggerInstance.Warning("fog: " + e.Message); }
        }

        private void ApplyTuningRuntime(string why)
        {
            if (_cfg.tuning == null || !_cfg.tuning.enabled) return;
            ApplyTuningArrays();
            ApplyAiOverrides();
            if (_cfg.tuning.max_levels) ApplyMaxLevels();
            ApplyRoster();
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
                        s = new AiSnap { Lead = ai.Max_Lead_Multiplier, OpenFire = ai.OpenFire_Distance, Visibility = ai.Extended_Visibility, CombatSpeed = ai.Combat_Speed_Rate };
                        _aiSnaps[id] = s;
                    }
                    ai.Max_Lead_Multiplier = s.Lead * pLead * t.ai_lead_scale;
                    ai.OpenFire_Distance = s.OpenFire * pDist * t.ai_open_fire_scale;
                    ai.Extended_Visibility = s.Visibility * pVis * t.ai_visibility_scale;
                    ai.Combat_Speed_Rate = s.CombatSpeed * pSpd * t.ai_combat_speed_scale;
                    ai.No_Attack = t.ai_no_attack;
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


        // ---- effects (post components added to the volume profile) ----------

        private class EffectSet
        {
            public Bloom bloom;
            public Tonemapping tone;
            public ColorAdjustments color;
        }

        private readonly Dictionary<int, EffectSet> _effectSets = new Dictionary<int, EffectSet>();

        private void ApplyEffects(string why)
        {
            var e = _cfg.effects;
            if (e == null) return;
            try
            {
                foreach (var vol in Resources.FindObjectsOfTypeAll<Volume>())
                {
                    if (vol == null) continue;
                    var prof = vol.sharedProfile != null ? vol.sharedProfile : vol.profile;
                    if (prof == null || prof.components == null) continue;

                    EffectSet set = null;
                    foreach (var c in prof.components)
                    {
                        if (c == null) continue;
                        if (set == null) set = new EffectSet();
                        if (set.bloom == null) set.bloom = c.TryCast<Bloom>();
                        if (set.tone == null) set.tone = c.TryCast<Tonemapping>();
                        if (set.color == null) set.color = c.TryCast<ColorAdjustments>();
                    }
                    if (set == null) set = new EffectSet();

                    if (set.bloom == null)
                    {
                        set.bloom = ScriptableObject.CreateInstance<Bloom>();
                        prof.components.Add(set.bloom);
                    }
                    if (set.tone == null)
                    {
                        set.tone = ScriptableObject.CreateInstance<Tonemapping>();
                        prof.components.Add(set.tone);
                    }
                    if (set.color == null)
                    {
                        set.color = ScriptableObject.CreateInstance<ColorAdjustments>();
                        prof.components.Add(set.color);
                    }

                    set.bloom.threshold.value = Mathf.Clamp(e.bloom_threshold, 0f, 4f);
                    set.bloom.intensity.value = Mathf.Clamp(e.bloom, 0f, 4f);
                    set.bloom.scatter.value = 0.7f;
                    try { set.bloom.highQualityFiltering.value = true; } catch { }
                    set.bloom.active = e.bloom > 0.001f;

                    string tm = (e.tonemapping ?? "none").ToLowerInvariant();
                    set.tone.mode.value = tm == "aces" ? TonemappingMode.ACES
                                        : tm == "neutral" ? TonemappingMode.Neutral
                                        : TonemappingMode.None;
                    set.tone.active = tm != "none";

                    set.color.postExposure.value = Mathf.Clamp(e.exposure, -5f, 5f);
                    set.color.contrast.value = Mathf.Clamp(e.contrast, -100f, 100f);
                    set.color.saturation.value = Mathf.Clamp(e.saturation, -100f, 100f);
                    set.color.active = Math.Abs(e.exposure) > 0.001f
                                    || Math.Abs(e.contrast) > 0.001f
                                    || Math.Abs(e.saturation) > 0.001f;
                    break;   // first volume is enough; profiles are shared per scene
                }
            }
            catch (Exception ex) { LoggerInstance.Warning("effects pass: " + ex.Message); }
        }

    }
}

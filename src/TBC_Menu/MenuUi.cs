// The in-game menu. Unity stripped IMGUI out of this build, so this is uGUI:
// plain Text/Image objects plus manual hit-testing against Input.mousePosition.
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
        // ---- menu (uGUI; IMGUI is stripped out of this build) ---------------


        private GameObject _root;
        private Transform _canvasTr;
        private Font _font;
        private readonly List<Row> _rows = new List<Row>();
        // The player's gun, held unloaded while the menu is open.
        //
        // The game's mouse input scripts (Cannon_Fire_Input_01_Mouse_CS and friends) are not
        // Update-driven - they expose Get_Input(), which the consuming script calls every frame -
        // so disabling the component does nothing at all. Measured in 1.7.2: four of them were
        // disabled and a click still fired through the open menu. What the fire path does respect is
        // Cannon_Fire_CS.Is_Loaded (it returns before reading the fire key), so that is what is held
        // down here - only on the player's tank, so the AI keeps shooting.
        private readonly List<Cannon_Fire_CS> _mutedFire = new List<Cannon_Fire_CS>();
        private readonly List<bool> _mutedFireWasLoaded = new List<bool>();
        private int _builtTab = -1;

        private class Row
        {
            public Text labelText;
            public Text valueText;
            public Rect rect;
            public Action<int> onClick;
            public Func<string> value;
        }

        private static readonly string[] TabNames = { "Resolution", "Visuals", "Quality", "Effects", "Vehicle", "Tuning" };
        private const float PanelX = 40f, PanelY = 50f, PanelW = 780f, RowH = 34f;
        private const float LabelX = 56f, LabelW = 520f, ValueX = 600f, ValueW = 200f;

        private void BuildUI()
        {
            if (_root != null) return;
            try
            {
                _font = FindFont();
                _root = new GameObject("TBC_Menu_Canvas");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;
                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                scaler.referencePixelsPerUnit = 100f;
                _canvasTr = _root.transform;
                LoggerInstance.Msg($"UI built (font={(_font == null ? "NULL" : _font.name)})");
            }
            catch (Exception e) { LoggerInstance.Error("UI build failed: " + e.Message); }
        }

        private Font FindFont()
        {
            try
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
                    if (t != null && t.font != null) return t.font;
            }
            catch { }
            try
            {
                foreach (var fo in Resources.FindObjectsOfTypeAll<Font>())
                    if (fo != null) return fo;
            }
            catch { }
            return null;
        }

        private Text MakeText(string name, float x, float y, float w, float h, int size, Color col, TextAnchor anchor)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_canvasTr, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var tx = go.AddComponent<Text>();
            tx.font = _font;
            tx.fontSize = size;
            tx.color = col;
            tx.alignment = anchor;
            tx.horizontalOverflow = HorizontalWrapMode.Overflow;
            tx.verticalOverflow = VerticalWrapMode.Overflow;
            tx.raycastTarget = false;
            return tx;
        }

        private void MakeImage(float x, float y, float w, float h, Color col)
        {
            var go = new GameObject("panel");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_canvasTr, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.color = col;
            img.raycastTarget = false;
        }

        private void RebuildMenu()
        {
            if (_root == null) return;
            for (int i = _canvasTr.childCount - 1; i >= 0; i--)
            {
                var ch = _canvasTr.GetChild(i);
                if (ch != null) UnityEngine.Object.Destroy(ch.gameObject);
            }
            _rows.Clear();

            float total = PanelY + 128f;
            if (_tab == 0) total += RowH * 7f;
            else if (_tab == 1) total += RowH * 20f;
            else if (_tab == 2) total += RowH * 6f;
            else if (_tab == 3) total += RowH * 7f;
            else if (_tab == 4) total += RowH * 19f;
            else total += RowH * 16f;
            total += RowH * 4f;
            MakeImage(PanelX, PanelY, PanelW, total, new Color(0.02f, 0.02f, 0.05f, 0.95f));
            MakeText("title", LabelX, PanelY + 10f, PanelW - 32f, 28f, 24, Color.white, TextAnchor.MiddleLeft).text = "TBC Menu   [F7]";

            float y = PanelY + 46f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int captured = i;
                var tx = MakeText("tab" + i, LabelX + i * 124f, y, 118f, RowH, 20,
                    _tab == i ? new Color(1f, 0.85f, 0.2f) : new Color(0.7f, 0.78f, 0.9f), TextAnchor.MiddleLeft);
                tx.text = (_tab == i ? "> " : "   ") + TabNames[i];
                _rows.Add(new Row
                {
                    labelText = tx,
                    rect = new Rect(LabelX + i * 124f, y, 118f, RowH),
                    onClick = d => { _tab = captured; RebuildMenu(); },
                });
            }
            y += RowH + 10f;

            if (_tab == 0) BuildResolutionRows(ref y);
            else if (_tab == 1) BuildVisualRows(ref y);
            else if (_tab == 2) BuildQualityRows(ref y);
            else if (_tab == 3) BuildEffectRows(ref y);
            else if (_tab == 4) BuildVehicleRows(ref y);
            else BuildTuningRows(ref y);

            y += 8f;
            AddRow(ref y, "Save settings", () => "click", d => Save(), null, false);
            // Load() replaces _cfg with a fresh object and every row closure captured its section of the
        // old one, so the menu has to be rebuilt or edits made after a reload go nowhere.
        AddRow(ref y, "Reload from disk", () => "click", d => { Load(); ApplyAll("reload"); RefreshEditTankList(); _builtTab = -1; }, null, false);
            AddRow(ref y, "Close  (F7)", () => "click", d => Toggle(), null, false);
            AddRow(ref y, "left half = down / right half = up", () => "", null, new Color(0.6f, 0.65f, 0.75f));

            _builtTab = _tab;
        }

        private const float ArrowW = 34f;

        // arrows = true draws clickable "<" and ">" buttons either side of the value,
        // so decreasing is a visible target instead of invisible blank space.
        private void AddRow(ref float y, string label, Func<string> value, Action<int> onClick,
                            Color? valueCol = null, bool arrows = true)
        {
            var lt = MakeText("l", LabelX, y, LabelW, RowH, 20, Color.white, TextAnchor.MiddleLeft);
            lt.text = label;

            Text vt = null;
            if (value != null)
            {
                float vx = arrows ? ValueX + ArrowW : ValueX;
                float vw = arrows ? ValueW - ArrowW * 2f : ValueW;
                vt = MakeText("v", vx, y, vw, RowH, 20, valueCol ?? new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleRight);
                vt.text = value();
            }

            if (onClick == null)
            {
                _rows.Add(new Row { labelText = lt, valueText = vt, value = value, onClick = null });
            }
            else if (!arrows)
            {
                _rows.Add(new Row
                {
                    labelText = lt, valueText = vt, value = value, onClick = onClick,
                    rect = new Rect(ValueX, y, ValueW, RowH),
                });
            }
            else
            {
                var left = MakeText("larrow", ValueX, y, ArrowW, RowH, 20, new Color(0.75f, 0.85f, 1f), TextAnchor.MiddleCenter);
                left.text = "<";
                var right = MakeText("rarrow", ValueX + ValueW - ArrowW, y, ArrowW, RowH, 20, new Color(0.75f, 0.85f, 1f), TextAnchor.MiddleCenter);
                right.text = ">";

                _rows.Add(new Row { labelText = left, value = null, onClick = d => onClick(-1), rect = new Rect(ValueX, y, ArrowW, RowH) });
                _rows.Add(new Row { labelText = null, valueText = vt, value = value, onClick = null });
                _rows.Add(new Row { labelText = right, value = null, onClick = d => onClick(1), rect = new Rect(ValueX + ValueW - ArrowW, y, ArrowW, RowH) });

                // fallback: the whole row is live too - left half down, right half up.
                // registered last so the arrows take priority.
                _rows.Add(new Row
                {
                    labelText = null, valueText = null, value = null, onClick = onClick,
                    rect = new Rect(LabelX, y, (ValueX - LabelX) + ValueW, RowH),
                });
            }
            y += RowH;
        }

        private void AddToggleRow(ref float y, string label, Func<bool> get, Action<bool> set)
            => AddRow(ref y, label, () => get() ? "ON" : "OFF", d => set(!get()));

        private void AddFloatRow(ref float y, string label, Func<float> get, Action<float> set, float min, float max, float step, string fmt)
            => AddRow(ref y, label, () => get().ToString(fmt), d => set(Mathf.Clamp(get() + d * step, min, max)));

        private void AddIntRow(ref float y, string label, Func<int> get, Action<int> set, int min, int max, int step)
            => AddRow(ref y, label, () => get().ToString(), d => set(Mathf.Clamp(get() + d * step, min, max)));

        private void AddChoiceRow(ref float y, string label, string[] options, Func<string> get, Action<string> set)
            => AddRow(ref y, label, get, d =>
            {
                int i = Array.IndexOf(options, get());
                if (i < 0) i = 0;
                i = (i + (d > 0 ? 1 : options.Length - 1)) % options.Length;
                set(options[i]);
            });

        private void BuildResolutionRows(ref float y)
        {
            var r = _cfg.resolution;
            AddIntRow(ref y, "Width", () => r.width, v => { r.width = v; ApplyResolution(); }, 640, 3840, 40);
            AddIntRow(ref y, "Height", () => r.height, v => { r.height = v; ApplyResolution(); }, 480, 2160, 30);
            AddChoiceRow(ref y, "Fullscreen mode", new[] { "Exclusive", "Borderless", "Maximized", "Windowed" },
                () => new[] { "Exclusive", "Borderless", "Maximized", "Windowed" }[Mathf.Clamp(r.fullscreen_mode, 0, 3)],
                s => { r.fullscreen_mode = Math.Max(0, Array.IndexOf(new[] { "Exclusive", "Borderless", "Maximized", "Windowed" }, s)); ApplyResolution(); });
            AddFloatRow(ref y, "Internal scale (breaks aim)", () => r.internal_scale, v => { r.internal_scale = v; SyncBuffer("menu"); }, 0.1f, 2f, 0.05f, "F2");
            AddRow(ref y, "  use Render scale below", () => "safe supersampling", null, new Color(0.6f, 0.65f, 0.75f));
            AddToggleRow(ref y, "Render direct to screen", () => r.bypass_buffer, b => { r.bypass_buffer = b; ApplyBufferBypass(); });
            AddRow(ref y, "Render buffer", () =>
            {
                var rt = FindBuffer();
                return rt == null ? "none" : rt.width + "x" + rt.height;
            }, d => SyncBuffer("menu"));
            AddRow(ref y, "Screen now", () => Screen.width + "x" + Screen.height, null, new Color(0.7f, 0.78f, 0.9f));
        }

        private void BuildVisualRows(ref float y)
        {
            var v = _cfg.visuals;
            AddFloatRow(ref y, "Film grain", () => v.film_grain, x => { v.film_grain = x; ApplyVisuals("menu"); }, 0f, 1f, 0.05f, "F2");
            AddFloatRow(ref y, "Chromatic aberration", () => v.chromatic_aberration, x => { v.chromatic_aberration = x; ApplyVisuals("menu"); }, 0f, 1f, 0.05f, "F2");
            AddFloatRow(ref y, "Vignette", () => v.vignette, x => { v.vignette = x; ApplyVisuals("menu"); }, 0f, 1f, 0.05f, "F2");
            AddFloatRow(ref y, "Fog density scale", () => v.fog_density_scale, x => { v.fog_density_scale = x; ApplyFog("menu"); }, 0f, 1.5f, 0.1f, "F2");
            AddChoiceRow(ref y, "Antialiasing", new[] { "off", "fxaa", "smaa", "taa" }, () => v.antialiasing ?? "off", s => { v.antialiasing = s; ApplyVisuals("menu"); });
            AddToggleRow(ref y, "Dithering", () => v.dithering, b => { v.dithering = b; ApplyVisuals("menu"); });
            AddIntRow(ref y, "Texture aniso", () => v.texture_aniso, x => { v.texture_aniso = x; _assetsDone.Clear(); ApplyAssets("menu"); }, 0, 16, 2);

            var c = _cfg.camera;
            AddRow(ref y, "Commander view (F6)", () =>
                CommanderWanted ? (_commanderActive ? "ON" : "waiting") : "OFF", d =>
            {
                ToggleCommanderView();
                c.commander_view = CommanderWanted;
            });
            AddRow(ref y, "Editing", () => SeatLabel, null, new Color(0.6f, 0.65f, 0.75f));
            AddFloatRow(ref y, "Commander height (Y)", () => SeatHeight, x => SeatHeight = x, 0.1f, 5f, 0.05f, "F2");
            AddFloatRow(ref y, "Commander forward (Z)", () => SeatForward, x => SeatForward = x, -2f, 4f, 0.05f, "F2");
            AddFloatRow(ref y, "Commander right (X)", () => SeatRight, x => SeatRight = x, -2f, 2f, 0.05f, "F2");
            AddFloatRow(ref y, "Commander FOV", () => SeatFov, x => SeatFov = x, 30f, 110f, 2f, "F0");
            AddRow(ref y, "Alt + arrows / PgUpDn / wheel", () => "move eye", null, new Color(0.6f, 0.65f, 0.75f));
            AddRow(ref y, "Alt + S", () => "save seat", null, new Color(0.6f, 0.65f, 0.75f));
            AddToggleRow(ref y, "Commander follows turret", () => c.commander_follow_turret, b => { c.commander_follow_turret = b; ReapplyCommander(); });
            AddToggleRow(ref y, "Commander free look", () => c.commander_free_look, b => { c.commander_free_look = b; ReapplyCommander(); });
            AddFloatRow(ref y, "Commander nudge speed", () => c.commander_nudge_speed, x => c.commander_nudge_speed = x, 0.2f, 6f, 0.1f, "F1");
        }

        private void ReapplyCommander()
        {
            if (!_commanderActive) return;
            ExitCommanderView();      // releases the camera and clears the request
            ToggleCommanderView();    // ... so ask for it again with the new settings
        }

        private void BuildQualityRows(ref float y)
        {
            var q = _cfg.quality;
            AddRow(ref y, "MSAA", () => q.msaa + "x", d =>
            {
                int[] steps = { 1, 2, 4, 8 };
                int i = Array.IndexOf(steps, q.msaa); if (i < 0) i = 0;
                i = (i + d + steps.Length) % steps.Length;
                q.msaa = steps[i];
                ApplyQuality("menu"); SyncBuffer("menu");
            });
            AddRow(ref y, "Main light shadowmap", () => q.main_light_shadow_res.ToString(), d =>
            {
                int[] steps = { 1024, 2048, 4096 };
                int i = Array.IndexOf(steps, q.main_light_shadow_res); if (i < 0) i = 0;
                i = Mathf.Clamp(i + d, 0, steps.Length - 1);
                q.main_light_shadow_res = steps[i];
                ApplyQuality("menu");
            });
            AddChoiceRow(ref y, "Soft shadow quality", new[] { "use", "low", "medium", "high" },
                () => q.soft_shadow_quality ?? "use", s => { q.soft_shadow_quality = s; ApplyQuality("menu"); });
            AddFloatRow(ref y, "Shadow distance", () => q.shadow_distance, x => { q.shadow_distance = x; ApplyQuality("menu"); }, 64f, 400f, 16f, "F0");
            AddFloatRow(ref y, "Render scale", () => q.render_scale, x => { q.render_scale = x; ApplyQuality("menu"); }, 0.5f, 2f, 0.1f, "F2");
            AddRow(ref y, "note", () => "render scale > 1 = supersampling", null, new Color(0.6f, 0.65f, 0.75f));
        }


        private void BuildEffectRows(ref float y)
        {
            var e = _cfg.effects;
            AddChoiceRow(ref y, "Tonemapping", new[] { "none", "neutral", "aces" }, () => e.tonemapping ?? "none", s => { e.tonemapping = s; ApplyEffects("menu"); });
            AddFloatRow(ref y, "Bloom intensity", () => e.bloom, x => { e.bloom = x; ApplyEffects("menu"); }, 0f, 2f, 0.1f, "F2");
            AddFloatRow(ref y, "Bloom threshold", () => e.bloom_threshold, x => { e.bloom_threshold = x; ApplyEffects("menu"); }, 0f, 2f, 0.1f, "F2");
            AddFloatRow(ref y, "Exposure", () => e.exposure, x => { e.exposure = x; ApplyEffects("menu"); }, -2f, 2f, 0.1f, "F2");
            AddFloatRow(ref y, "Contrast", () => e.contrast, x => { e.contrast = x; ApplyEffects("menu"); }, -50f, 50f, 5f, "F0");
            AddFloatRow(ref y, "Saturation", () => e.saturation, x => { e.saturation = x; ApplyEffects("menu"); }, -50f, 50f, 5f, "F0");
        }

        private void BuildVehicleRows(ref float y)
        {
            var v = _cfg.vehicle;
            AddToggleRow(ref y, "Enable vehicle overrides", () => v.enabled, b =>
            {
                v.enabled = b;
                if (b) ApplyVehicleOverrides();
                else RestoreVehicleOriginals();
            });
            AddFloatRow(ref y, "Speed scale (all tanks)", () => v.speed_scale, x => { v.speed_scale = x; ApplyVehicleOverrides(); }, 0.05f, 3f, 0.05f, "F2");
            AddFloatRow(ref y, "Torque scale (all tanks)", () => v.torque_scale, x => { v.torque_scale = x; ApplyVehicleOverrides(); }, 0.05f, 3f, 0.05f, "F2");
            AddFloatRow(ref y, "Traverse scale (all tanks)", () => v.traverse_scale, x => { v.traverse_scale = x; ApplyVehicleOverrides(); }, 0.05f, 3f, 0.05f, "F2");
            AddToggleRow(ref y, "Player tank only", () => v.player_only, b => { v.player_only = b; ApplyVehicleOverrides(); });

            RefreshEditTankList();
            string tank = EditTankName;

            AddRow(ref y, "--- per tank (absolute) ---", () => (_vehEditIndex + 1) + "/" + _vehEditNames.Count, null, new Color(0.6f, 0.65f, 0.75f), false);
            AddRow(ref y, "Tank", () => tank, d => CycleEditTank(d));

            AddRow(ref y, "  max speed m/s", () => OptLabel(EditEntry(false)?.max_speed, "F2"), d =>
            {
                var e = EditEntry(true);
                if (e == null) return;
                float cur = e.max_speed ?? GameMaxSpeedOf(EditTankName) ?? 10f;
                e.max_speed = Mathf.Clamp(cur + d * 0.5f, 0.5f, 30f);
                ApplyVehicleOverrides();
            });
            AddRow(ref y, "  traverse deg/s", () => OptLabel(EditEntry(false)?.traverse, "F1"), d =>
            {
                var e = EditEntry(true);
                if (e == null) return;
                float cur = e.traverse ?? GameTraverseOf(EditTankName) ?? 30f;
                e.traverse = Mathf.Clamp(cur + d * 1f, 0.5f, 120f);
                ApplyVehicleOverrides();
            });
            AddRow(ref y, "  elevation deg/s", () => OptLabel(EditEntry(false)?.elevation, "F1"), d =>
            {
                var e = EditEntry(true);
                if (e == null) return;
                float cur = e.elevation ?? 20f;
                e.elevation = Mathf.Clamp(cur + d * 1f, 0.5f, 90f);
                ApplyVehicleOverrides();
            });
            AddRow(ref y, "  torque scale", () => OptLabel(EditEntry(false)?.torque_scale, "F2"), d =>
            {
                var e = EditEntry(true);
                if (e == null) return;
                float cur = e.torque_scale ?? 1f;
                e.torque_scale = Mathf.Clamp(cur + d * 0.05f, 0.05f, 3f);
                ApplyVehicleOverrides();
            });
            AddRow(ref y, "  accel time s", () => OptLabel(EditEntry(false)?.accel_time, "F1"), d =>
            {
                var e = EditEntry(true);
                if (e == null) return;
                float cur = e.accel_time ?? (GameAccelTimeOf(EditTankName) ?? 10f);
                e.accel_time = Mathf.Clamp(cur + d * 2f, 1f, 120f);
                ApplyVehicleOverrides();
            });
            AddRow(ref y, "  decel time s", () => OptLabel(EditEntry(false)?.decel_time, "F1"), d =>
            {
                var e = EditEntry(true);
                if (e == null) return;
                float cur = e.decel_time ?? (GameDecelTimeOf(EditTankName) ?? 2f);
                e.decel_time = Mathf.Clamp(cur + d * 0.5f, 0.1f, 60f);
                ApplyVehicleOverrides();
            });
            AddRow(ref y, "  clear this tank", () => "click", d =>
            {
                var cfg = _cfg.vehicle;
                string tank = EditTankName;
                if (cfg.per_tank != null) cfg.per_tank.Remove(tank);
                // Put the game's own values back for that tank before the global scales are
                // re-applied, or the ramp, the flags and the cached rates stay behind.
                foreach (var drive in UnityEngine.Object.FindObjectsOfType<Drive_Control_CS>())
                {
                    if (drive == null) continue;
                    if (TankNameOf(drive.transform) != tank) continue;
                    RestoreTankOriginals(drive);
                }
                ApplyVehicleOverrides();
                _status = "cleared " + tank + " - game values restored";
            }, null, false);
            AddRow(ref y, "Load realism table", () => "write " + RealismTable.Length + " tanks", d => LoadRealismTable(), null, false);
            AddRow(ref y, "Dump vehicle values", () => "write json", d => DumpVehicles(), null, false);

            AddRow(ref y, "live", () => PlayerSpeedLabel(), null, new Color(0.6f, 0.9f, 0.7f), false);
            AddRow(ref y, "applies to", () => v.player_only ? "player only, every 2s" : "every tank, every 2s", null, new Color(0.6f, 0.65f, 0.75f), false);
        }

        private static string OptLabel(float? value, string fmt)
            => value.HasValue ? value.Value.ToString(fmt) : "game";

        private void BuildTuningRows(ref float y)
        {
            var t = _cfg.tuning;
            AddToggleRow(ref y, "Enable tuning", () => t.enabled, b => { t.enabled = b; ApplyTuningRuntime("menu"); });
            AddFloatRow(ref y, "Attack scale", () => t.attack, x => { t.attack = x; ApplyTuningRuntime("menu"); }, 0.2f, 3f, 0.1f, "F2");
            AddFloatRow(ref y, "Defence scale", () => t.defence, x => { t.defence = x; ApplyTuningRuntime("menu"); }, 0.2f, 3f, 0.1f, "F2");
            AddFloatRow(ref y, "Drive scale", () => t.drive, x => { t.drive = x; ApplyTuningRuntime("menu"); }, 0.2f, 3f, 0.1f, "F2");
            AddFloatRow(ref y, "Turret scale", () => t.turret, x => { t.turret = x; ApplyTuningRuntime("menu"); }, 0.2f, 3f, 0.1f, "F2");
            AddFloatRow(ref y, "Reload scale", () => t.reload, x => { t.reload = x; ApplyTuningRuntime("menu"); }, 0.2f, 3f, 0.1f, "F2");
            AddFloatRow(ref y, "Bullet scale", () => t.bullet, x => { t.bullet = x; ApplyTuningRuntime("menu"); }, 0.2f, 3f, 0.1f, "F2");
            AddToggleRow(ref y, "Max tuning levels", () => t.max_levels, b => { t.max_levels = b; ApplyTuningRuntime("menu"); });
            AddChoiceRow(ref y, "AI preset", new[] { "off", "green", "veteran", "ace" }, () => t.ai_preset ?? "off", s => { t.ai_preset = s; ApplyTuningRuntime("menu"); });
            AddFloatRow(ref y, "AI lead accuracy", () => t.ai_lead_scale, x => { t.ai_lead_scale = x; ApplyTuningRuntime("menu"); }, 0.2f, 2.5f, 0.1f, "F2");
            AddFloatRow(ref y, "AI open fire dist", () => t.ai_open_fire_scale, x => { t.ai_open_fire_scale = x; ApplyTuningRuntime("menu"); }, 0.2f, 2.5f, 0.1f, "F2");
            AddToggleRow(ref y, "AI passive", () => t.ai_no_attack, b => { t.ai_no_attack = b; ApplyTuningRuntime("menu"); });
            AddToggleRow(ref y, "Unlock all tanks", () => t.unlock_all, b => { t.unlock_all = b; ApplyTuningRuntime("menu"); });
            AddIntRow(ref y, "Points", () => t.points, x => { t.points = x; ApplyTuningRuntime("menu"); }, 0, 200000, 5000);
        }

        private void UpdateMenu()
        {
            if (_root == null) return;
            _root.SetActive(_open);
            if (!_open) return;
            if (_tab != _builtTab) RebuildMenu();

            foreach (var r in _rows)
                if (r.valueText != null && r.value != null) r.valueText.text = r.value();

            try
            {
                Vector2 mp = Input.mousePosition;
                var p = new Vector2(mp.x, Screen.height - mp.y);
                if (!Input.GetMouseButtonDown(0)) return;
                foreach (var r in _rows)
                {
                    if (r.onClick == null) continue;
                    if (p.y < r.rect.y || p.y > r.rect.y + r.rect.height) continue;
                    if (p.x < r.rect.x || p.x > r.rect.x + r.rect.width) continue;
                    float rel = (p.x - r.rect.x) / r.rect.width;
                    r.onClick(rel < 0.5f ? -1 : 1);
                    break;   // the list may have been rebuilt
                }
            }
            catch (Exception e) { LoggerInstance.Warning("menu input: " + e.Message); }
        }

        private void SetGameInputSuspended(bool suspend)
        {
            try
            {
                if (suspend) BlockPlayerFire();
                else ReleasePlayerFire();
            }
            catch (Exception e) { LoggerInstance.Warning("fire block: " + e.Message); }
        }

        // Also runs on the 2 s tick: a respawn or a new mission brings a fresh cannon that has to be
        // held down too, and a reload that finished while the menu was open puts Is_Loaded back up.
        // Re-asserts the block for cannons that appeared since (respawn, next mission) AND records
        // them, so the release path knows about every gun it held down. Without this a respawned gun
        // was never released and stayed unloaded.
        private void EnsurePlayerFireBlocked()
        {
            try
            {
                Transform player = PlayerTankRoot();
                if (player == null) return;
                foreach (var fire in UnityEngine.Object.FindObjectsOfType<Cannon_Fire_CS>())
                {
                    if (fire == null) continue;
                    if (TankRootOf(fire.transform) != player) continue;   // the AI keeps its guns
                    if (!_mutedFire.Contains(fire))
                    {
                        _mutedFire.Add(fire);
                        _mutedFireWasLoaded.Add(fire.Is_Loaded);   // remember, do not assume
                    }
                    fire.Is_Loaded = false;
                }
            }
            catch (Exception e) { LoggerInstance.Warning("fire block: " + e.Message); }
        }

        private void BlockPlayerFire()
        {
            Transform player = PlayerTankRoot();
            if (player == null) return;
            _mutedFire.Clear();
            _mutedFireWasLoaded.Clear();
            foreach (var fire in UnityEngine.Object.FindObjectsOfType<Cannon_Fire_CS>())
            {
                if (fire == null) continue;
                if (TankRootOf(fire.transform) != player) continue;
                _mutedFire.Add(fire);
                _mutedFireWasLoaded.Add(fire.Is_Loaded);
                fire.Is_Loaded = false;
            }
            if (_mutedFire.Count > 0)
            {
                int wereLoaded = 0;
                foreach (var b in _mutedFireWasLoaded) if (b) wereLoaded++;
                LoggerInstance.Msg($"menu open: player gun held unloaded ({_mutedFire.Count} cannon(s), " +
                                   $"{wereLoaded} were loaded)");
            }
        }

        // Puts back the state each cannon had when it was held down - a gun that was already empty
        // (no ammo, mid-reload) must not come back loaded, and a gun we did not hold must not be
        // touched at all. Cannons that vanished with their scene are simply dropped.
        private void ReleasePlayerFire()
        {
            int released = 0, reloading = 0, gone = 0;
            for (int i = 0; i < _mutedFire.Count; i++)
            {
                var fire = _mutedFire[i];
                if (fire == null) { gone++; continue; }
                bool wasLoaded = i < _mutedFireWasLoaded.Count && _mutedFireWasLoaded[i];
                bool busy = false;
                try { busy = fire.reloadCoroutine != null; } catch { }
                if (busy) { reloading++; continue; }        // the game will set the flag itself
                try { fire.Is_Loaded = wasLoaded; } catch { }
                released++;
            }
            if (released + reloading + gone > 0)
                LoggerInstance.Msg($"menu closed: gun released ({released} restored, " +
                                   $"{reloading} still reloading, {gone} gone)");
            _mutedFire.Clear();
            _mutedFireWasLoaded.Clear();
        }
    }
}
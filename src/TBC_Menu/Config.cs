// Configuration: the JSON shape, loading and saving.
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
    // ------------------------------------------------------------------ config

    public class ResolutionCfg
    {
        public int width = 1920;
        public int height = 1080;
        public int fullscreen_mode = 0;     // 0 exclusive, 1 borderless, 2 maximized, 3 windowed
        public float internal_scale = 1.0f; // 0 = leave the game's own buffer alone
        public bool fill_window = true;     // drop the fixed 16:9 letterbox
    }

    public class VisualCfg
    {
        public float film_grain = 0f;
        public float chromatic_aberration = 0f;
        public float vignette = 0f;
        public string antialiasing = "smaa";   // off | fxaa | smaa | taa
        public bool dithering = true;
        public float fog_density_scale = 1f;
        public int texture_aniso = 8;
    }

    public class TuningCfg
    {
        public bool enabled = false;
        public bool max_levels = false;
        public float attack = 1f, defence = 1f, drive = 1f, turret = 1f, reload = 1f, bullet = 1f;
        public string ai_preset = "off";   // off | green | veteran | ace
        public float ai_lead_scale = 1f;
        public float ai_open_fire_scale = 1f;
        public float ai_visibility_scale = 1f;
        public float ai_combat_speed_scale = 1f;
        public bool ai_no_attack = false;
        public bool unlock_all = false;
        public int points = 0;
    }

    public class QualityCfg
    {
        public int msaa = 1;                    // 1, 2, 4, 8
        public int main_light_shadow_res = 1024; // 1024, 2048, 4096
        public string soft_shadow_quality = "use"; // use, low, medium, high
        public float shadow_distance = 128f;
        public float render_scale = 1f;         // >1 = supersampling
    }

    public class EffectsCfg
    {
        public string tonemapping = "none";     // none, neutral, aces
        public float bloom = 0f;                // 0 = off
        public float bloom_threshold = 1f;
        public float exposure = 0f;
        public float contrast = 0f;
        public float saturation = 0f;
    }

    public class CameraCfg
    {
        public bool commander_view = false;      // auto-enter on a mission scene
        public float commander_height = 1.2f;    // metres above the turret origin
        public float commander_forward = 0.2f;   // metres forward from the turret origin
        public float commander_right = 0f;       // metres to the right of the turret origin
        public float commander_nudge_speed = 1.5f; // metres per second while adjusting
        public float commander_fov = 70f;
        public float commander_sensitivity = 2f;
        public bool commander_follow_turret = true; // view yaw is relative to the turret
        public bool commander_free_look = false;    // true: mouse looks around instead of turning the gun
    }

    public class MenuConfig
    {
        public bool enabled = true;
        public string menu_key = "F7";
        public ResolutionCfg resolution = new ResolutionCfg();
        public VisualCfg visuals = new VisualCfg();
        public QualityCfg quality = new QualityCfg();
        public EffectsCfg effects = new EffectsCfg();
        public TuningCfg tuning = new TuningCfg();
        public CameraCfg camera = new CameraCfg();
    }


    public partial class MenuMod
    {
        // ---- config -------------------------------------------------------

        private void Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    File.WriteAllText(_configPath, JsonConvert.SerializeObject(new MenuConfig(), Formatting.Indented));
                    LoggerInstance.Msg("Wrote default config: " + _configPath);
                }
                var cfg = JsonConvert.DeserializeObject<MenuConfig>(File.ReadAllText(_configPath));
                if (cfg != null) _cfg = cfg;
                LoggerInstance.Msg("Config loaded from " + _configPath);
            }
            catch (Exception e) { LoggerInstance.Error("config load failed: " + e.Message); }
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_cfg, Formatting.Indented));
                _status = "saved to " + ConfigName;
            }
            catch (Exception e) { _status = "save failed: " + e.Message; }
        }

    }
}

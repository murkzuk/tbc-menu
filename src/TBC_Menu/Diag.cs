// Diagnostics: F8 dumps the render/UI/aim geometry to the log. Temporary.
using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppChobiAssets.PTM;

namespace TbcMenu
{
    public partial class MenuMod
    {
        private float _nextDiag;
        private int _diagCount;

        private void ResetDiag() { _diagCount = 0; _nextDiag = 0f; }

        // Automatic capture while the buffer resize is active: three dumps per scene, 6 s apart,
        // so the geometry is recorded even if nobody presses F8.
        private void AutoDiag()
        {
            try
            {
                if (_cfg == null || _cfg.resolution == null || _cfg.resolution.internal_scale <= 0f) return;
                if (Time.time < _nextDiag) return;
                _nextDiag = Time.time + 6f;
                if (_diagCount >= 3) return;
                _diagCount++;
                LoggerInstance.Msg("auto-diag " + _diagCount + "/3");
                DumpUiGeometry();
            }
            catch (Exception e) { LoggerInstance.Warning("auto-diag: " + e.Message); }
        }

        private void DumpUiGeometry()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var rt = FindBuffer();
                sb.AppendLine("=== UI/aim geometry ===");
                sb.AppendLine($"screen {Screen.width}x{Screen.height}  buffer {(rt == null ? "none" : rt.width + "x" + rt.height)}"
                              + (rt == null ? "" : $"  filter {rt.filterMode}  msaa {rt.antiAliasing}"));
                try { var mp = Input.mousePosition; sb.AppendLine($"mouse {mp.x:F0},{mp.y:F0}"); } catch { }

                foreach (var cam in Camera.allCameras)
                {
                    if (cam == null) continue;
                    string tgt = cam.targetTexture == null ? "screen" : (cam.targetTexture.width + "x" + cam.targetTexture.height);
                    var pr = cam.pixelRect;
                    var r = cam.rect;
                    sb.AppendLine($"camera '{cam.name}' depth {cam.depth} target {tgt} fov {cam.fieldOfView:F1} " +
                                  $"rect {r.x:F3},{r.y:F3},{r.width:F3},{r.height:F3} " +
                                  $"pixelRect {pr.x:F0},{pr.y:F0},{pr.width:F0},{pr.height:F0}");
                }

                foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
                {
                    if (c == null) continue;
                    string camName = c.worldCamera == null ? "-" : c.worldCamera.name;
                    sb.AppendLine($"canvas '{c.name}' mode {c.renderMode} sort {c.sortingOrder} worldCam {camName} " +
                                  $"scale {c.scaleFactor:F3} size {c.pixelRect.width:F0}x{c.pixelRect.height:F0}");
                }

                foreach (var sc in Resources.FindObjectsOfTypeAll<CanvasScaler>())
                {
                    if (sc == null) continue;
                    sb.AppendLine($"scaler on '{sc.gameObject.name}' mode {sc.uiScaleMode} factor {sc.scaleFactor:F3} " +
                                  $"ref {sc.referenceResolution.x:F0}x{sc.referenceResolution.y:F0} match {sc.matchWidthOrHeight:F2}");
                }

                foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (t == null) continue;
                    string n = t.name ?? "";
                    if (n.IndexOf("reticle", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Reticle", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var rtT = t.TryCast<RectTransform>();
                    string extra = rtT == null ? "" : $" anchored {rtT.anchoredPosition.x:F0},{rtT.anchoredPosition.y:F0} sizeDelta {rtT.sizeDelta.x:F0}x{rtT.sizeDelta.y:F0}";
                    sb.AppendLine($"reticle '{n}' pos {t.position.x:F0},{t.position.y:F0},{t.position.z:F0} lossyScale {t.lossyScale.x:F2}{extra}");
                }

                LoggerInstance.Msg(sb.ToString());
            }
            catch (Exception e) { LoggerInstance.Warning("diag: " + e.Message); }
        }
    }
}
using System.Linq;
using MrTerrainPainter.Editor.Services;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.EditorTools;

namespace MrTerrainPainter.Editor.Tools
{
    [Overlay(typeof(SceneView), "MTP Brush")]
    public class MTPBrushOverlay : Overlay
    {
        private bool subscribed;
        private bool ensuredWindow;




        public override VisualElement CreatePanelContent()
        {
            if (!subscribed)
            {
                subscribed = true;
                ToolManager.activeToolChanged += OnActiveToolChanged;
                // 初始化一次
                UpdateVisibility();
            }
            var cfg = MrTerrainPainter.Editor.Config.ConfigTools.LoadOrCreateAsset();
            var vt = MrTerrainPainter.Editor.Config.ConfigTools.GetBrushOverlayUxml(cfg);
            if (vt == null)
            {
                return new Label("Overlay UXML 未配置或未找到");
            }
            var root = vt.Instantiate();
            var style = MrTerrainPainter.Editor.Config.ConfigTools.GetStylesUss(cfg);
            if (style != null) root.styleSheets.Add(style);
            var brush = MTPBrushContext.Brush;

            BindSlider(root, "Size", 0.5f, 50f, () => brush.size, v => brush.size = v);
            BindSlider(root, "Strength", 0.1f, 10f, () => brush.strength, v => brush.strength = v);
            BindSlider(root, "Density", 0f, 5f, () => brush.densityScale, v => brush.densityScale = v);
            BindSlider(root, "Hardness", 0f, 1f, () => brush.hardness, v => brush.hardness = v);
            var dist = root.Q<UnityEditor.UIElements.EnumField>("Distribution");
            if (dist != null)
            {
                dist.Init(brush.distribution);
                dist.SetValueWithoutNotify(brush.distribution);
                dist.RegisterValueChangedCallback(evt => { brush.distribution = (DistributionType)evt.newValue; });
            }
            var mixExtra = root.Q<Toggle>("MixExtraProfiles");
            if (mixExtra != null)
            {
                mixExtra.SetValueWithoutNotify(brush.mixExtraProfiles);
                mixExtra.RegisterValueChangedCallback(evt => { brush.mixExtraProfiles = evt.newValue; });
            }
            var btnOpen = root.Q<Button>("OpenSettings");
            if (btnOpen != null)
            {
                btnOpen.clicked += () =>
                {
                    var win = MrTerrainPainter.Editor.MrTerrainPainterWindow.TryGet(out var existing) ? existing : MrTerrainPainter.Editor.MrTerrainPainterWindow.GetOrOpen();
                    if (win == null) return;
                    win.Show();
                    win.Focus();
                    EditorApplication.delayCall += () => { if (win != null) win.OpenPaintingSettings(); };
                };
            }
            return root;
        }

        private void OnActiveToolChanged()
        {
            bool isActive = ToolManager.activeToolType == typeof(MrTerrainPainter.Editor.Tools.MTPBrushTool);
            try { displayed = isActive; } catch { }
            if (isActive && !ensuredWindow)
            {
                ensuredWindow = true;
                if (!MrTerrainPainter.Editor.MrTerrainPainterWindow.TryGet(out var _))
                {
                    MrTerrainPainter.Editor.MrTerrainPainterWindow.GetOrOpen();
                }
            }
            if (!isActive)
            {
                ensuredWindow = false;
            }
        }

        private void UpdateVisibility()
        {
            OnActiveToolChanged();
        }

        public override void OnWillBeDestroyed()
        {
            if (subscribed)
            {
                ToolManager.activeToolChanged -= OnActiveToolChanged;
                subscribed = false;
            }
            base.OnWillBeDestroyed();
        }

        private void BindSlider(VisualElement root, string name, float min, float max, System.Func<float> getter, System.Action<float> setter)
        {
            var slider = root.Q<SliderInt>(name);
            if (slider == null) return;
            var hv = Mathf.Max(1, slider.highValue);
            var v = Mathf.RoundToInt(Mathf.InverseLerp(min, max, Mathf.Clamp(getter(), min, max)) * hv);
            slider.SetValueWithoutNotify(v);
            slider.RegisterValueChangedCallback(evt =>
            {
                var f = Mathf.Lerp(min, max, Mathf.Clamp01(evt.newValue / (float)hv));
                setter(f);
            });
        }
    }
}

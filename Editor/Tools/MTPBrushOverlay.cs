using System.Linq;
using MrTerrainPainter.Editor.Services;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Tools
{
    [Overlay(typeof(SceneView), "MTP Brush")]
    public class MTPBrushOverlay : Overlay
    {
        private const string AssetPath = "Assets/MrTerrPainterV1/Editor/MTPBrushOverlay.uxml";



        public override VisualElement CreatePanelContent()
        {
            var cfg = MrTerrainPainter.Editor.Config.ConfigTools.LoadOrCreateAsset();
            var vt = cfg.brushOverlayUxml ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetPath);
            var root = vt != null ? vt.Instantiate() : new VisualElement();
            if (cfg.stylesUss != null) root.styleSheets.Add(cfg.stylesUss);
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
                    var win = Resources.FindObjectsOfTypeAll<MrTerrainPainterWindow>().FirstOrDefault();
                    if (win == null) win = EditorWindow.GetWindow<MrTerrainPainterWindow>(false, "Mr Terrain Painter");
                    win.Show();
                    var method = win.GetType().GetMethod("OpenPaintingSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    method?.Invoke(win, null);
                };
            }
            return root;
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

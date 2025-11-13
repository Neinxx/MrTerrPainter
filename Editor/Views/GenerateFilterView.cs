using MrTerrainPainter.Editor.Services;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    // 生成过滤视图：封装 Generate 页的噪声/过滤控件绑定
    public class GenerateFilterView
    {
        private readonly VisualElement root;

        public GenerateFilterView(VisualElement root)
        {
            this.root = root;
        }

        public void Bind(VegetationGenerator.NoiseSettings noise)
        {
            if (root == null || noise == null) return; // 提前返回

            noise.enabled = false;

            var filterBtn = root.Q<Button>("GenerationFilter");
            var filterContent = root.Q<VisualElement>("FilterContent");
            if (filterBtn != null)
            {
                ApplyFilterUIState(filterContent, filterBtn, noise.enabled);
                filterBtn.clicked += () =>
                {
                    noise.enabled = !noise.enabled;
                    ApplyFilterUIState(filterContent, filterBtn, noise.enabled);
                };
            }

            var threshold = root.Q<SliderInt>("Threshold");
            if (threshold != null)
            {
                var max = Mathf.Max(1, threshold.highValue);
                threshold.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.Clamp01(noise.threshold) * max));
                threshold.RegisterValueChangedCallback(evt => { noise.threshold = Mathf.Clamp01(evt.newValue / (float)max); });
            }

            var invert = root.Q<Toggle>("InvertThreshold");
            if (invert != null)
            {
                invert.SetValueWithoutNotify(noise.invert);
                invert.RegisterValueChangedCallback(evt => { noise.invert = evt.newValue; });
            }

            var seed = root.Q<IntegerField>("NoiseSeed");
            if (seed != null)
            {
                seed.SetValueWithoutNotify(noise.seed);
                seed.RegisterValueChangedCallback(evt => { noise.seed = evt.newValue; });
            }

            float ToFloat(int i, float min, float max, int hv) => Mathf.Lerp(min, max, Mathf.Clamp01(i / (float)hv));

            var persistence = root.Q<SliderInt>("Persistence");
            if (persistence != null)
            {
                var hv = Mathf.Max(1, persistence.highValue);
                persistence.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.InverseLerp(0f, 1f, Mathf.Clamp(noise.persistence, 0f, 1f)) * hv));
                persistence.RegisterValueChangedCallback(evt => { noise.persistence = ToFloat(evt.newValue, 0f, 1f, hv); });
            }

            var lacunarity = root.Q<SliderInt>("Lacunarity");
            if (lacunarity != null)
            {
                var hv = Mathf.Max(1, lacunarity.highValue);
                lacunarity.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.InverseLerp(1f, 4f, Mathf.Clamp(noise.lacunarity, 1f, 4f)) * hv));
                lacunarity.RegisterValueChangedCallback(evt => { noise.lacunarity = ToFloat(evt.newValue, 1f, 4f, hv); });
            }

            var octaves = root.Q<IntegerField>("OctaveCount");
            if (octaves != null)
            {
                octaves.SetValueWithoutNotify(Mathf.Clamp(noise.octaves, 1, 6));
                octaves.RegisterValueChangedCallback(evt =>
                {
                    noise.octaves = Mathf.Clamp(evt.newValue, 1, 6);
                    octaves.SetValueWithoutNotify(noise.octaves);
                });
            }

            var scale = root.Q<SliderInt>("Scale");
            if (scale != null)
            {
                var hv = Mathf.Max(1, scale.highValue);
                scale.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.InverseLerp(1f, 200f, Mathf.Clamp(noise.scale, 1f, 200f)) * hv));
                scale.RegisterValueChangedCallback(evt => { noise.scale = ToFloat(evt.newValue, 1f, 200f, hv); });
            }
        }

        private void ApplyFilterUIState(VisualElement content, Button btn, bool enabled)
        {
            if (btn == null) return;
            var onColor = new Color(0.47f, 0.78f, 0.30f);
            var offColor = new Color(0.89f, 0.51f, 0.28f);
            var c = enabled ? onColor : offColor;
            btn.style.color = new StyleColor(c);
            if (enabled)
            {
                btn.RemoveFromClassList("mt-button");
                btn.AddToClassList("mt-button--activeG");
            }
            else
            {
                btn.RemoveFromClassList("mt-button--activeG");
                btn.AddToClassList("mt-button");
            }
            if (content != null) content.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
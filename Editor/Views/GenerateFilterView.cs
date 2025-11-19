using MrTerrainPainter.Editor.Services;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    public class GenerateFilterView
    {
        private readonly VisualElement root;

        private static float ToFloat(int i, float min, float max, int hv)
        {
            return Mathf.Lerp(min, max, Mathf.Clamp01(i / (float)Mathf.Max(1, hv)));
        }

        public GenerateFilterView(VisualElement root)
        {
            this.root = root;
        }

        public void Bind(VegetationGenerator.FilterSettings filter)
        {
            if (root == null || filter == null) return;

            // 【重要】不要在这里 new NoiseSettings，必须使用 filter 中已有的引用
            // 否则 UI 绑定的将是一个全新的临时对象，而不是 Session 中的数据
            if (filter.noise == null)
            {
                Debug.LogError("[MTP] Filter Settings has no noise object! UI binding failed.");
                return;
            }

            var noise = filter.noise;
            noise.enabled = true;

            BindFilterToggle(noise);
            BindNoise(noise);
            BindDistributionAndShape(filter);
            BindGeneral(filter);
            BindCluster(filter);
        }

        // ... ApplyFilterUIState 和 BindFilterToggle 保持不变 ...
        private void ApplyFilterUIState(VisualElement content, Button btn, bool enabled)
        {
            if (btn == null) return;
            var onColor = new Color(0.47f, 0.78f, 0.30f);
            var offColor = new Color(0.89f, 0.51f, 0.28f);
            btn.style.color = new StyleColor(enabled ? onColor : offColor);

            if (enabled) { btn.RemoveFromClassList("mt-button"); btn.AddToClassList("mt-button--activeG"); }
            else { btn.RemoveFromClassList("mt-button--activeG"); btn.AddToClassList("mt-button"); }

            if (content != null) content.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BindFilterToggle(VegetationGenerator.NoiseSettings noise)
        {
            var filterBtn = root.Q<Button>("GenerationFilter");
            var filterContent = root.Q<VisualElement>("FilterContent");
            if (filterBtn == null) return;

            ApplyFilterUIState(filterContent, filterBtn, noise.enabled);
            filterBtn.clicked += () =>
            {
                noise.enabled = !noise.enabled;
                ApplyFilterUIState(filterContent, filterBtn, noise.enabled);
            };
        }

        private void BindNoise(VegetationGenerator.NoiseSettings noise)
        {
            BindSlider("Threshold", 1, val => Mathf.RoundToInt(Mathf.Clamp01(noise.threshold) * val),
                (val, max) => noise.threshold = Mathf.Clamp01(val / max));

            BindToggle("InvertThreshold", () => noise.invert, v => noise.invert = v);
            BindInt("NoiseSeed", () => noise.seed, v => noise.seed = v);

            BindSlider("Persistence", 1, val => Mathf.RoundToInt(Mathf.InverseLerp(0f, 1f, noise.persistence) * val),
                (val, max) => noise.persistence = ToFloat((int)val, 0f, 1f, (int)max));

            BindSlider("Lacunarity", 1, val => Mathf.RoundToInt(Mathf.InverseLerp(1f, 4f, noise.lacunarity) * val),
                (val, max) => noise.lacunarity = ToFloat((int)val, 1f, 4f, (int)max));

            BindInt("OctaveCount", () => Mathf.Clamp(noise.octaves, 1, 6), v => noise.octaves = Mathf.Clamp(v, 1, 6));

            BindSlider("Scale", 1, val => Mathf.RoundToInt(Mathf.InverseLerp(1f, 200f, noise.scale) * val),
                (val, max) => noise.scale = ToFloat((int)val, 1f, 200f, (int)max));
        }

        private void BindDistributionAndShape(VegetationGenerator.FilterSettings filter)
        {
            // 【优化】直接初始化 EnumField，不要销毁重建，那样会破坏布局引用的稳定性
            BindEnum<DistributionType>("GenDistribution", filter.distribution, v => filter.distribution = v);
            BindEnum<BrushShape>("GenShape", filter.shape, v => filter.shape = v);
        }

        private void BindGeneral(VegetationGenerator.FilterSettings filter)
        {
            BindSlider("GenMinSpacingJitter", 1, val => Mathf.RoundToInt(Mathf.Clamp01(filter.minSpacingJitter) * val),
                (val, max) => filter.minSpacingJitter = Mathf.Clamp01(val / max));

            BindInt("GenMaxPoints", () => Mathf.Max(filter.maxPoints, 1), v => filter.maxPoints = Mathf.Max(1, v));

            BindSlider("AdaptiveMinFactor", 1, val => Mathf.RoundToInt(Mathf.InverseLerp(0.1f, 1.5f, filter.adaptiveMinFactor) * val),
                (val, max) => filter.adaptiveMinFactor = ToFloat((int)val, 0.1f, 1.5f, (int)max));

            BindSlider("AdaptiveMaxFactor", 1, val => Mathf.RoundToInt(Mathf.InverseLerp(1f, 3f, filter.adaptiveMaxFactor) * val),
                (val, max) => filter.adaptiveMaxFactor = ToFloat((int)val, 1f, 3f, (int)max));

            BindSlider("AdaptiveNoiseWeight", 1, val => Mathf.RoundToInt(Mathf.InverseLerp(0.25f, 4f, filter.adaptiveNoiseWeight) * val),
                (val, max) => filter.adaptiveNoiseWeight = ToFloat((int)val, 0.25f, 4f, (int)max));
        }

        private void BindCluster(VegetationGenerator.FilterSettings filter)
        {
            var fc = root.Q<Foldout>("GenCluster");
            if (fc == null) return;

            // 注意：这里需要传递 Foldout 作为 root 来查找子元素
            BindInt("GenClusterCount", () => Mathf.Max(filter.cluster.clusterCount, 1), v => { var c = filter.cluster; c.clusterCount = Mathf.Max(1, v); filter.cluster = c; }, fc);
            BindInt("GenChildPerCluster", () => Mathf.Max(filter.cluster.childPerCluster, 1), v => { var c = filter.cluster; c.childPerCluster = Mathf.Max(1, v); filter.cluster = c; }, fc);

            BindSlider("GenClusterRadius", 1, val => Mathf.RoundToInt(Mathf.InverseLerp(0.1f, 50f, filter.cluster.clusterRadius) * val),
                (val, max) => { var c = filter.cluster; c.clusterRadius = Mathf.Lerp(0.1f, 50f, val / max); filter.cluster = c; }, fc);

            BindSlider("GenChildJitter", 1, val => Mathf.RoundToInt(Mathf.InverseLerp(0f, 5f, filter.cluster.childJitter) * val),
                (val, max) => { var c = filter.cluster; c.childJitter = Mathf.Lerp(0f, 5f, val / max); filter.cluster = c; }, fc);
        }

        // --- 辅助绑定方法 (减少重复代码) ---

        private void BindSlider(string name, int minHighVal, System.Func<float, int> getter, System.Action<float, float> setter, VisualElement searchRoot = null)
        {
            var container = searchRoot ?? root;
            var slider = container.Q<SliderInt>(name);
            if (slider == null) return;

            var max = Mathf.Max(minHighVal, slider.highValue);
            slider.SetValueWithoutNotify(getter(max));
            slider.RegisterValueChangedCallback(evt => setter(evt.newValue, max));
        }

        private void BindInt(string name, System.Func<int> getter, System.Action<int> setter, VisualElement searchRoot = null)
        {
            var container = searchRoot ?? root;
            var field = container.Q<IntegerField>(name);
            if (field == null) return;
            field.SetValueWithoutNotify(getter());
            field.RegisterValueChangedCallback(evt => setter(evt.newValue));
        }

        private void BindToggle(string name, System.Func<bool> getter, System.Action<bool> setter)
        {
            var toggle = root.Q<Toggle>(name);
            if (toggle == null) return;
            toggle.SetValueWithoutNotify(getter());
            toggle.RegisterValueChangedCallback(evt => setter(evt.newValue));
        }

        private void BindEnum<T>(string name, System.Enum initialValue, System.Action<T> setter) where T : System.Enum
        {
            var field = root.Q<EnumField>(name);
            if (field == null) return;

            // 初始化 EnumField 的类型，这样 UI Toolkit 才知道显示什么选项
            field.Init(initialValue);
            field.SetValueWithoutNotify(initialValue);

            field.RegisterValueChangedCallback(evt =>
            {
                setter((T)evt.newValue);
            });
        }
    }
}
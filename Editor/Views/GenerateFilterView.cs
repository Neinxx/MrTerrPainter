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
            if (root == null || filter == null) return; // 提前返回

            var noise = filter.noise ?? (filter.noise = new VegetationGenerator.NoiseSettings());
            noise.enabled = true;
            BindFilterToggle(noise);
            BindNoise(noise);
            BindDistributionAndShape(filter);
            BindGeneral(filter);
            BindCluster(filter);
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

        private void BindDistributionAndShape(VegetationGenerator.FilterSettings filter)
        {
            var dist = root.Q<EnumField>("GenDistribution");
            if (dist != null)
            {
                if (dist.parent != null)
                {
                    var parent = dist.parent;
                    int idx = parent.IndexOf(dist);
                    var newDist = new EnumField("分布类型", filter.distribution);
                    newDist.name = "GenDistribution";
                    parent.Insert(idx, newDist);
                    dist.RemoveFromHierarchy();
                    dist = newDist;
                }
                dist.Init(filter.distribution);
                dist.SetValueWithoutNotify(filter.distribution);
                dist.focusable = true;
                dist.SetEnabled(true);
                dist.RegisterValueChangedCallback(evt =>
                {
                    var intVal = System.Convert.ToInt32(evt.newValue);
                    filter.distribution = (DistributionType)System.Enum.ToObject(typeof(DistributionType), intVal);
                });
            }
            var shape = root.Q<EnumField>("GenShape");
            if (shape != null)
            {
                if (shape.parent != null)
                {
                    var parent = shape.parent;
                    int idx = parent.IndexOf(shape);
                    var newShape = new EnumField("形状", filter.shape);
                    newShape.name = "GenShape";
                    parent.Insert(idx, newShape);
                    shape.RemoveFromHierarchy();
                    shape = newShape;
                }
                shape.Init(filter.shape);
                shape.SetValueWithoutNotify(filter.shape);
                shape.focusable = true;
                shape.SetEnabled(true);
                shape.RegisterValueChangedCallback(evt =>
                {
                    var intVal = System.Convert.ToInt32(evt.newValue);
                    filter.shape = (BrushShape)System.Enum.ToObject(typeof(BrushShape), intVal);
                });
            }
        }

        private void BindGeneral(VegetationGenerator.FilterSettings filter)
        {
            var jitter = root.Q<SliderInt>("GenMinSpacingJitter");
            if (jitter != null)
            {
                var hv = Mathf.Max(1, jitter.highValue);
                jitter.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.Clamp01(filter.minSpacingJitter) * hv));
                jitter.RegisterValueChangedCallback(evt => { filter.minSpacingJitter = Mathf.Clamp01(evt.newValue / (float)hv); });
            }
            var maxPoints = root.Q<IntegerField>("GenMaxPoints");
            if (maxPoints != null)
            {
                maxPoints.SetValueWithoutNotify(Mathf.Max(filter.maxPoints, 1));
                maxPoints.RegisterValueChangedCallback(evt => { filter.maxPoints = Mathf.Max(1, evt.newValue); });
            }

            var apMin = root.Q<SliderInt>("AdaptiveMinFactor");
            if (apMin != null)
            {
                var hv = Mathf.Max(1, apMin.highValue);
                var intVal = Mathf.RoundToInt(Mathf.InverseLerp(0.1f, 1.5f, Mathf.Clamp(filter.adaptiveMinFactor, 0.1f, 1.5f)) * hv);
                apMin.SetValueWithoutNotify(intVal);
                apMin.RegisterValueChangedCallback(evt => { filter.adaptiveMinFactor = ToFloat(evt.newValue, 0.1f, 1.5f, hv); });
            }
            var apMax = root.Q<SliderInt>("AdaptiveMaxFactor");
            if (apMax != null)
            {
                var hv = Mathf.Max(1, apMax.highValue);
                var intVal = Mathf.RoundToInt(Mathf.InverseLerp(1f, 3f, Mathf.Clamp(filter.adaptiveMaxFactor, 1f, 3f)) * hv);
                apMax.SetValueWithoutNotify(intVal);
                apMax.RegisterValueChangedCallback(evt => { filter.adaptiveMaxFactor = ToFloat(evt.newValue, 1f, 3f, hv); });
            }
            var apW = root.Q<SliderInt>("AdaptiveNoiseWeight");
            if (apW != null)
            {
                var hv = Mathf.Max(1, apW.highValue);
                var intVal = Mathf.RoundToInt(Mathf.InverseLerp(0.25f, 4f, Mathf.Clamp(filter.adaptiveNoiseWeight, 0.25f, 4f)) * hv);
                apW.SetValueWithoutNotify(intVal);
                apW.RegisterValueChangedCallback(evt => { filter.adaptiveNoiseWeight = ToFloat(evt.newValue, 0.25f, 4f, hv); });
            }
        }

        private void BindCluster(VegetationGenerator.FilterSettings filter)
        {
            var fc = root.Q<Foldout>("GenCluster");
            if (fc == null) return;
            var cc = fc.Q<IntegerField>("GenClusterCount");
            var cpc = fc.Q<IntegerField>("GenChildPerCluster");
            var cr = fc.Q<SliderInt>("GenClusterRadius");
            var cj = fc.Q<SliderInt>("GenChildJitter");
            if (cc != null)
            {
                cc.SetValueWithoutNotify(Mathf.Max(filter.cluster.clusterCount, 1));
                cc.RegisterValueChangedCallback(evt => { var c = filter.cluster; c.clusterCount = Mathf.Max(1, evt.newValue); filter.cluster = c; });
            }
            if (cpc != null)
            {
                cpc.SetValueWithoutNotify(Mathf.Max(filter.cluster.childPerCluster, 1));
                cpc.RegisterValueChangedCallback(evt => { var c = filter.cluster; c.childPerCluster = Mathf.Max(1, evt.newValue); filter.cluster = c; });
            }
            if (cr != null)
            {
                var hv = Mathf.Max(1, cr.highValue);
                cr.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.InverseLerp(0.1f, 50f, Mathf.Clamp(filter.cluster.clusterRadius, 0.1f, 50f)) * hv));
                cr.RegisterValueChangedCallback(evt => { var c = filter.cluster; c.clusterRadius = Mathf.Lerp(0.1f, 50f, Mathf.Clamp01(evt.newValue / (float)hv)); filter.cluster = c; });
            }
            if (cj != null)
            {
                var hv = Mathf.Max(1, cj.highValue);
                cj.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.InverseLerp(0f, 5f, Mathf.Clamp(filter.cluster.childJitter, 0f, 5f)) * hv));
                cj.RegisterValueChangedCallback(evt => { var c = filter.cluster; c.childJitter = Mathf.Lerp(0f, 5f, Mathf.Clamp01(evt.newValue / (float)hv)); filter.cluster = c; });
            }
        }
    }
}

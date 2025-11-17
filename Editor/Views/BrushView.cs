using MrTerrainPainter.Editor.Services;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System;

namespace MrTerrainPainter.Editor.Views
{
    // 笔刷视图：封装 Paint 页的笔刷控件绑定
    public class BrushView
    {
        private readonly VisualElement root;

        public BrushView(VisualElement root)
        {
            this.root = root;
        }

        // 定义一个委托类型，用于通用的浮点数属性存取
        private delegate float PropertyGetter();
        private delegate void PropertySetter(float value);

        /// <summary>
        /// 将 BrushSettings 中的属性绑定到 UIElements 控件
        /// </summary>
        public void Bind(BrushSettings brush)
        {
            if (root == null || brush == null) return;

            // 1. 绑定 EnumField (Shape)
            BindEnumField("Shape", () => brush.shape, value => brush.shape = value);

            // 2. 绑定 Toggle (Preview)
            BindToggle("Preview", () => brush.preview, value => brush.preview = value);

            // 3. 绑定 SliderInt 属性
            BindSliderInt("Size", 0.5f, 50f, () => brush.size, value => brush.size = value);
            BindSliderInt("Strength", 0.1f, 10f, () => brush.strength, value => brush.strength = value);
            BindSliderInt("Density", 0f, 5f, () => brush.densityScale, value => brush.densityScale = value);
            BindSliderInt("Hardness", 0f, 1f, () => brush.hardness, value => brush.hardness = value);
            BindSliderInt("StrokeSpacing", 0f, 1f, () => brush.strokeSpacingFactor, value => brush.strokeSpacingFactor = value);
            var useAbs = root.Q<Toggle>("UseAbsoluteStrokeSpacing");
            if (useAbs != null)
            {
                useAbs.SetValueWithoutNotify(brush.useAbsoluteStrokeSpacing);
                useAbs.RegisterValueChangedCallback(evt => { brush.useAbsoluteStrokeSpacing = evt.newValue; });
            }
            BindSliderInt("StrokeSpacingAbs", 0f, 200f, () => brush.strokeSpacingAbsolute, value => brush.strokeSpacingAbsolute = value);

            BindEnumField("Distribution", () => brush.distribution, value => brush.distribution = value);
            var useBurst = root.Q<Toggle>("UseBurstPoisson");
            if (useBurst != null)
            {
                useBurst.SetValueWithoutNotify(brush.useBurstPoisson);
                useBurst.RegisterValueChangedCallback(evt => { brush.useBurstPoisson = evt.newValue; });
            }
            var curve = root.Q<CurveField>("FalloffCurve");
            if (curve != null)
            {
                curve.value = brush.falloffCurve;
                curve.RegisterValueChangedCallback(evt => { brush.falloffCurve = evt.newValue; });
            }
            BindSliderInt("MinSpacingJitter", 0f, 1f, () => brush.minSpacingJitter, value => brush.minSpacingJitter = value);
            BindSliderInt("AdaptiveMinFactor", 0.1f, 1.5f, () => brush.adaptiveMinFactor, value => brush.adaptiveMinFactor = value);
            BindSliderInt("AdaptiveMaxFactor", 1f, 3f, () => brush.adaptiveMaxFactor, value => brush.adaptiveMaxFactor = value);
            BindSliderInt("AdaptiveNoiseWeight", 0.25f, 4f, () => brush.adaptiveNoiseWeight, value => brush.adaptiveNoiseWeight = value);
            var strokeSeed = root.Q<IntegerField>("StrokeSeed");
            if (strokeSeed != null)
            {
                strokeSeed.SetValueWithoutNotify(brush.strokeSeed);
                strokeSeed.RegisterValueChangedCallback(evt => { brush.strokeSeed = evt.newValue; });
            }
            var maxPoints = root.Q<IntegerField>("MaxPoints");
            if (maxPoints != null)
            {
                maxPoints.SetValueWithoutNotify(brush.maxPoints);
                maxPoints.RegisterValueChangedCallback(evt => { brush.maxPoints = Mathf.Max(1, evt.newValue); });
            }

            var clusterCount = root.Q<IntegerField>("ClusterCount");
            if (clusterCount != null)
            {
                clusterCount.SetValueWithoutNotify(brush.cluster.clusterCount);
                clusterCount.RegisterValueChangedCallback(evt => { var c = brush.cluster; c.clusterCount = Mathf.Max(1, evt.newValue); brush.cluster = c; });
            }
            var childPerCluster = root.Q<IntegerField>("ChildPerCluster");
            if (childPerCluster != null)
            {
                childPerCluster.SetValueWithoutNotify(brush.cluster.childPerCluster);
                childPerCluster.RegisterValueChangedCallback(evt => { var c = brush.cluster; c.childPerCluster = Mathf.Max(1, evt.newValue); brush.cluster = c; });
            }
            BindSliderInt("ClusterRadius", 0.1f, 20f, () => brush.cluster.clusterRadius, value => { var c = brush.cluster; c.clusterRadius = value; brush.cluster = c; });
            BindSliderInt("ChildJitter", 0f, 5f, () => brush.cluster.childJitter, value => { var c = brush.cluster; c.childJitter = value; brush.cluster = c; });

            var mixItems = root.Q<Toggle>("MixItemsWeighted");
            if (mixItems != null)
            {
                mixItems.SetValueWithoutNotify(brush.mixItemsWeighted);
                mixItems.RegisterValueChangedCallback(evt => { brush.mixItemsWeighted = evt.newValue; });
            }
            var limitPerItem = root.Q<Toggle>("LimitPerItem");
            if (limitPerItem != null)
            {
                limitPerItem.SetValueWithoutNotify(brush.limitPerItem);
                limitPerItem.RegisterValueChangedCallback(evt => { brush.limitPerItem = evt.newValue; });
            }
            BindSliderInt("GlobalSpacingFactor", 0f, 1f, () => brush.globalSpacingFactor, value => brush.globalSpacingFactor = value);
            var mixExtra = root.Q<Toggle>("MixExtraProfiles");
            if (mixExtra != null)
            {
                mixExtra.SetValueWithoutNotify(brush.mixExtraProfiles);
                mixExtra.RegisterValueChangedCallback(evt => { brush.mixExtraProfiles = evt.newValue; });
            }

            brush.Changed += propertyName =>
            {
                if (string.Equals(propertyName, nameof(BrushSettings.shape), StringComparison.Ordinal))
                {
                    var enumField = root.Q<EnumField>("Shape");
                    if (enumField != null) { enumField.SetValueWithoutNotify(brush.shape); }
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.preview), StringComparison.Ordinal))
                {
                    var toggle = root.Q<Toggle>("Preview");
                    if (toggle != null) { toggle.SetValueWithoutNotify(brush.preview); }
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.size), StringComparison.Ordinal))
                {
                    UpdateSliderInt("Size", 0.5f, 50f, () => brush.size);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.strength), StringComparison.Ordinal))
                {
                    UpdateSliderInt("Strength", 0.1f, 10f, () => brush.strength);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.densityScale), StringComparison.Ordinal))
                {
                    UpdateSliderInt("Density", 0f, 5f, () => brush.densityScale);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.hardness), StringComparison.Ordinal))
                {
                    UpdateSliderInt("Hardness", 0f, 1f, () => brush.hardness);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.strokeSpacingFactor), StringComparison.Ordinal))
                {
                    UpdateSliderInt("StrokeSpacing", 0f, 1f, () => brush.strokeSpacingFactor);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.useAbsoluteStrokeSpacing), StringComparison.Ordinal))
                {
                    var t = root.Q<Toggle>("UseAbsoluteStrokeSpacing");
                    if (t != null) t.SetValueWithoutNotify(brush.useAbsoluteStrokeSpacing);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.strokeSpacingAbsolute), StringComparison.Ordinal))
                {
                    UpdateSliderInt("StrokeSpacingAbs", 0f, 200f, () => brush.strokeSpacingAbsolute);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.distribution), StringComparison.Ordinal))
                {
                    var enumField = root.Q<EnumField>("Distribution");
                    if (enumField != null) { enumField.SetValueWithoutNotify(brush.distribution); }
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.useBurstPoisson), StringComparison.Ordinal))
                {
                    var t = root.Q<Toggle>("UseBurstPoisson");
                    if (t != null) t.SetValueWithoutNotify(brush.useBurstPoisson);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.falloffCurve), StringComparison.Ordinal))
                {
                    var curveField = root.Q<CurveField>("FalloffCurve");
                    if (curveField != null) { curveField.value = brush.falloffCurve; }
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.minSpacingJitter), StringComparison.Ordinal))
                {
                    UpdateSliderInt("MinSpacingJitter", 0f, 1f, () => brush.minSpacingJitter);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.adaptiveMinFactor), StringComparison.Ordinal))
                {
                    UpdateSliderInt("AdaptiveMinFactor", 0.1f, 1.5f, () => brush.adaptiveMinFactor);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.adaptiveMaxFactor), StringComparison.Ordinal))
                {
                    UpdateSliderInt("AdaptiveMaxFactor", 1f, 3f, () => brush.adaptiveMaxFactor);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.adaptiveNoiseWeight), StringComparison.Ordinal))
                {
                    UpdateSliderInt("AdaptiveNoiseWeight", 0.25f, 4f, () => brush.adaptiveNoiseWeight);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.strokeSeed), StringComparison.Ordinal))
                {
                    var field = root.Q<IntegerField>("StrokeSeed");
                    if (field != null) { field.SetValueWithoutNotify(brush.strokeSeed); }
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.maxPoints), StringComparison.Ordinal))
                {
                    var field = root.Q<IntegerField>("MaxPoints");
                    if (field != null) { field.SetValueWithoutNotify(brush.maxPoints); }
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.cluster), StringComparison.Ordinal))
                {
                    var cc = root.Q<IntegerField>("ClusterCount");
                    var cpc = root.Q<IntegerField>("ChildPerCluster");
                    if (cc != null) cc.SetValueWithoutNotify(brush.cluster.clusterCount);
                    if (cpc != null) cpc.SetValueWithoutNotify(brush.cluster.childPerCluster);
                    UpdateSliderInt("ClusterRadius", 0.1f, 20f, () => brush.cluster.clusterRadius);
                    UpdateSliderInt("ChildJitter", 0f, 5f, () => brush.cluster.childJitter);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.mixItemsWeighted), StringComparison.Ordinal))
                {
                    var t = root.Q<Toggle>("MixItemsWeighted");
                    if (t != null) t.SetValueWithoutNotify(brush.mixItemsWeighted);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.limitPerItem), StringComparison.Ordinal))
                {
                    var t = root.Q<Toggle>("LimitPerItem");
                    if (t != null) t.SetValueWithoutNotify(brush.limitPerItem);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.globalSpacingFactor), StringComparison.Ordinal))
                {
                    UpdateSliderInt("GlobalSpacingFactor", 0f, 1f, () => brush.globalSpacingFactor);
                }
                else if (string.Equals(propertyName, nameof(BrushSettings.mixExtraProfiles), StringComparison.Ordinal))
                {
                    var t = root.Q<Toggle>("MixExtraProfiles");
                    if (t != null) t.SetValueWithoutNotify(brush.mixExtraProfiles);
                }
            };
        }

        // --- 私有通用绑定方法 ---

        /// <summary>
        /// 通用 EnumField 绑定方法
        /// </summary>
        private void BindEnumField<TEnum>(string name, Func<TEnum> getter, Action<TEnum> setter) where TEnum : Enum
        {
            var enumField = root.Q<EnumField>(name);
            if (enumField != null)
            {
                // 初始化 EnumField 必须使用正确的 Enum 类型值
                enumField.Init(getter());
                enumField.SetValueWithoutNotify(getter());

                enumField.RegisterValueChangedCallback(evt =>
                {
                    var intVal = System.Convert.ToInt32(evt.newValue);
                    var newValue = (TEnum)System.Enum.ToObject(typeof(TEnum), intVal);
                    setter(newValue);
                });
            }
        }

        /// <summary>
        /// 通用 Toggle 绑定方法
        /// </summary>
        private void BindToggle(string name, Func<bool> getter, Action<bool> setter)
        {
            var toggle = root.Q<Toggle>(name);
            if (toggle != null)
            {
                toggle.SetValueWithoutNotify(getter());
                toggle.RegisterValueChangedCallback(evt => { setter(evt.newValue); });
            }
        }

        /// <summary>
        /// 通用 SliderInt 绑定方法，用于浮点数到整数的映射
        /// </summary>
        private void BindSliderInt(string name, float minFloat, float maxFloat, PropertyGetter getter, PropertySetter setter)
        {
            var slider = root.Q<SliderInt>(name);
            if (slider == null) return;

            // 确保 highValue 至少为 1，避免除以零
            var highValue = Mathf.Max(1, slider.highValue);

            // 初始设置 SliderInt 的值 (Float -> Int)
            var initialIntValue = ToInt(getter(), minFloat, maxFloat, highValue);
            slider.SetValueWithoutNotify(initialIntValue);

            // 注册回调 (Int -> Float)
            slider.RegisterValueChangedCallback(evt =>
            {
                var newValueFloat = ToFloat(evt.newValue, minFloat, maxFloat, highValue);
                setter(newValueFloat);
            });
        }

        private void UpdateSliderInt(string name, float minFloat, float maxFloat, PropertyGetter getter)
        {
            var slider = root.Q<SliderInt>(name);
            if (slider == null) return;
            var highValue = Mathf.Max(1, slider.highValue);
            var intVal = ToInt(getter(), minFloat, maxFloat, highValue);
            slider.SetValueWithoutNotify(intVal);
        }

        /// <summary>
        /// 将浮点数值映射到 SliderInt 的整数范围 (Float -> Int)
        /// </summary>
        /// <param name="f">原始浮点值</param>
        /// <param name="min">浮点最小值</param>
        /// <param name="max">浮点最大值</param>
        /// <param name="highValue">SliderInt 的 highValue</param>
        /// <returns>映射后的整数值</returns>
        private static int ToInt(float f, float min, float max, int highValue)
        {
            // InverseLerp 获取归一化比例 (0-1)
            var normalized = Mathf.InverseLerp(min, max, Mathf.Clamp(f, min, max));
            // 将归一化比例映射到整数范围并四舍五入
            return Mathf.RoundToInt(normalized * highValue);
        }

        /// <summary>
        /// 将 SliderInt 的整数值映射回浮点数范围 (Int -> Float)
        /// </summary>
        /// <param name="i">SliderInt 的整数值</param>
        /// <param name="min">浮点最小值</param>
        /// <param name="max">浮点最大值</param>
        /// <param name="highValue">SliderInt 的 highValue</param>
        /// <returns>映射后的浮点值</returns>
        private static float ToFloat(int i, float min, float max, int highValue)
        {
            // 计算归一化比例 (0-1)
            var normalized = Mathf.Clamp01(i / (float)highValue);
            // 将归一化比例映射回浮点数范围
            return Mathf.Lerp(min, max, normalized);
        }
    }
}

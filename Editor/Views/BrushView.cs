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

            BindEnumField("Distribution", () => brush.distribution, value => brush.distribution = value);
            var curve = root.Q<CurveField>("FalloffCurve");
            if (curve != null)
            {
                curve.value = brush.falloffCurve;
                curve.RegisterValueChangedCallback(evt => { brush.falloffCurve = evt.newValue; });
            }
            BindSliderInt("MinSpacingJitter", 0f, 1f, () => brush.minSpacingJitter, value => brush.minSpacingJitter = value);
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
                    // 确保转换是安全的，因为 Init 已经设置了正确的类型
                    if (evt.newValue is TEnum newValue)
                    {
                        setter(newValue);
                    }
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

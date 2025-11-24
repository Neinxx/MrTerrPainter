using System;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    // 属性面板视图：负责 Prefab 选择、权重、范围与密度/间距等控件的绑定与刷新
    public class PropertyPanelView
    {
        public struct PropertyPanelCallbacks
        {
            public Func<VegetationItem> GetSelectedItem;
            public Func<VegetationProfile> GetCurrentProfile;
            public Func<int> GetSelectedItemIndex;
            public Action<int> RemoveItemAt;
            public Action<VegetationProfile, int, GameObject> AssignPrefabToItem;
            public Action RefreshPreviewListUI;
            public Action RefreshVegetationListUI;
            public Action UpdatePropertyPanelFromSelectedItem;
            public Action MarkCurrentProfileDirty;
        }

        private readonly VisualElement root;

        private ObjectField uiSelectPrefab;
        private Slider uiWeigth;
        private MinMaxSlider uiSceleRange;
        private MinMaxSlider uiYrotationRange;
        private MinMaxSlider uiHeigthRange;
        private MinMaxSlider uiSlopeRange;
        private Slider uiBaseDensity;
        private FloatField uiMinSpacing;
        private FloatField uiEdgeSlopeThreshold;
        private MinMaxSlider uiEmbedDepthRange;
        private FloatField uiFacadeEnterSlope;
        private FloatField uiFacadeExitSlope;
        private FloatField uiProbeStep;
        private FloatField uiProbeMaxDist;
        private FloatField uiFacadeRefHeight;
        private Vector3Field uiFacadeScaleOffset;
        private Vector3Field uiFacadeOffsets;
        private EnumField uiFacadeSmoothMode;
        private IntegerField uiFacadeSmoothWindow;
        private FloatField uiFacadeSmoothSigma;

        private PropertyPanelCallbacks callbacks;

        public PropertyPanelView(VisualElement queryRoot)
        {
            root = queryRoot;
        }

        public void Bind(PropertyPanelCallbacks cb)
        {
            callbacks = cb;

            if (root == null) return;

            uiSelectPrefab = root.Q<ObjectField>("SelectPrefab");
            uiWeigth = root.Q<Slider>("Weigth");
            uiSceleRange = root.Q<MinMaxSlider>("SceleRange");
            uiYrotationRange = root.Q<MinMaxSlider>("YrotationRange");
            uiHeigthRange = root.Q<MinMaxSlider>("HeigthRange");
            uiSlopeRange = root.Q<MinMaxSlider>("SlopeRange");
            uiBaseDensity = root.Q<Slider>("BaseDensity");
            uiMinSpacing = root.Q<FloatField>("MinSpacing");
            uiEdgeSlopeThreshold = root.Q<FloatField>("EdgeSlopeThreshold");
            uiEmbedDepthRange = root.Q<MinMaxSlider>("EmbedDepthRange");
            uiFacadeEnterSlope = root.Q<FloatField>("FacadeEnterSlope");
            uiFacadeExitSlope = root.Q<FloatField>("FacadeExitSlope");
            uiProbeStep = root.Q<FloatField>("ProbeStep");
            uiProbeMaxDist = root.Q<FloatField>("ProbeMaxDist");
            uiFacadeRefHeight = root.Q<FloatField>("FacadeRefHeight");
            uiFacadeScaleOffset = root.Q<Vector3Field>("FacadeScaleOffset");
            uiFacadeOffsets = root.Q<Vector3Field>("FacadeOffsets");
            uiFacadeSmoothMode = root.Q<EnumField>("FacadeSmoothMode");
            uiFacadeSmoothWindow = root.Q<IntegerField>("FacadeSmoothWindow");
            uiFacadeSmoothSigma = root.Q<FloatField>("FacadeSmoothSigma");

            // 所有控件均来自 UXML；不在 C# 中动态创建

            // 上下限初始化（遵循提前返回与单一职责）
            if (uiWeigth != null) { uiWeigth.lowValue = 0f; uiWeigth.highValue = 1f; }
            if (uiSceleRange != null) { uiSceleRange.lowLimit = 0f; uiSceleRange.highLimit = 5f; }
            if (uiHeigthRange != null) { uiHeigthRange.lowLimit = 0f; uiHeigthRange.highLimit = 1000f; }
            if (uiSlopeRange != null) { uiSlopeRange.lowLimit = 0f; uiSlopeRange.highLimit = 90f; }
            if (uiBaseDensity != null) { uiBaseDensity.lowValue = 0f; uiBaseDensity.highValue = 10f; }
            if (uiMinSpacing != null) { uiMinSpacing.tooltip = "条目级最小间距（米）"; }
            if (uiEdgeSlopeThreshold != null) { uiEdgeSlopeThreshold.tooltip = "Landscape 最小坡度阈值（度）"; }
            if (uiEmbedDepthRange != null) { uiEmbedDepthRange.lowLimit = 0f; uiEmbedDepthRange.highLimit = 1f; }
            if (uiFacadeEnterSlope != null) { uiFacadeEnterSlope.tooltip = "Facade 进入陡坡阈值（度）"; }
            if (uiFacadeExitSlope != null) { uiFacadeExitSlope.tooltip = "Facade 退出至平缓阈值（度）"; }
            if (uiProbeStep != null) { uiProbeStep.tooltip = "Facade 探测步长（米）"; }
            if (uiProbeMaxDist != null) { uiProbeMaxDist.tooltip = "Facade 最大探测距离（米）"; }
            if (uiFacadeRefHeight != null) { uiFacadeRefHeight.tooltip = "Facade 参考高度（米）"; }
            if (uiFacadeOffsets != null) { uiFacadeOffsets.tooltip = "Facade 偏移：X沿right，Y沿up，Z沿水平-Forward"; }
            if (uiFacadeScaleOffset != null) { uiFacadeScaleOffset.tooltip = "Facade 自适应后的逐轴缩放偏移（XYZ加法），用于微调最终缩放"; }
            if (uiFacadeSmoothMode != null) { uiFacadeSmoothMode.tooltip = "虚拟立面平滑模式：Mean/Gaussian/Median"; }
            if (uiFacadeSmoothWindow != null) { uiFacadeSmoothWindow.tooltip = "平滑窗口大小（奇数>=3）"; }
            if (uiFacadeSmoothSigma != null) { uiFacadeSmoothSigma.tooltip = "高斯平滑Sigma"; }

            // 类型约束与事件绑定
            if (uiSelectPrefab != null)
            {
                uiSelectPrefab.objectType = typeof(GameObject);
                uiSelectPrefab.allowSceneObjects = false;
                uiSelectPrefab.RegisterValueChangedCallback(evt =>
                {
                    var item = callbacks.GetSelectedItem?.Invoke();
                    if (item == null) return; // 提前返回

                    var newGo = evt.newValue as GameObject;
                    if (newGo == null)
                    {
                        var index = callbacks.GetSelectedItemIndex != null ? callbacks.GetSelectedItemIndex() : -1;
                        callbacks.RemoveItemAt?.Invoke(index);
                        callbacks.RefreshPreviewListUI?.Invoke();
                        callbacks.UpdatePropertyPanelFromSelectedItem?.Invoke();
                        return; // 提前返回
                    }
                    var profile = callbacks.GetCurrentProfile?.Invoke();
                    var i = callbacks.GetSelectedItemIndex != null ? callbacks.GetSelectedItemIndex() : -1;
                    callbacks.AssignPrefabToItem?.Invoke(profile, i, newGo);
                });
            }

            uiWeigth?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0f, 1f);
                item.weight = v;
                uiWeigth.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiSceleRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                v.x = Mathf.Max(0f, v.x);
                v.y = Mathf.Max(v.x, v.y);
                item.uniformScaleRange = v;
                uiSceleRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiYrotationRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                float maxRot = uiYrotationRange != null ? uiYrotationRange.highLimit : 30f;
                v.x = Mathf.Clamp(v.x, 0f, maxRot);
                v.y = Mathf.Clamp(v.y, 0f, maxRot);
                if (v.y < v.x) v.y = v.x;
                item.yRotationRange = v;
                uiYrotationRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiHeigthRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                if (v.y < v.x) v.y = v.x;
                item.heightRange = v;
                uiHeigthRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiSlopeRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                v.x = Mathf.Clamp(v.x, 0f, 90f);
                v.y = Mathf.Clamp(v.y, 0f, 90f);
                if (v.y < v.x) v.y = v.x;
                item.slopeRange = v;
                uiSlopeRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiBaseDensity?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0f, 10f);
                item.baseDensity = v;
                uiBaseDensity.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiMinSpacing?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Max(evt.newValue, 0f);
                item.minSpacing = v;
                uiMinSpacing.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiEdgeSlopeThreshold?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0f, 90f);
                item.edgeSlopeThreshold = v;
                uiEdgeSlopeThreshold.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiEmbedDepthRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                v.x = Mathf.Clamp(v.x, 0f, 1f);
                v.y = Mathf.Clamp(v.y, v.x, 1f);
                item.embedDepthRange = v;
                uiEmbedDepthRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiFacadeEnterSlope?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0f, 90f);
                item.edgeSlopeEnter = v;
                uiFacadeEnterSlope.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });
            uiFacadeExitSlope?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0f, 90f);
                item.edgeSlopeExit = v;
                uiFacadeExitSlope.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });
            uiProbeStep?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0.1f, 5f);
                item.probeStep = v;
                uiProbeStep.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });
            uiProbeMaxDist?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0.5f, 20f);
                item.probeMaxDist = v;
                uiProbeMaxDist.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });
            uiFacadeRefHeight?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Max(evt.newValue, 0.0001f);
                item.referenceHeightMeters = v;
                uiFacadeRefHeight.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });
            uiFacadeOffsets?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                item.offsets = v;
                uiFacadeOffsets.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });
            uiFacadeScaleOffset?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                item.facadeScaleOffset = v;
                uiFacadeScaleOffset.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });
            uiFacadeSmoothMode?.RegisterValueChangedCallback(evt =>
            {
                var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
                if (cfg == null) return;
                var v = (MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode)evt.newValue;
                cfg.facadeSmoothMode = v;
                uiFacadeSmoothMode.SetValueWithoutNotify(v);
                // 避免频繁保存触发资产读条：仅更新内存，刷新视图
            });
            uiFacadeSmoothWindow?.RegisterValueChangedCallback(evt =>
            {
                var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
                if (cfg == null) return;
                var v = Mathf.Max(evt.newValue, 3);
                if ((v % 2) == 0) v += 1;
                cfg.facadeSmoothWindow = v;
                uiFacadeSmoothWindow.SetValueWithoutNotify(v);
                // 避免频繁保存触发资产读条：仅更新内存，刷新视图
            });
            uiFacadeSmoothSigma?.RegisterValueChangedCallback(evt =>
            {
                var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
                if (cfg == null) return;
                var v = Mathf.Max(evt.newValue, 0.1f);
                cfg.facadeSmoothSigma = v;
                uiFacadeSmoothSigma.SetValueWithoutNotify(v);
                // 避免频繁保存触发资产读条：仅更新内存，刷新视图
            });
        }

        // 从选中条目刷新属性面板显示（保持与窗口逻辑一致）
        public void UpdateFromSelectedItem()
        {
            var item = callbacks.GetSelectedItem?.Invoke();
            if (item == null)
            {
                uiSelectPrefab?.SetValueWithoutNotify(null);
                uiWeigth?.SetValueWithoutNotify(0f);
                uiSceleRange?.SetValueWithoutNotify(Vector2.one);
                var maxRot = uiYrotationRange != null ? uiYrotationRange.highLimit : 30f;
                uiYrotationRange?.SetValueWithoutNotify(new Vector2(0f, maxRot));
                uiHeigthRange?.SetValueWithoutNotify(new Vector2(0f, 1000f));
                uiSlopeRange?.SetValueWithoutNotify(new Vector2(0f, 90f));
                uiBaseDensity?.SetValueWithoutNotify(0f);
            uiMinSpacing?.SetValueWithoutNotify(0f);
                uiEdgeSlopeThreshold?.SetValueWithoutNotify(75f);
                uiEmbedDepthRange?.SetValueWithoutNotify(new Vector2(0.1f, 0.3f));
                if (uiEdgeSlopeThreshold != null) uiEdgeSlopeThreshold.style.display = DisplayStyle.None;
                if (uiEmbedDepthRange != null) uiEmbedDepthRange.style.display = DisplayStyle.None;
                if (uiFacadeEnterSlope != null) uiFacadeEnterSlope.style.display = DisplayStyle.None;
                if (uiFacadeExitSlope != null) uiFacadeExitSlope.style.display = DisplayStyle.None;
                if (uiProbeStep != null) uiProbeStep.style.display = DisplayStyle.None;
                if (uiProbeMaxDist != null) uiProbeMaxDist.style.display = DisplayStyle.None;
                if (uiFacadeRefHeight != null) uiFacadeRefHeight.style.display = DisplayStyle.None;
                if (uiFacadeOffsets != null) uiFacadeOffsets.style.display = DisplayStyle.None;
                return;
            }

            uiSelectPrefab?.SetValueWithoutNotify(item.prefab);
            uiWeigth?.SetValueWithoutNotify(item.weight);
            uiSceleRange?.SetValueWithoutNotify(item.uniformScaleRange);
            uiYrotationRange?.SetValueWithoutNotify(item.yRotationRange);
            uiHeigthRange?.SetValueWithoutNotify(item.heightRange);
            uiSlopeRange?.SetValueWithoutNotify(item.slopeRange);
            uiBaseDensity?.SetValueWithoutNotify(item.baseDensity);
            uiMinSpacing?.SetValueWithoutNotify(item.minSpacing);
            uiEdgeSlopeThreshold?.SetValueWithoutNotify(item.edgeSlopeThreshold);
            uiEmbedDepthRange?.SetValueWithoutNotify(item.embedDepthRange);
            uiFacadeEnterSlope?.SetValueWithoutNotify(item.edgeSlopeEnter);
            uiFacadeExitSlope?.SetValueWithoutNotify(item.edgeSlopeExit);
            uiProbeStep?.SetValueWithoutNotify(item.probeStep);
            uiProbeMaxDist?.SetValueWithoutNotify(item.probeMaxDist);
            uiFacadeRefHeight?.SetValueWithoutNotify(item.referenceHeightMeters);
            uiFacadeScaleOffset?.SetValueWithoutNotify(item.facadeScaleOffset);
            uiFacadeOffsets?.SetValueWithoutNotify(item.offsets);
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            if (cfg != null)
            {
                uiFacadeSmoothMode?.Init(cfg.facadeSmoothMode);
                uiFacadeSmoothMode?.SetValueWithoutNotify(cfg.facadeSmoothMode);
                uiFacadeSmoothWindow?.SetValueWithoutNotify(cfg.facadeSmoothWindow);
                uiFacadeSmoothSigma?.SetValueWithoutNotify(cfg.facadeSmoothSigma);
            }

            var isLandscape = item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape;
            if (uiEdgeSlopeThreshold != null) uiEdgeSlopeThreshold.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiEmbedDepthRange != null) uiEmbedDepthRange.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiFacadeEnterSlope != null) uiFacadeEnterSlope.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiFacadeExitSlope != null) uiFacadeExitSlope.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiProbeStep != null) uiProbeStep.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiMinSpacing != null) uiMinSpacing.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiProbeMaxDist != null) uiProbeMaxDist.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiFacadeRefHeight != null) uiFacadeRefHeight.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiFacadeScaleOffset != null) uiFacadeScaleOffset.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiFacadeOffsets != null) uiFacadeOffsets.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiFacadeSmoothMode != null) uiFacadeSmoothMode.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiFacadeSmoothWindow != null) uiFacadeSmoothWindow.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;
            if (uiFacadeSmoothSigma != null) uiFacadeSmoothSigma.style.display = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;

            // —— 动态匹配上下限 ——
            if (uiSceleRange != null)
            {
                uiSceleRange.lowLimit = Mathf.Min(0f, item.uniformScaleRange.x);
                uiSceleRange.highLimit = Mathf.Max(5f, item.uniformScaleRange.y);
            }
            if (uiHeigthRange != null)
            {
                uiHeigthRange.lowLimit = Mathf.Min(0f, item.heightRange.x);
                uiHeigthRange.highLimit = Mathf.Max(1000f, item.heightRange.y);
            }
            if (uiSlopeRange != null)
            {
                uiSlopeRange.lowLimit = Mathf.Min(0f, item.slopeRange.x);
                uiSlopeRange.highLimit = Mathf.Max(90f, item.slopeRange.y);
            }
        }
    }
}

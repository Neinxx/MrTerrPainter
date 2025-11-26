using System;
using System.Collections.Generic;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using PrefabType = MrTerrainPainter.Runtime.Profiles.PrefabType;

namespace MrTerrainPainter.Editor.Views
{
    public class PropertyPanelView
    {
        #region Callbacks & Definitions
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
            public Action ScanSelectedTerrainsForFacades;
            public Action BakeCachedFacades;
        }
        #endregion

        private readonly VisualElement root;
        private PropertyPanelCallbacks callbacks;

        // 自动化列表
        private readonly List<Action<VegetationItem>> _valueUpdaters = new List<Action<VegetationItem>>();
        private readonly List<VisualElement> _landscapeGroup = new List<VisualElement>();

        // 特殊控件引用
        private ObjectField uiSelectPrefab;
        private MinMaxSlider uiSceleRange;
        private MinMaxSlider uiYrotationRange;
        private MinMaxSlider uiHeigthRange;
        private MinMaxSlider uiSlopeRange;
        private Toggle uiUseContourDetection;
        private FloatField uiContourSlopeDeg;

        public PropertyPanelView(VisualElement queryRoot)
        {
            root = queryRoot;
        }

        public void Bind(PropertyPanelCallbacks cb)
        {
            callbacks = cb;
            if (root == null) return;

            _valueUpdaters.Clear();
            _landscapeGroup.Clear();

            // =========================================================
            // 1. 绑定通用字段 (Always Visible)
            // =========================================================

            BindPrefabSelector();

            BindField<Slider, float>("Weigth",
                i => i.weight, (i, v) => i.weight = Mathf.Clamp01(v),
                setup: s => { s.lowValue = 0f; s.highValue = 1f; });

            BindField<Slider, float>("BaseDensity",
                i => i.baseDensity, (i, v) => i.baseDensity = Mathf.Clamp(v, 0f, 10f),
                setup: s => { s.lowValue = 0f; s.highValue = 10f; });

            // 范围滑条
            BindRangeSlider("SceleRange", i => i.uniformScaleRange, (i, v) => i.uniformScaleRange = SanitizeRange(v, 0f), ref uiSceleRange, 0f, 5f);
            BindRangeSlider("YrotationRange", i => i.yRotationRange, (i, v) => i.yRotationRange = SanitizeRange(v, 0f, 360f), ref uiYrotationRange, 0f, 360f);
            BindRangeSlider("HeigthRange", i => i.heightRange, (i, v) => i.heightRange = SanitizeRange(v, 0f), ref uiHeigthRange, 0f, 1000f);
            BindRangeSlider("SlopeRange", i => i.slopeRange, (i, v) => i.slopeRange = SanitizeRange(v, 0f, 90f), ref uiSlopeRange, 0f, 90f);

            // =========================================================
            // 2. 绑定 Landscape 专属字段 (Landscape Only)
            // =========================================================

            // [修改] 名称改为 FacadeMinSpacing，并归入 Landscape 组
            BindField<FloatField, float>("FacadeMinSpacing",
                i => i.minSpacing, (i, v) => i.minSpacing = Mathf.Max(0f, v),
                setup: f => f.tooltip = "条目级最小间距（米）", isLandscapeOnly: true);

            BindField<FloatField, float>("EdgeSlopeThreshold",
                i => i.edgeSlopeThreshold, (i, v) => i.edgeSlopeThreshold = Mathf.Clamp(v, 0f, 90f),
                setup: f => f.tooltip = "Landscape 最小坡度阈值（度）", isLandscapeOnly: true);

            BindField<MinMaxSlider, Vector2>("EmbedDepthRange",
                i => i.embedDepthRange, (i, v) => i.embedDepthRange = SanitizeRange(v, 0f, 1f),
                setup: s => { s.lowLimit = 0f; s.highLimit = 1f; }, isLandscapeOnly: true);

            BindField<FloatField, float>("FacadeEnterSlope", i => i.edgeSlopeEnter, (i, v) => i.edgeSlopeEnter = Mathf.Clamp(v, 0f, 90f), isLandscapeOnly: true);
            BindField<FloatField, float>("FacadeExitSlope", i => i.edgeSlopeExit, (i, v) => i.edgeSlopeExit = Mathf.Clamp(v, 0f, 90f), isLandscapeOnly: true);
            BindField<FloatField, float>("ProbeStep", i => i.probeStep, (i, v) => i.probeStep = Mathf.Clamp(v, 0.1f, 5f), isLandscapeOnly: true);
            BindField<FloatField, float>("ProbeMaxDist", i => i.probeMaxDist, (i, v) => i.probeMaxDist = Mathf.Clamp(v, 0.5f, 20f), isLandscapeOnly: true);
            BindField<FloatField, float>("FacadeRefHeight", i => i.referenceHeightMeters, (i, v) => i.referenceHeightMeters = Mathf.Max(0.0001f, v), isLandscapeOnly: true);

            BindField<Vector3Field, Vector3>("FacadeScaleOffset", i => i.facadeScaleOffset, (i, v) => i.facadeScaleOffset = v, isLandscapeOnly: true);
            BindField<Vector3Field, Vector3>("FacadeOffsets", i => i.offsets, (i, v) => i.offsets = v, isLandscapeOnly: true);

            // =========================================================
            // 3. 绑定全局配置字段 (Landscape Only)
            // =========================================================

            BindConfigField<EnumField, Enum>("FacadeSmoothMode",
                c => c.facadeSmoothMode, (c, v) => c.facadeSmoothMode = (FacadeSmoothingMode)v, isLandscapeOnly: true);

            BindConfigField<IntegerField, int>("FacadeSmoothWindow",
                c => c.facadeSmoothWindow, (c, v) => c.facadeSmoothWindow = EnsureOdd(v), isLandscapeOnly: true);

            BindConfigField<FloatField, float>("FacadeSmoothSigma",
                c => c.facadeSmoothSigma, (c, v) => c.facadeSmoothSigma = Mathf.Max(0.1f, v), isLandscapeOnly: true);

            BindConfigField<FloatField, float>("RdpEpsilon",
                c => c.facadeRdpEpsilon, (c, v) => c.facadeRdpEpsilon = Mathf.Max(0.01f, v), isLandscapeOnly: true);
            BindConfigField<ColorField, Color>("PreviewBottomColor",
                c => c.facadePreviewBottomColor, (c, v) => c.facadePreviewBottomColor = v, isLandscapeOnly: true);
            BindConfigField<ColorField, Color>("PreviewTopColor",
                c => c.facadePreviewTopColor, (c, v) => c.facadePreviewTopColor = v, isLandscapeOnly: true);

            // =========================================================
            // 4. 按钮与特殊交互 (Landscape Only)
            // =========================================================

            var btnScan = root.Q<Button>("ScanFacadesButton");
            if (btnScan != null)
            {
                btnScan.SetClickHandler(() => callbacks.ScanSelectedTerrainsForFacades?.Invoke());
                _landscapeGroup.Add(btnScan);
            }

            var btnBake = root.Q<Button>("BakeFacadesButton");
            if (btnBake != null)
            {
                btnBake.SetClickHandler(() => callbacks.BakeCachedFacades?.Invoke());
                _landscapeGroup.Add(btnBake);
            }

            uiUseContourDetection = root.Q<Toggle>("UseContourDetection");
            if (uiUseContourDetection != null)
            {
                uiUseContourDetection.tooltip = "使用高度图等值线扫描替代射线扫描";
                BindConfigField<Toggle, bool>("UseContourDetection",
                    c => c.useContourDetection, (c, v) => c.useContourDetection = v, isLandscapeOnly: true);
            }

            uiContourSlopeDeg = root.Q<FloatField>("ContourSlopeDeg");
            if (uiContourSlopeDeg != null)
            {
                uiContourSlopeDeg.tooltip = "等值线坡度阈值（度）";
                BindConfigField<FloatField, float>("ContourSlopeDeg",
                    c => c.contourSlopeDeg, (c, v) => c.contourSlopeDeg = Mathf.Clamp(v, 0f, 90f), isLandscapeOnly: true);
            }
        }

        // 刷新面板数据 (从 Item -> UI)
        public void UpdateFromSelectedItem()
        {
            var item = callbacks.GetSelectedItem?.Invoke();

            if (item == null)
            {
                root.SetEnabled(false);
                return;
            }
            root.SetEnabled(true);

            // 执行所有注册的值更新器
            foreach (var updater in _valueUpdaters) updater(item);

            // 特殊字段手动更新
            if (uiSelectPrefab != null) uiSelectPrefab.SetValueWithoutNotify(item.prefab);

            // 更新 Config 类字段
            UpdateConfigFields();

            // 控制可见性
            UpdateVisibility(item);
        }

        private void UpdateVisibility(VegetationItem item)
        {
            bool isLandscape = item.prefabType == PrefabType.Landscape;
            DisplayStyle style = isLandscape ? DisplayStyle.Flex : DisplayStyle.None;

            foreach (var el in _landscapeGroup)
            {
                el.style.display = style;
            }
        }

        // --- 核心辅助方法 ---

        private void BindField<TElement, TValue>(
            string name,
            Func<VegetationItem, TValue> getter,
            Action<VegetationItem, TValue> setter,
            Action<TElement> setup = null,
            bool isLandscapeOnly = false)
            where TElement : VisualElement, INotifyValueChanged<TValue>
        {
            var field = root.Q<TElement>(name);
            if (field == null) return;

            setup?.Invoke(field);

            field.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                setter(item, evt.newValue);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            _valueUpdaters.Add(item => field.SetValueWithoutNotify(getter(item)));

            if (isLandscapeOnly) _landscapeGroup.Add(field);
        }

        private void BindRangeSlider(
            string name,
            Func<VegetationItem, Vector2> getter,
            Action<VegetationItem, Vector2> setter,
            ref MinMaxSlider fieldRef,
            float absMin, float absMax)
        {
            var field = root.Q<MinMaxSlider>(name);
            fieldRef = field;
            if (field == null) return;

            field.lowLimit = absMin;
            field.highLimit = absMax;

            field.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                setter(item, evt.newValue);
                UpdateSliderLimits(field, evt.newValue, absMin, absMax);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            _valueUpdaters.Add(item =>
            {
                var val = getter(item);
                UpdateSliderLimits(field, val, absMin, absMax);
                field.SetValueWithoutNotify(val);
            });
        }

        private void BindConfigField<TElement, TValue>(
            string name,
            Func<Editor.Config.MrTerrainPainterConfig, TValue> getter,
            Action<Editor.Config.MrTerrainPainterConfig, TValue> setter,
            bool isLandscapeOnly = false)
            where TElement : VisualElement, INotifyValueChanged<TValue>
        {
            var field = root.Q<TElement>(name);
            if (field == null) return;

            field.RegisterValueChangedCallback(evt =>
            {
                var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
                if (cfg == null) return;
                setter(cfg, evt.newValue);
                SceneView.RepaintAll();
            });

            _configUpdaters.Add(cfg =>
            {
                if (field is EnumField ef && typeof(TValue).IsEnum) ef.Init((Enum)(object)getter(cfg));
                field.SetValueWithoutNotify(getter(cfg));
            });

            if (isLandscapeOnly) _landscapeGroup.Add(field);
        }

        private readonly List<Action<Editor.Config.MrTerrainPainterConfig>> _configUpdaters = new List<Action<Editor.Config.MrTerrainPainterConfig>>();

        private void UpdateConfigFields()
        {
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            if (cfg == null) return;
            foreach (var updater in _configUpdaters) updater(cfg);
        }

        private void BindPrefabSelector()
        {
            uiSelectPrefab = root.Q<ObjectField>("SelectPrefab");
            if (uiSelectPrefab == null) return;

            uiSelectPrefab.objectType = typeof(GameObject);
            uiSelectPrefab.allowSceneObjects = false;
            uiSelectPrefab.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;

                var newGo = evt.newValue as GameObject;
                if (newGo == null)
                {
                    int index = callbacks.GetSelectedItemIndex?.Invoke() ?? -1;
                    callbacks.RemoveItemAt?.Invoke(index);
                    callbacks.RefreshPreviewListUI?.Invoke();
                    callbacks.UpdatePropertyPanelFromSelectedItem?.Invoke();
                }
                else
                {
                    var profile = callbacks.GetCurrentProfile?.Invoke();
                    int index = callbacks.GetSelectedItemIndex?.Invoke() ?? -1;
                    callbacks.AssignPrefabToItem?.Invoke(profile, index, newGo);
                }
            });
        }

        private Vector2 SanitizeRange(Vector2 v, float min, float max = float.MaxValue)
        {
            v.x = Mathf.Clamp(v.x, min, max);
            v.y = Mathf.Clamp(v.y, v.x, max);
            return v;
        }

        private int EnsureOdd(int v)
        {
            v = Mathf.Max(3, v);
            return (v % 2 == 0) ? v + 1 : v;
        }

        private void UpdateSliderLimits(MinMaxSlider slider, Vector2 value, float absMin, float absMax)
        {
            slider.lowLimit = Mathf.Min(absMin, value.x);
            slider.highLimit = Mathf.Max(absMax, value.y);
        }
    }

    internal static class ViewExtensions
    {
        public static void SetClickHandler(this Button b, Action a)
        {
            if (b != null)
            {
                if (b.userData is Action old) b.clicked -= old;
                b.userData = a;
                b.clicked += a;
            }
        }
    }
}
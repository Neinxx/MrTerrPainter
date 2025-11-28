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
            public Action<float> BatchSetMinRadius;
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

            BindField<FloatField, float>("MinRadius",
                i => i.minRadius, (i, v) => i.minRadius = Mathf.Max(0f, v),
                setup: f => f.tooltip = "最小半径（米），用于控制实例间最小中心距以减少重叠");

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

            var btnBatchMinRadius = root.Q<Button>("BatchMinRadiusButton");
            if (btnBatchMinRadius != null)
            {
                btnBatchMinRadius.tooltip = "将当前选中条目的最小半径批量应用到选中的条目";
                btnBatchMinRadius.SetClickHandler(() =>
                {
                    var item = callbacks.GetSelectedItem?.Invoke();
                    if (item == null) return;
                    callbacks.BatchSetMinRadius?.Invoke(item.minRadius);
                    callbacks.RefreshVegetationListUI?.Invoke();
                    callbacks.RefreshPreviewListUI?.Invoke();
                    callbacks.UpdatePropertyPanelFromSelectedItem?.Invoke();
                });
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

            ApplyFacadeLocalization();
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
            if (field == null && name == "YrotationRange") field = root.Q<MinMaxSlider>("YRotationRange");
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
                if (field is EnumField ef)
                {
                    var val = (Enum)(object)getter(cfg);
                    ef.Init(val);
                    ef.SetValueWithoutNotify(val);
                }
                else
                {
                    field.SetValueWithoutNotify(getter(cfg));
                }
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

        private void ApplyFacadeUISimplificationIfEnabled()
        {
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            if (cfg == null || !cfg.useSimplifiedFacadeUI) return;

            string[] advancedNames =
            {
                // 检测/平滑/预览高级参数
                "EdgeSlopeThreshold",
                "FacadeEnterSlope",
                "FacadeExitSlope",
                "ProbeStep",
                "ProbeMaxDist",
                "FacadeRefHeight", // 由简化模式改用统一 ReferenceHeight 字段
                "PreviewBottomColor",
                "PreviewTopColor",
                "FacadeSmoothMode",
                "FacadeSmoothWindow",
                "FacadeSmoothSigma",
                "RdpEpsilon",
                // 通用高阶条件
                "HeigthRange",
                "SlopeRange"
            };

            for (int i = 0; i < advancedNames.Length; i++)
            {
                var el = root.Q<VisualElement>(advancedNames[i]);
                if (el != null) el.style.display = DisplayStyle.None;
            }
        }

        private void ApplyFacadeLocalization()
        {
            SetLabelAndTooltip("FacadeMinSpacing", "最小间距(米)", "封边石的最小中心距，过小会重叠。");
            SetLabelAndTooltip("EdgeSlopeThreshold", "最小坡度(度)", "仅当坡度大于该值时判定为立面区域。");
            SetLabelAndTooltip("EmbedDepthRange", "嵌入深度(米)", "沿法线向内嵌入的范围，数值越大越深入墙体。");
            SetLabelAndTooltip("FacadeEnterSlope", "进入陡坡阈值(度)", "开始检测立面的坡度门槛，值越大越严格。");
            SetLabelAndTooltip("FacadeExitSlope", "退出平缓阈值(度)", "结束立面检测的坡度门槛，值越小越容易结束。");
            SetLabelAndTooltip("ProbeStep", "探测步长(米)", "沿前进方向的采样步长，增大更平滑，减小更贴细节。");
            SetLabelAndTooltip("ProbeMaxDist", "最大探测距离(米)", "单次扫描的最大范围，过小可能无法覆盖完整立面。");
            SetLabelAndTooltip("FacadeRefHeight", "参考高度(米)", "将立面高度换算为本地Y缩放的参照值。");
            SetLabelAndTooltip("FacadeScaleOffset", "缩放偏移(XYZ)", "在自适应缩放基础上的XYZ加法偏置，用于微调大小。");
            SetLabelAndTooltip("FacadeOffsets", "位置偏移(XYZ)", "立面坐标系：X右、Y上、Z朝墙；负Z为外推。");
            SetLabelAndTooltip("YrotationRange", "旋转范围(度)", "绕立面法线的扭转角范围，用于造型差异(0-360)。");
            SetLabelAndTooltip("UseContourDetection", "使用等值线检测", "用高度图等值线替代射线检测，可获得更连续的边线。");
            SetLabelAndTooltip("ContourSlopeDeg", "等值线坡度(度)", "提取坡度≥阈值的连通线，增大值可缩小检测范围。");

            SetLabelAndTooltip("FacadeSmoothMode", "平滑模式", "均值/高斯/中位数，用于消除锯齿与噪声。");
            SetLabelAndTooltip("FacadeSmoothWindow", "平滑窗口(奇数)", "窗口越大越平滑，但可能损失细节，建议7-11。");
            SetLabelAndTooltip("FacadeSmoothSigma", "高斯Sigma", "仅高斯模式有效，控制权重分布宽度。");
            SetLabelAndTooltip("RdpEpsilon", "简化容差(米)", "RDP折线简化容差，增大将减少节点数量。");
            SetLabelAndTooltip("PreviewBottomColor", "底边颜色", "立面底边的预览颜色。");
            SetLabelAndTooltip("PreviewTopColor", "顶边颜色", "立面顶边的预览颜色。");
        }

        private void SetLabelAndTooltip(string name, string label, string tip)
        {
            var el0 = root.Q<FloatField>(name); if (el0 != null) { el0.label = label; el0.tooltip = tip; return; }
            var el1 = root.Q<Slider>(name); if (el1 != null) { el1.label = label; el1.tooltip = tip; return; }
            var el2 = root.Q<MinMaxSlider>(name); if (el2 != null) { el2.label = label; el2.tooltip = tip; return; }
            var el3 = root.Q<IntegerField>(name); if (el3 != null) { el3.label = label; el3.tooltip = tip; return; }
            var el4 = root.Q<EnumField>(name); if (el4 != null) { el4.label = label; el4.tooltip = tip; return; }
            var el5 = root.Q<Toggle>(name); if (el5 != null) { el5.label = label; el5.tooltip = tip; return; }
            var el6 = root.Q<Vector3Field>(name); if (el6 != null) { el6.label = label; el6.tooltip = tip; return; }
            var el7 = root.Q<ColorField>(name); if (el7 != null) { el7.label = label; el7.tooltip = tip; return; }
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

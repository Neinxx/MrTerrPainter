using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Services;
using MrTerrainPainter.Editor.Tools; // 引用 MTPBrushContext
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace MrTerrainPainter.Editor.Tools
{
    [Overlay(typeof(SceneView), "MTP Brush")]
    public class MTPBrushOverlay : Overlay
    {
        // --- UI 引用 ---
        private VisualElement _root;
        private Button _openSettingsBtn;
        private VisualElement _mappingWarning;
        private Button _fixMappingBtn;
        private VisualElement _brushContent;
        private DropdownField _profilesDropdown;

        // --- 状态 ---
        private bool _subscribed;
        private bool _updateQueued;
        private bool _repaintQueued;
        private BrushSettings _boundBrush;

        public override VisualElement CreatePanelContent()
        {
            SubscribeEvents();

            // 1. 加载配置
            var cfg = ConfigTools.LoadOrCreateAsset();
            cfg.mappingEntries ??= new List<MrTerrainPainterConfig.MappingEntry>();

            // 2. 加载 UXML
            var vt = ConfigTools.GetBrushOverlayUxml(cfg);
            if (vt == null) return new Label("MTP Error: Overlay UXML missing") { style = { color = Color.red } };

            _root = vt.Instantiate();

            // 加载 USS (可选，确保样式生效)
            var style = ConfigTools.GetStylesUss(cfg);
            if (style != null) _root.styleSheets.Add(style);

            // 3. 获取 UI 元素
            _openSettingsBtn = _root.Q<Button>("OpenSettingsBtn");
            _mappingWarning = _root.Q<VisualElement>("MappingWarning");
            _fixMappingBtn = _root.Q<Button>("FixMappingBtn");
            _brushContent = _root.Q<VisualElement>("BrushContent");
            _profilesDropdown = _root.Q<DropdownField>("Profiles");

            // 4. 初始化逻辑
            SetupNavigation();
            SetupNormalToggle(cfg);
            SetupProfiles();

            // 5. 绑定笔刷数据
            BindBrushSettings(MTPBrushContext.Brush);

            // 6. 刷新初始状态
            RefreshVisibility(cfg);

            return _root;
        }

        public override void OnWillBeDestroyed()
        {
            UnsubscribeEvents();
            UnbindBrushSettings();
            base.OnWillBeDestroyed();
        }

        #region Event Management

        private void SubscribeEvents()
        {
            if (_subscribed) return;
            _subscribed = true;

            ToolManager.activeToolChanged += OnStateChanged;
            Selection.selectionChanged += OnStateChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            ConfigTools.CompletenessChanged += OnCompletenessChanged;
            MrTerrainPainterWindow.WindowStateChanged += OnWindowStateChanged;
            MTPBrushContext.BrushReplaced += OnBrushReplaced;
            MTPBrushContext.ProfileChanged += OnExternalProfileChanged;
            MTPBrushContext.ExtrasChanged += OnExtrasChanged;
            ConfigTools.ConfigUpdated += OnConfigUpdated;
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed) return;
            _subscribed = false;

            ToolManager.activeToolChanged -= OnStateChanged;
            Selection.selectionChanged -= OnStateChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            ConfigTools.CompletenessChanged -= OnCompletenessChanged;
            MrTerrainPainterWindow.WindowStateChanged -= OnWindowStateChanged;
            MTPBrushContext.BrushReplaced -= OnBrushReplaced;
            MTPBrushContext.ProfileChanged -= OnExternalProfileChanged;
            MTPBrushContext.ExtrasChanged -= OnExtrasChanged;
            ConfigTools.ConfigUpdated -= OnConfigUpdated;
        }

        #endregion

        #region Setup & Binding

        private void SetupNavigation()
        {
            // 打开主窗口按钮
            if (_openSettingsBtn != null)
            {
                _openSettingsBtn.clicked += () =>
                {
                    var win = MrTerrainPainterWindow.GetOrOpen();
                    win?.OpenPaintingSettings();
                };
            }

            // 修复映射按钮
            if (_fixMappingBtn != null)
            {
                _fixMappingBtn.clicked += MrTerrainPainterSettingsWindow.Open;
            }
        }

        private void SetupNormalToggle(MrTerrainPainterConfig cfg)
        {
            var toggle = _root.Q<Toggle>("NormalDirection");
            if (toggle != null)
            {
                toggle.SetValueWithoutNotify(cfg.normalDirection);
                toggle.RegisterValueChangedCallback(e => ConfigTools.SetNormalDirection(cfg, e.newValue));
                // 监听外部变更
                ConfigTools.NormalDirectionChanged += v => toggle.SetValueWithoutNotify(v);
            }
        }

        private void SetupProfiles()
        {
            if (_profilesDropdown == null) return;

            RefreshProfileDropdownList();

            _profilesDropdown.RegisterValueChangedCallback(evt =>
            {
                var profiles = GetCurrentAvailableProfiles();
                // 根据名字反查 Profile
                var profile = profiles.FirstOrDefault(p => p != null && p.name == evt.newValue);
                if (profile != null)
                {
                    // 更新上下文
                    MTPBrushContext.CurrentProfile = profile;
                    // 同步给窗口
                    if (MrTerrainPainterWindow.TryGet(out var win))
                        win.SetCurrentProfilePublic(profile);
                }
            });
        }

        private void BindBrushSettings(BrushSettings brush)
        {
            UnbindBrushSettings();
            _boundBrush = brush;
            if (_boundBrush == null) return;

            _boundBrush.Changed += OnBrushPropertyChanged;

            // 1. 绑定滑条 (使用浮点 Slider，直接绑定)
            BindSlider("Size", () => brush.size, v => brush.size = v);
            BindSlider("Strength", () => brush.strength, v => brush.strength = v);
            BindSlider("Density", () => brush.densityScale, v => brush.densityScale = v);
            BindSlider("Hardness", () => brush.hardness, v => brush.hardness = v);
            BindSlider("StrokeSpacing", () => brush.strokeSpacingFactor, v => brush.strokeSpacingFactor = v);
            BindSlider("StrokeSpacingAbs", () => brush.strokeSpacingAbsolute, v => brush.strokeSpacingAbsolute = v);

            // 2. [关键修复] 绑定 Enum，修复下拉框失效
            BindEnum<DistributionType>("Distribution", brush.distribution, v => brush.distribution = v);

            // 3. 绑定 Toggle
            BindToggle("MixExtraProfiles", () => brush.mixExtraProfiles, v => brush.mixExtraProfiles = v);

            // 4. 绝对间距联动逻辑
            var absToggle = _root.Q<Toggle>("UseAbsoluteStrokeSpacing");
            var absSlider = _root.Q<Slider>("StrokeSpacingAbs"); // 注意这里是 Slider 不是 SliderInt

            if (absToggle != null)
            {
                absToggle.SetValueWithoutNotify(brush.useAbsoluteStrokeSpacing);
                absSlider?.SetEnabled(brush.useAbsoluteStrokeSpacing);

                absToggle.RegisterValueChangedCallback(evt =>
                {
                    brush.useAbsoluteStrokeSpacing = evt.newValue;
                    absSlider?.SetEnabled(evt.newValue);
                });
            }
        }

        private void UnbindBrushSettings()
        {
            if (_boundBrush != null)
            {
                _boundBrush.Changed -= OnBrushPropertyChanged;
                _boundBrush = null;
            }
        }
        private void OnConfigUpdated()
        {
            // 重新加载配置并刷新界面可见性
            var cfg = ConfigTools.LoadOrCreateAsset();
            RefreshVisibility(cfg);
            // 如果有绑定的 Toggle (如法线方向)，也需要刷新
            var toggle = _root.Q<Toggle>("NormalDirection");
            if (toggle != null) toggle.SetValueWithoutNotify(cfg.normalDirection);
        }

        #endregion

        #region Update Logic

        private void RefreshVisibility(MrTerrainPainterConfig cfg = null)
        {
            cfg ??= ConfigTools.LoadOrCreateAsset();
            bool isComplete = ConfigTools.IsComplete(cfg, out _);

            // 1. 显示/隐藏 Mapping 警告条
            if (_mappingWarning != null)
                _mappingWarning.style.display = isComplete ? DisplayStyle.None : DisplayStyle.Flex;

            // 2. 如果配置不完整，禁用笔刷控件交互
            _brushContent?.SetEnabled(isComplete);

            // 3. 刷新控件可见性 (基于窗口模式)
            UpdateFeatureVisibility();

            // 4. 决定 Overlay 是否显示
            UpdateOverlayDisplayState();
        }

        private static readonly string[] BrushParamsControls =
       {
            "Profiles", "Size", "Strength", "Density",
            "Hardness", "Distribution", "MixExtraProfiles"
        };

        private static readonly string[] HelperControls =
        {
            "NormalDirection"
        };

        private void UpdateFeatureVisibility()
        {
            // 获取窗口状态
            bool windowOpen = MrTerrainPainterWindow.TryGet(out var win);

            // 如果窗口没打开，painting 和 settings 自然为 false
            bool isPainting = windowOpen && win.IsPaintingModePublic();
            bool isSettings = windowOpen && win.IsSettingsOpenPublic();

            // 2. 使用布尔逻辑推导显示状态
            // 逻辑分析：
            // - 笔刷参数：除了"绘画模式"(窗口已有参数)外，其他情况都显示
            bool showBrushParams = !isPainting;

            // - 辅助功能：除了"设置模式"(不需要辅助)外，其他情况都显示
            bool showHelpers = !isSettings;

            // 3. 应用状态
            SetVisibility(showBrushParams, BrushParamsControls);
            SetVisibility(showHelpers, HelperControls);
        }

        // 通用辅助方法：根据 bool 设置一组控件的显隐
        private void SetVisibility(bool visible, string[] controlNames)
        {
            var style = visible ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var name in controlNames)
            {
                var el = _root.Q(name);
                if (el != null) el.style.display = style;
            }
        }

        private void RefreshProfileDropdownList()
        {
            if (_profilesDropdown == null) return;
            var profiles = GetCurrentAvailableProfiles();
            _profilesDropdown.choices = profiles.Select(p => p ? p.name : "<Null>").ToList();

            var current = MTPBrushContext.CurrentProfile;
            // 设置当前值
            _profilesDropdown.SetValueWithoutNotify(current != null && profiles.Contains(current) ? current.name : null);
        }

        private void UpdateOverlayDisplayState()
        {
            if (_updateQueued) return;
            _updateQueued = true;

            // 延迟一帧更新，防止布局抖动
            _root?.schedule.Execute(() =>
            {
                _updateQueued = false;
                bool isToolActive = ToolManager.activeToolType == typeof(MTPBrushTool);
                bool hasTerrain = Selection.activeGameObject?.GetComponent<Terrain>() != null;

                // 控制 Overlay 本身的显示/隐藏
                displayed = isToolActive && hasTerrain;
            });
        }

        private void OnBrushPropertyChanged(string propertyName)
        {
            // 双向绑定：笔刷数据变了 -> 更新 UI
            var b = _boundBrush;
            if (b == null) return;

            switch (propertyName)
            {
                case nameof(BrushSettings.size): UpdateSliderValue("Size", b.size); break;
                case nameof(BrushSettings.strength): UpdateSliderValue("Strength", b.strength); break;
                case nameof(BrushSettings.densityScale): UpdateSliderValue("Density", b.densityScale); break;
                case nameof(BrushSettings.hardness): UpdateSliderValue("Hardness", b.hardness); break;
                case nameof(BrushSettings.strokeSpacingFactor): UpdateSliderValue("StrokeSpacing", b.strokeSpacingFactor); break;
                case nameof(BrushSettings.strokeSpacingAbsolute): UpdateSliderValue("StrokeSpacingAbs", b.strokeSpacingAbsolute); break;
                    // 其他属性如 Enum/Toggle 变化频率低，这里视情况添加
            }
            RequestSceneRepaint();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 绑定 EnumField，显式调用 Init 修复显示问题
        /// </summary>
        private void BindEnum<T>(string name, Enum value, Action<T> setter) where T : Enum
        {
            var field = _root.Q<EnumField>(name);
            if (field == null) return;

            field.Init(value); // 关键：初始化选项
            field.SetValueWithoutNotify(value);

            field.RegisterValueChangedCallback(evt => setter((T)evt.newValue));
        }

        /// <summary>
        /// 绑定浮点 Slider
        /// </summary>
        private void BindSlider(string name, Func<float> getter, Action<float> setter)
        {
            var slider = _root.Q<Slider>(name);
            if (slider == null) return;

            slider.SetValueWithoutNotify(getter());
            slider.RegisterValueChangedCallback(evt => setter(evt.newValue));
        }

        private void UpdateSliderValue(string name, float val)
        {
            var slider = _root.Q<Slider>(name);
            slider?.SetValueWithoutNotify(val);
        }

        private void BindToggle(string name, Func<bool> getter, Action<bool> setter)
        {
            var toggle = _root.Q<Toggle>(name);
            if (toggle != null)
            {
                toggle.SetValueWithoutNotify(getter());
                toggle.RegisterValueChangedCallback(evt => setter(evt.newValue));
            }
        }

        private List<VegetationProfile> GetCurrentAvailableProfiles()
        {
            // 优先从 Session 获取
            if (MrTerrainPainterWindow.TryGet(out var win) && win.Session != null)
                return win.Session.AvailableProfiles;

            // 否则手动加载
            var list = new List<VegetationProfile>();
            foreach (var guid in AssetDatabase.FindAssets("t:VegetationProfile"))
            {
                var vp = AssetDatabase.LoadAssetAtPath<VegetationProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (vp != null) list.Add(vp);
            }
            return list;
        }

        private void RequestSceneRepaint()
        {
            if (_repaintQueued) return;
            _repaintQueued = true;
            EditorApplication.delayCall += () => { _repaintQueued = false; SceneView.RepaintAll(); };
        }

        // 回调代理
        private void OnStateChanged() => RefreshVisibility();
        private void OnProjectChanged() { RefreshProfileDropdownList(); RefreshVisibility(); }
        private void OnCompletenessChanged(bool c) => RefreshVisibility();
        private void OnWindowStateChanged(bool o, bool s, bool p) { RefreshProfileDropdownList(); RefreshVisibility(); RequestSceneRepaint(); }
        private void OnBrushReplaced() { BindBrushSettings(MTPBrushContext.Brush); RequestSceneRepaint(); }
        private void OnExternalProfileChanged(VegetationProfile p) { RefreshProfileDropdownList(); RequestSceneRepaint(); }
        private void OnExtrasChanged() { UpdateFeatureVisibility(); RequestSceneRepaint(); }

        private void ShowControls(params string[] names) => SetDisplay(DisplayStyle.Flex, names);
        private void HideControls(params string[] names) => SetDisplay(DisplayStyle.None, names);
        private void SetDisplay(DisplayStyle style, params string[] names)
        {
            if (_root == null) return;
            foreach (var name in names) { var el = _root.Q(name); if (el != null) el.style.display = style; }
        }

        #endregion
    }
}
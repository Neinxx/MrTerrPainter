using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Services;
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
        // --- UI 缓存引用 (避免频繁 Q<T>) ---
        private VisualElement _root;
        private Button _openSettingsBtn;
        private VisualElement _mappingWarning;
        private Button _fixMappingBtn;
        private VisualElement _brushContent;
        private DropdownField _profilesDropdown;

        // 笔刷参数控件缓存
        private Slider _sizeSlider;
        private Slider _strengthSlider;
        private Slider _densitySlider;
        private Slider _hardnessSlider;
        private EnumField _distributionField;
        private Toggle _mixExtraToggle;
        private Slider _spacingFactorSlider;
        private Slider _spacingAbsSlider;
        private Toggle _useAbsSpacingToggle;
        private Toggle _normalDirToggle;

        // --- 状态与数据 ---
        private bool _subscribed;
        private bool _updateQueued;
        private bool _repaintQueued;
        private BrushSettings _boundBrush;

        // 缓存 Profile 列表，避免每帧扫描 AssetDatabase
        private List<VegetationProfile> _cachedProfiles = new List<VegetationProfile>();
        private bool _profilesDirty = true;

        public override VisualElement CreatePanelContent()
        {
            // 1. 加载配置
            var cfg = ConfigTools.LoadOrCreateAsset();
            cfg.mappingEntries ??= new List<MrTerrainPainterConfig.MappingEntry>();

            // 2. 加载 UXML
            var vt = ConfigTools.GetBrushOverlayUxml(cfg);
            if (vt == null) return new Label("MTP Error: Overlay UXML missing") { style = { color = Color.red } };

            _root = vt.Instantiate();

            // 加载 USS
            var style = ConfigTools.GetStylesUss(cfg);
            if (style != null) _root.styleSheets.Add(style);

            // 3. 初始化并缓存 UI 引用 (核心优化)
            CacheUiElements();

            // 4. 绑定静态逻辑
            SetupStaticEvents();
            SetupProfileDropdown();
            SetupNormalToggle(cfg);

            // 5. 事件订阅
            SubscribeEvents();

            // 6. 绑定数据与初始状态
            new BrushBinder().Bind(MTPBrushContext.Brush, _sizeSlider, _strengthSlider, _densitySlider, _hardnessSlider, _distributionField, _mixExtraToggle, _spacingFactorSlider, _spacingAbsSlider, _useAbsSpacingToggle);
            RefreshProfilesIfNeeded();
            RefreshVisibility(cfg);

            return _root;
        }

        public override void OnWillBeDestroyed()
        {
            UnbindBrushSettings();
            UnsubscribeEvents();
            base.OnWillBeDestroyed();
        }

        #region UI Initialization & Caching

        private void CacheUiElements()
        {
            // 顶层结构
            _openSettingsBtn = _root.Q<Button>("OpenSettingsBtn");
            _mappingWarning = _root.Q<VisualElement>("MappingWarning");
            _fixMappingBtn = _root.Q<Button>("FixMappingBtn");
            _brushContent = _root.Q<VisualElement>("BrushContent");
            _profilesDropdown = _root.Q<DropdownField>("Profiles");

            // 笔刷参数
            _sizeSlider = _root.Q<Slider>("Size");
            _strengthSlider = _root.Q<Slider>("Strength");
            _densitySlider = _root.Q<Slider>("Density");
            _hardnessSlider = _root.Q<Slider>("Hardness");
            _distributionField = _root.Q<EnumField>("Distribution");
            _mixExtraToggle = _root.Q<Toggle>("MixExtraProfiles");

            // 间距设置
            _spacingFactorSlider = _root.Q<Slider>("StrokeSpacing");
            _spacingAbsSlider = _root.Q<Slider>("StrokeSpacingAbs");
            _useAbsSpacingToggle = _root.Q<Toggle>("UseAbsoluteStrokeSpacing");

            // 辅助设置
            _normalDirToggle = _root.Q<Toggle>("NormalDirection");
        }

        private void SetupStaticEvents()
        {
            if (_openSettingsBtn != null)
            {
                _openSettingsBtn.clicked += () =>
                {
                    var win = MrTerrainPainterWindow.GetOrOpen();
                    win?.OpenPaintingSettings();
                };
            }

            if (_fixMappingBtn != null)
                _fixMappingBtn.clicked += MrTerrainPainterSettingsWindow.Open;
        }

        #endregion

        #region Event Management

        private void SubscribeEvents()
        {
            if (_subscribed) return;
            _subscribed = true;

            ToolManager.activeToolChanged += OnStateChanged;
            Selection.selectionChanged += OnStateChanged;
            EditorApplication.projectChanged += OnProjectChanged; // 监听资源变更

            ConfigTools.CompletenessChanged += OnCompletenessChanged;
            ConfigTools.ConfigUpdated += OnConfigUpdated;
            ConfigTools.NormalDirectionChanged += OnNormalDirectionExternalChanged;

            MrTerrainPainterWindow.WindowStateChanged += OnWindowStateChanged;
            MrTerrainPainterWindow.ProfilesUpdated += OnProfilesUpdated;

            MTPBrushContext.BrushReplaced += OnBrushReplaced;
            MTPBrushContext.ProfileChanged += OnExternalProfileChanged;
            MTPBrushContext.ExtrasChanged += OnExtrasChanged;
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed) return;
            _subscribed = false;

            ToolManager.activeToolChanged -= OnStateChanged;
            Selection.selectionChanged -= OnStateChanged;
            EditorApplication.projectChanged -= OnProjectChanged;

            ConfigTools.CompletenessChanged -= OnCompletenessChanged;
            ConfigTools.ConfigUpdated -= OnConfigUpdated;
            ConfigTools.NormalDirectionChanged -= OnNormalDirectionExternalChanged;

            MrTerrainPainterWindow.WindowStateChanged -= OnWindowStateChanged;
            MrTerrainPainterWindow.ProfilesUpdated -= OnProfilesUpdated;

            MTPBrushContext.BrushReplaced -= OnBrushReplaced;
            MTPBrushContext.ProfileChanged -= OnExternalProfileChanged;
            MTPBrushContext.ExtrasChanged -= OnExtrasChanged;
        }

        #endregion

        #region Logic & Binding

        private void SetupNormalToggle(MrTerrainPainterConfig cfg)
        {
            if (_normalDirToggle == null) return;

            _normalDirToggle.SetValueWithoutNotify(cfg.normalDirection);
            _normalDirToggle.RegisterValueChangedCallback(e => ConfigTools.SetNormalDirection(cfg, e.newValue));
        }

        private void OnNormalDirectionExternalChanged(bool v)
        {
            _normalDirToggle?.SetValueWithoutNotify(v);
        }

        private void SetupProfileDropdown()
        {
            if (_profilesDropdown == null) return;

            _profilesDropdown.RegisterValueChangedCallback(evt =>
            {
                // 仅在需要时查找 Profile
                var profile = _cachedProfiles.FirstOrDefault(p => p != null && p.name == evt.newValue);
                if (profile != null)
                {
                    MTPBrushContext.CurrentProfile = profile;
                    if (MrTerrainPainterWindow.TryGet(out var win))
                        win.SetCurrentProfilePublic(profile);
                }
            });
        }

        private void BindBrushSettings(BrushSettings brush) { }

        private void UnbindBrushSettings() { }

        /// <summary>
        /// UI 双向绑定响应：数据变了 -> 更新 UI
        /// </summary>
        private void OnBrushPropertyChanged(string propertyName) { }

        #endregion

        #region Profile Management (Optimized)

        private void OnProjectChanged()
        {
            _profilesDirty = true;
            RefreshProfilesIfNeeded();
            RefreshVisibility();
        }

        private void RefreshProfilesIfNeeded()
        {
            // --- 1. 列表数据缓存逻辑 (仅在标记为 Dirty 时重新加载) ---
            if (_profilesDirty || _cachedProfiles.Count == 0)
            {
                _cachedProfiles.Clear();
                if (MrTerrainPainterWindow.TryGet(out var win) && win.Session != null)
                {
                    _cachedProfiles.AddRange(win.Session.AvailableProfiles);
                }
                else
                {
                    _profilesDirty = false;
                }

                _profilesDirty = false; // 重置脏标记

                // 同步更新下拉框的“选项列表” (Choices)
                if (_profilesDropdown != null)
                {
                    var next = _cachedProfiles.Select(p => p ? p.name : "<Null>").ToList();
                    var prev = _profilesDropdown.choices ?? new System.Collections.Generic.List<string>();
                    bool same = prev.Count == next.Count;
                    if (same)
                    {
                        for (int i = 0; i < prev.Count; i++) { if (prev[i] != next[i]) { same = false; break; } }
                    }
                    if (!same) _profilesDropdown.choices = next;
                }
            }

            // --- 2. 选中项同步逻辑 (必须每次都执行，不能被 return 拦截) ---
            if (_profilesDropdown != null)
            {
                var current = MTPBrushContext.CurrentProfile;

                // 获取当前 Profile 的名字，如果在列表中找不到则为空
                string targetName = null;
                if (current != null)
                {
                    // 简单检查：如果当前 Profile 有效，就尝试显示它的名字
                    // 注意：如果 Profile 是新创建的且 Dirty 还没来得及刷新，这里可能会暂时不显示，
                    // 但通常创建新 Profile 会触发 ProjectChanged 从而设置 Dirty，所以直接用 name 即可。
                    targetName = current.name;
                }

                // 仅当 UI 显示的值与实际值不同时才更新，防止光标闪烁
                if (_profilesDropdown.value != targetName)
                {
                    _profilesDropdown.SetValueWithoutNotify(targetName);
                }
            }
        }

        #endregion

        #region Visibility & Updates

        private void RefreshVisibility(MrTerrainPainterConfig cfg = null)
        {
            cfg ??= ConfigTools.LoadOrCreateAsset();
            bool isComplete = ConfigTools.IsComplete(cfg, out _);

            // 基础 UI 状态
            if (_mappingWarning != null) _mappingWarning.style.display = isComplete ? DisplayStyle.None : DisplayStyle.Flex;
            if (_openSettingsBtn != null) _openSettingsBtn.style.display = isComplete ? DisplayStyle.Flex : DisplayStyle.None;
            _brushContent?.SetEnabled(isComplete);

            // 模式可见性逻辑
            bool windowOpen = MrTerrainPainterWindow.TryGet(out var win);
            bool isPainting = windowOpen && win.IsPaintingModePublic();
            bool isSettings = windowOpen && win.IsSettingsOpenPublic();

            // 笔刷参数: 非绘画模式显示
            // 辅助功能: 非设置模式显示
            SetElementGroupVisibility(isPainting == false, _sizeSlider, _strengthSlider, _densitySlider, _hardnessSlider, _distributionField, _mixExtraToggle);
            SetElementGroupVisibility(isSettings == false, _normalDirToggle);

            // Overlay 自身显示逻辑
            UpdateOverlayDisplayState();
        }

        private void UpdateOverlayDisplayState()
        {
            if (_updateQueued) return;
            _updateQueued = true;

            // 延迟一帧以避免布局抖动
            _root?.schedule.Execute(() =>
            {
                _updateQueued = false;
                bool isToolActive = ToolManager.activeToolType == typeof(MTPBrushTool);

                // 只有选中物体且带有 Terrain 组件时才显示
                bool hasTerrain = Selection.activeGameObject != null &&
                                  Selection.activeGameObject.GetComponent<Terrain>() != null;

                displayed = isToolActive && hasTerrain;
            });
        }

        private void SetElementGroupVisibility(bool visible, params VisualElement[] elements)
        {
            var style = visible ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var el in elements)
            {
                if (el != null) el.style.display = style;
            }
        }

        #endregion

        #region Helpers

        // 泛型绑定 Slider/Toggle，减少重复代码
        private void BindControl<T>(BaseField<T> field, Func<T> getter, Action<T> setter)
        {
            if (field == null) return;
            field.SetValueWithoutNotify(getter());
            // 先移除旧回调防止重复 (虽然实例化时是新的，但这是好习惯)
            field.RegisterValueChangedCallback(evt => setter(evt.newValue));
        }

        private void UpdateValue(Slider slider, float val) => slider?.SetValueWithoutNotify(val);

        private void RequestSceneRepaint()
        {
            if (_repaintQueued) return;
            _repaintQueued = true;
            EditorApplication.delayCall += () =>
            {
                _repaintQueued = false;
                SceneView.RepaintAll();
            };
        }

        // 事件回调代理
        private void OnStateChanged() => RefreshVisibility();
        private void OnConfigUpdated() => RefreshVisibility();
        private void OnCompletenessChanged(bool c) => RefreshVisibility();
        private void OnWindowStateChanged(bool o, bool s, bool p)
        {
            _profilesDirty = true; // 窗口状态改变可能影响 Session，标记脏数据
            RefreshProfilesIfNeeded();
            RefreshVisibility();
            RequestSceneRepaint();
        }
        private void OnBrushReplaced()
        {
            BindBrushSettings(MTPBrushContext.Brush);
            RequestSceneRepaint();
        }
        private void OnExternalProfileChanged(VegetationProfile p)
        {
            RefreshProfilesIfNeeded(); // 只更新选中的值，不需要完全重建列表
            RequestSceneRepaint();
        }
        private void OnExtrasChanged()
        {
            RefreshVisibility();
            RequestSceneRepaint();
        }

        private void OnProfilesUpdated()
        {
            _profilesDirty = true;
            RefreshProfilesIfNeeded();
        }

        #endregion
    }
}

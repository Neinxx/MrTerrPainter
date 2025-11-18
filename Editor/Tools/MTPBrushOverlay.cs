using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Services;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Tools
{
    [Overlay(typeof(SceneView), "MTP Brush")]
    public class MTPBrushOverlay : Overlay
    {
        // 状态标记
        private bool _subscribed;
        private bool _updateQueued;
        private double _lastUpdateTime;

        // UI 缓存引用 (避免频繁查询)
        private VisualElement _root;
        private Button _mappingBtn;
        private Button _paintingBtn;
        private VisualElement _otherNode;
        private VisualElement _buttonNode;
        private DropdownField _profilesDropdown;
        private MrTerrainPainter.Editor.Services.BrushSettings _subscribedBrush;
        private bool _repaintQueued;
        private EventCallback<ChangeEvent<string>> _onProfilesChanged;
        private bool _profilesDropdownSubscribed;



        public override VisualElement CreatePanelContent()
        {
            // 1. 事件订阅
            SubscribeEvents();

            // 2. 加载配置与资源
            var cfg = Config.ConfigTools.LoadOrCreateAsset();
            if (cfg.mappingEntries == null) cfg.mappingEntries = new List<Config.MrTerrainPainterConfig.MappingEntry>();
            var vt = Config.ConfigTools.GetBrushOverlayUxml(cfg);

            if (vt == null)
            {
                return new Label("MTP Error: Overlay UXML 未配置或未找到") { style = { color = Color.red } };
            }

            _root = vt.Instantiate();
            var style = Config.ConfigTools.GetStylesUss(cfg);
            if (style != null) _root.styleSheets.Add(style);

            // 3. 获取 UI 元素引用
            _mappingBtn = _root.Q<Button>("PlaseMapping");
            _paintingBtn = _root.Q<Button>("Painting");
            _otherNode = _root.Q<VisualElement>("Other");
            _buttonNode = _root.Q<VisualElement>("buttonNode");
            _profilesDropdown = _root.Q<DropdownField>("Profiles");

            var brush = MTPBrushContext.Brush;

            // 4. 初始化各模块
            SetupMappingLogic(cfg);
            SetupPaintingButton(cfg);
            SetupNormalDirectionToggle(cfg);
            SetupProfilesLogic();
            SetupSliders(brush);
            SetupAdditionalSettings(brush);
            SubscribeBrushChanges(brush);

            // 初始化显示状态
            ToggleMappingVisibility(cfg);
            UpdatePanelFeatureVisibility();

            return _root;
        }

        public override void OnWillBeDestroyed()
        {
            UnsubscribeEvents();
            UnsubscribeBrushChanges();
            if (_profilesDropdownSubscribed && _profilesDropdown != null && _onProfilesChanged != null)
            {
                _profilesDropdown.UnregisterValueChangedCallback(_onProfilesChanged);
                _profilesDropdownSubscribed = false;
            }
            base.OnWillBeDestroyed();
        }

        #region Setup Methods (初始化分块)

        private void SubscribeEvents()
        {
            if (_subscribed) return;
            _subscribed = true;
            ToolManager.activeToolChanged += OnActiveToolChanged;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            Config.ConfigTools.CompletenessChanged += OnCompletenessChanged;
            MrTerrainPainterWindow.WindowStateChanged += OnWindowStateChanged;
            MrTerrainPainter.Editor.Tools.MTPBrushContext.BrushReplaced += OnBrushReplaced;
            // 初始化时更新一次可见性
            UpdateVisibility();
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed) return;
            ToolManager.activeToolChanged -= OnActiveToolChanged;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            Config.ConfigTools.CompletenessChanged -= OnCompletenessChanged;
            MrTerrainPainterWindow.WindowStateChanged -= OnWindowStateChanged;
            MrTerrainPainter.Editor.Tools.MTPBrushContext.BrushReplaced -= OnBrushReplaced;
            _subscribed = false;
        }

        private void SetupMappingLogic(Config.MrTerrainPainterConfig cfg)
        {
            if (_mappingBtn != null)
            {
                _mappingBtn.clicked += () =>
                {
                    MrTerrainPainterSettingsWindow.Open();
                };
            }
        }

        private void SetupPaintingButton(Config.MrTerrainPainterConfig cfg)
        {
            if (_paintingBtn != null)
            {
                _paintingBtn.clicked += () =>
                {
                    var win = MrTerrainPainterWindow.TryGet(out var existing) ? existing : MrTerrainPainterWindow.GetOrOpen();
                    if (win != null)
                    {
                        if (Config.ConfigTools.GuardAndOpenSettingsOnlyIfIncomplete(win))
                        {
                            win.OpenPaintingSettings();
                        }
                    }
                };
            }
        }

        private void SetupNormalDirectionToggle(Config.MrTerrainPainterConfig cfg)
        {
            var normalToggle = _root.Q<Toggle>("NormalDirection");
            if (normalToggle != null)
            {
                normalToggle.SetValueWithoutNotify(cfg.normalDirection);
                normalToggle.RegisterValueChangedCallback(evt =>
                {
                    Config.ConfigTools.SetNormalDirection(cfg, evt.newValue);
                });
                Config.ConfigTools.NormalDirectionChanged += v =>
                {
                    normalToggle.SetValueWithoutNotify(v);
                };
            }
        }

        private void SetupProfilesLogic()
        {
            if (_profilesDropdown == null) return;

            RefreshProfileList();
            _onProfilesChanged = evt =>
            {
                var list = GetProfilesFromWindowOrProject();
                var choices = _profilesDropdown.choices;
                int idx = choices != null ? choices.IndexOf(evt.newValue) : -1;
                if (idx < 0) idx = _profilesDropdown.index;
                if (idx >= 0 && idx < list.Count)
                {
                    var p = list[idx];
                    MTPBrushContext.CurrentProfile = p;
                    if (MrTerrainPainterWindow.TryGet(out var win) && win != null)
                        win.SetCurrentProfilePublic(p);
                }
            };
            UpdateProfilesDropdownInteractivity();

            // 当外部 Profile 改变时同步 UI
            MTPBrushContext.ProfileChanged += vp =>
            {
                // 只有当 UI 存在时才更新
                if (_profilesDropdown != null) RefreshProfileList();
                RequestSceneRepaint();
            };

            MTPBrushContext.ExtrasChanged += () =>
            {
                UpdatePanelFeatureVisibility();
                RequestSceneRepaint();
            };
        }

        private void SetupSliders(BrushSettings brush) // 假设 brush 是 BrushSettings 类型
        {
            BindSlider(_root, "Size", 0.5f, 50f, () => brush.size, v => brush.size = v);
            BindSlider(_root, "Strength", 0.1f, 10f, () => brush.strength, v => brush.strength = v);
            BindSlider(_root, "Density", 0f, 5f, () => brush.densityScale, v => brush.densityScale = v);
            BindSlider(_root, "Hardness", 0f, 1f, () => brush.hardness, v => brush.hardness = v);
            BindSlider(_root, "StrokeSpacing", 0f, 1f, () => brush.strokeSpacingFactor, v => brush.strokeSpacingFactor = v);
        }

        private void SetupAdditionalSettings(BrushSettings brush)
        {
            var dist = _root.Q<EnumField>("Distribution");
            if (dist != null)
            {
                dist.Init(brush.distribution);
                dist.SetValueWithoutNotify(brush.distribution);
                dist.RegisterValueChangedCallback(evt => { brush.distribution = (DistributionType)evt.newValue; });
            }

            var mixExtra = _root.Q<Toggle>("MixExtraProfiles");
            if (mixExtra != null)
            {
                mixExtra.SetValueWithoutNotify(brush.mixExtraProfiles);
                mixExtra.RegisterValueChangedCallback(evt => { brush.mixExtraProfiles = evt.newValue; });
            }

            var useAbs = _root.Q<Toggle>("UseAbsoluteStrokeSpacing");
            if (useAbs != null)
            {
                useAbs.SetValueWithoutNotify(brush.useAbsoluteStrokeSpacing);
                useAbs.RegisterValueChangedCallback(evt => { brush.useAbsoluteStrokeSpacing = evt.newValue; });
            }
            BindSlider(_root, "StrokeSpacingAbs", 0f, 200f, () => brush.strokeSpacingAbsolute, v => brush.strokeSpacingAbsolute = v);
        }

        #endregion

        #region Logic & Helpers (逻辑与辅助)

        private void OnProjectChanged()
        {
            // 资源变动时，刷新 Profile 列表和 UI 状态
            RefreshProfileList();
            var cfg = Config.ConfigTools.LoadOrCreateAsset();
            ToggleMappingVisibility(cfg);
            UpdatePanelFeatureVisibility();
            RequestSceneRepaint();
        }

        private void OnCompletenessChanged(bool isComplete)
        {
            var cfg = Config.ConfigTools.LoadOrCreateAsset();
            ToggleMappingVisibility(cfg);
            UpdatePanelFeatureVisibility();
            RequestSceneRepaint();
        }

        private void RefreshProfileList()
        {
            if (_profilesDropdown == null) return;

            var list = GetProfilesFromWindowOrProject();
            _profilesDropdown.choices = list.Select(p => p != null ? p.name : "<null>").ToList();

            var currentProfile = MTPBrushContext.CurrentProfile;
            int idx = currentProfile != null ? list.IndexOf(currentProfile) : -1;
            if (idx >= 0 && idx < list.Count)
            {
                _profilesDropdown.index = idx;
                _profilesDropdown.SetValueWithoutNotify(list[idx] != null ? list[idx].name : null);
            }
            else
            {
                _profilesDropdown.index = -1;
                _profilesDropdown.SetValueWithoutNotify(null);
            }
        }

        private void UpdateProfilesDropdownInteractivity()
        {
            if (!_profilesDropdownSubscribed && _onProfilesChanged != null)
            {
                _profilesDropdown.RegisterValueChangedCallback(_onProfilesChanged);
                _profilesDropdownSubscribed = true;
            }
        }

        private void ToggleMappingVisibility(Config.MrTerrainPainterConfig cfg)
        {
            bool isConfigComplete = Config.ConfigTools.IsComplete(cfg, out _);

            // 1. 处理 Mapping 按钮
            if (_mappingBtn != null)
                _mappingBtn.style.display = isConfigComplete ? DisplayStyle.None : DisplayStyle.Flex;

            // 2. 处理互斥显示 (Other vs ButtonNode)
            // 确保逻辑互斥，避免状态混淆
            if (isConfigComplete)
            {
                if (_buttonNode != null) _buttonNode.style.display = DisplayStyle.None;
                if (_otherNode != null) _otherNode.style.display = DisplayStyle.Flex;
            }
            else
            {
                if (_buttonNode != null) _buttonNode.style.display = DisplayStyle.Flex;
                if (_otherNode != null) _otherNode.style.display = DisplayStyle.None;
            }
        }

        private void BindSlider(VisualElement root, string name, float min, float max, Func<float> getter, Action<float> setter)
        {
            var slider = root.Q<SliderInt>(name);
            if (slider == null) return;

            // 确保 highValue 有效，防止除零
            var hv = Mathf.Max(1, slider.highValue);

            // 初始化值
            float currentVal = Mathf.Clamp(getter(), min, max);
            var v = Mathf.RoundToInt(Mathf.InverseLerp(min, max, currentVal) * hv);
            slider.SetValueWithoutNotify(v);

            slider.RegisterValueChangedCallback(evt =>
            {
                // 将 SliderInt 的 0-HighValue 映射回 min-max
                var f = Mathf.Lerp(min, max, Mathf.Clamp01(evt.newValue / (float)hv));
                setter(f);
            });
        }

        private void UpdateSlider(string name, float min, float max, Func<float> getter)
        {
            var slider = _root.Q<SliderInt>(name);
            if (slider == null) return;
            var hv = Mathf.Max(1, slider.highValue);
            var currentVal = Mathf.Clamp(getter(), min, max);
            var v = Mathf.RoundToInt(Mathf.InverseLerp(min, max, currentVal) * hv);
            slider.SetValueWithoutNotify(v);
        }

        private void RequestSceneRepaint()
        {
            if (_repaintQueued) return;
            _repaintQueued = true;
            EditorApplication.delayCall += () =>
            {
                _repaintQueued = false;
                var sv = SceneView.lastActiveSceneView;
                if (sv != null) sv.Repaint(); else SceneView.RepaintAll();
            };
        }

        private void SubscribeBrushChanges(MrTerrainPainter.Editor.Services.BrushSettings brush)
        {
            _subscribedBrush = brush;
            if (_subscribedBrush == null) return;
            _subscribedBrush.Changed += OnBrushChanged;
        }

        private void UnsubscribeBrushChanges()
        {
            if (_subscribedBrush == null) return;
            _subscribedBrush.Changed -= OnBrushChanged;
            _subscribedBrush = null;
        }

        private void OnBrushChanged(string propertyName)
        {
            var brush = _subscribedBrush;
            if (brush == null) return;
            if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.size), StringComparison.Ordinal))
            {
                UpdateSlider("Size", 0.5f, 50f, () => brush.size);
            }
            else if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.strength), StringComparison.Ordinal))
            {
                UpdateSlider("Strength", 0.1f, 10f, () => brush.strength);
            }
            else if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.densityScale), StringComparison.Ordinal))
            {
                UpdateSlider("Density", 0f, 5f, () => brush.densityScale);
            }
            else if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.hardness), StringComparison.Ordinal))
            {
                UpdateSlider("Hardness", 0f, 1f, () => brush.hardness);
            }
            else if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.strokeSpacingFactor), StringComparison.Ordinal))
            {
                UpdateSlider("StrokeSpacing", 0f, 1f, () => brush.strokeSpacingFactor);
            }
            else if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.distribution), StringComparison.Ordinal))
            {
                var dist = _root.Q<EnumField>("Distribution");
                if (dist != null)
                {
                    dist.SetValueWithoutNotify(brush.distribution);
                }
            }
            else if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.mixExtraProfiles), StringComparison.Ordinal))
            {
                var mixExtra = _root.Q<Toggle>("MixExtraProfiles");
                if (mixExtra != null)
                {
                    mixExtra.SetValueWithoutNotify(brush.mixExtraProfiles);
                }
            }
            else if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.useAbsoluteStrokeSpacing), StringComparison.Ordinal))
            {
                var t = _root.Q<Toggle>("UseAbsoluteStrokeSpacing");
                if (t != null) t.SetValueWithoutNotify(brush.useAbsoluteStrokeSpacing);
            }
            else if (string.Equals(propertyName, nameof(MrTerrainPainter.Editor.Services.BrushSettings.strokeSpacingAbsolute), StringComparison.Ordinal))
            {
                UpdateSlider("StrokeSpacingAbs", 0f, 200f, () => brush.strokeSpacingAbsolute);
            }
            RequestSceneRepaint();
        }

        private void OnBrushReplaced()
        {
            var brush = MrTerrainPainter.Editor.Tools.MTPBrushContext.Brush;
            UnsubscribeBrushChanges();
            SubscribeBrushChanges(brush);
            // 重建滑条绑定，确保引用最新实例
            SetupSliders(brush);
            SetupAdditionalSettings(brush);
            // 刷新当前滑条显示
            UpdateSlider("Size", 0.5f, 50f, () => brush.size);
            UpdateSlider("Strength", 0.1f, 10f, () => brush.strength);
            UpdateSlider("Density", 0f, 5f, () => brush.densityScale);
            UpdateSlider("Hardness", 0f, 1f, () => brush.hardness);
            UpdateSlider("StrokeSpacing", 0f, 1f, () => brush.strokeSpacingFactor);
            UpdateSlider("StrokeSpacingAbs", 0f, 200f, () => brush.strokeSpacingAbsolute);
            RequestSceneRepaint();
        }

        private List<VegetationProfile> LoadProfiles()
        {
            var list = new List<VegetationProfile>();
            var guids = AssetDatabase.FindAssets("t:VegetationProfile");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var vp = AssetDatabase.LoadAssetAtPath<VegetationProfile>(path);
                if (vp != null) list.Add(vp);
            }
            return list;
        }

        private List<VegetationProfile> GetProfilesFromWindowOrProject()
        {
            if (MrTerrainPainterWindow.TryGet(out var win) && win != null)
            {
                return win.GetAvailableProfilesSnapshotPublic();
            }
            return LoadProfiles();
        }

        private void OnActiveToolChanged() => UpdateVisibility();
        private void OnSelectionChanged() => UpdateVisibility();

        private void UpdateVisibility()
        {
            if (_updateQueued) return;
            _updateQueued = true;

            EditorApplication.delayCall += () =>
            {
                _updateQueued = false;
                // 确保 Overlay 没有被销毁
                if (this == null) return;
                double now = EditorApplication.timeSinceStartup;
                if (now - _lastUpdateTime < 0.016) return;
                _lastUpdateTime = now;

                bool isActive = ToolManager.activeToolType == typeof(MTPBrushTool);
                bool hasTerrain = HasTerrainSelection();

                // Overlay.displayed 属性是安全的，不需要 try-catch
                displayed = isActive && hasTerrain;
                UpdatePanelFeatureVisibility();
            };
        }

        private bool HasTerrainSelection()
        {
            var go = Selection.activeGameObject;
            return go != null && go.GetComponent<Terrain>() != null;
        }

        private void OnWindowStateChanged(bool open, bool settings, bool painting)
        {
            RefreshProfileList();
            UpdatePanelFeatureVisibility();
            RequestSceneRepaint();
            UpdateVisibility();
        }

        private void UpdatePanelFeatureVisibility()
        {
            bool windowOpen = MrTerrainPainterWindow.TryGet(out var win) && win != null;
            if (!windowOpen)
            {
                ShowAllOverlayControls();
                return;
            }
            bool settings = win.IsSettingsOpenPublic();
            bool painting = win.IsPaintingModePublic();
            if (painting)
            {
                HideControls("Profiles", "Size", "Strength", "Density", "Hardness", "Distribution", "MixExtraProfiles");
                ShowControls("NormalDirection");
            }
            else if (settings)
            {
                HideControls("NormalDirection");
                ShowControls("Profiles", "Size", "Strength", "Density", "Hardness", "Distribution", "MixExtraProfiles");
            }
            else
            {
                ShowAllOverlayControls();
            }
        }

        private void HideControls(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                var ve = _root.Q<VisualElement>(names[i]);
                if (ve != null) ve.style.display = DisplayStyle.None;
            }
        }

        private void ShowControls(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                var ve = _root.Q<VisualElement>(names[i]);
                if (ve != null) ve.style.display = DisplayStyle.Flex;
            }
        }

        private void ShowAllOverlayControls()
        {
            ShowControls("Profiles", "Size", "Strength", "Density", "Hardness", "Distribution", "MixExtraProfiles", "NormalDirection");
        }

        #endregion
    }
}

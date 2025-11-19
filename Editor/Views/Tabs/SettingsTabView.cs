using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Tools;

namespace MrTerrainPainter.Editor.Views.Tabs
{
    public class SettingsTabView
    {
        private readonly MrTerrainPainterWindow _window;
        private readonly MrTerrainPainterConfig _directConfig;
        private readonly VisualElement _root;

        private MrTerrainPainterConfig Config => _window != null ? _window.config : _directConfig;

        public SettingsTabView(MrTerrainPainterWindow window, VisualElement root)
        {
            _window = window;
            _root = root;
        }

        // 支持仅传递 Config 的构造函数（用于独立配置窗口）
        public SettingsTabView(MrTerrainPainterConfig config, VisualElement root)
        {
            _directConfig = config;
            _root = root;
        }

        public void Setup()
        {
            if (Config == null) return;
            SetupBasicSettings();
            SetupMappingList();
            SetupActionButtons();
        }

        private void SetupBasicSettings()
        {
            // 使用 Helper 绑定基础属性
            BindField<TextField, string>("RecipeGenerationPath",
                () => Config.recipeGenerationPath,
                v => Config.recipeGenerationPath = v);

            BindField<Toggle, bool>("ShowPool",
                () => VegetationPool.ShowInHierarchy,
                v => { VegetationPool.ShowInHierarchy = v; Config.showPoolInHierarchy = v; VegetationPool.ApplyShowInHierarchyAll(); });

            // 特殊处理 NormalDirection
            var normalToggle = _root.Q<Toggle>("NormalDirection");
            if (normalToggle != null)
            {
                normalToggle.SetValueWithoutNotify(Config.normalDirection);
                normalToggle.RegisterValueChangedCallback(e => ConfigTools.SetNormalDirection(Config, e.newValue));
                ConfigTools.NormalDirectionChanged += v => normalToggle.SetValueWithoutNotify(v);
            }

            // 资源引用
            BindObject<VisualTreeAsset>("VegetationSharedUXML", () => Config.vegetationSharedUxml, v => Config.vegetationSharedUxml = v);
            BindObject<VisualTreeAsset>("BrushOverlayUXML", () => Config.brushOverlayUxml, v => Config.brushOverlayUxml = v);
            BindObject<StyleSheet>("StylesUSS", () => Config.stylesUss, v => Config.stylesUss = v);
        }

        private void SetupMappingList()
        {
            var foldout = _root.Q<Foldout>("MappingList");
            var template = ConfigTools.GetSettingsMappingUxml();
            if (foldout == null || template == null) return;

            Config.mappingEntries ??= new List<MrTerrainPainterConfig.MappingEntry>();

            // Status Box
            var statusBox = foldout.Q<HelpBox>("MappingStatusBox") ?? new HelpBox("", HelpBoxMessageType.None) { name = "MappingStatusBox" };
            if (!foldout.Contains(statusBox)) foldout.Insert(0, statusBox);

            void Refresh()
            {
                // 保留 StatusBox，清除其他
                var box = foldout.Q("MappingStatusBox");
                foldout.Clear();
                if (box != null) foldout.Add(box);

                for (int i = 0; i < Config.mappingEntries.Count; i++)
                    CreateMappingRow(foldout, template, i, Refresh);

                UpdateStatus(statusBox);
            }

            _root.Q<Button>("Add")?.SetClickHandler(() =>
            {
                Config.mappingEntries.Add(new MrTerrainPainterConfig.MappingEntry { type = Config.defaultGenerationType });
                Save();
                Refresh();
            });

            Refresh();
        }

        private void CreateMappingRow(VisualElement parent, VisualTreeAsset template, int index, Action onRefresh)
        {
            var row = template.Instantiate();
            var entry = Config.mappingEntries[index];

            // Object Field
            var objField = row.Q<ObjectField>("ObjectField");
            objField.objectType = typeof(Transform);
            objField.SetValueWithoutNotify(entry.node);
            objField.RegisterValueChangedCallback(e =>
            {
                // 实时获取引用以防闭包过期
                if (index < Config.mappingEntries.Count)
                {
                    Config.mappingEntries[index].node = e.newValue as Transform;
                    Save();
                }
            });

            // Enum Field
            var enumField = row.Q<EnumField>("PrefabType");
            enumField.Init(entry.type);
            enumField.RegisterValueChangedCallback(e =>
            {
                if (index < Config.mappingEntries.Count)
                {
                    Config.mappingEntries[index].type = (Runtime.Profiles.PrefabType)e.newValue;
                    Save();
                }
            });

            // Delete
            row.Q<Button>("Delete")?.SetClickHandler(() =>
            {
                Config.mappingEntries.RemoveAt(index);
                Save();
                onRefresh();
            });

            parent.Add(row);
        }

        private void UpdateStatus(HelpBox box)
        {
            int unbound = Config.mappingEntries.Count(e => e == null || e.node == null);
            bool hasPlant = Config.mappingEntries.Any(e => e?.type == Runtime.Profiles.PrefabType.Plant && e.node != null);

            if (unbound == 0 && hasPlant)
            {
                box.messageType = HelpBoxMessageType.Info;
                box.text = "Mapping 已完成";
            }
            else
            {
                box.messageType = HelpBoxMessageType.Warning;
                box.text = $"未绑定: {unbound} | 需包含 Plant 类型节点";
            }
        }

        private void SetupActionButtons()
        {
            void HandleSave()
            {
                ConfigTools.Save(Config);
                MTPBrushContext.SetConfig(Config);
                if (ConfigTools.IsComplete(Config, out var reason))
                {
                    _window?.OnConfigurationCompleted();
                    _window?.CelebrateMappingCompleted();
                    EditorUtility.DisplayDialog("成功", "配置已保存", "确定");
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", reason, "确定");
                }
            }

            _root.Q<Button>("SaveConfiguration")?.SetClickHandler(HandleSave);
            _root.Q<Button>("Confirm")?.SetClickHandler(HandleSave);
            _root.Q<Button>("BindDefaultResources")?.SetClickHandler(BindDefaults);
        }

        private void BindDefaults()
        {
            // 仅填充空缺的默认资源
            if (Config.brushOverlayUxml == null) Config.brushOverlayUxml = ConfigTools.GetBrushOverlayUxml(Config);
            if (Config.stylesUss == null) Config.stylesUss = ConfigTools.GetStylesUss(Config);
            // ... 其他资源绑定逻辑保持一致
            Save();
            SetupBasicSettings(); // 刷新 UI
        }

        // --- Generic Helpers ---
        private void BindField<TElement, TValue>(string name, Func<TValue> getter, Action<TValue> setter) where TElement : VisualElement, INotifyValueChanged<TValue>
        {
            var el = _root.Q<TElement>(name);
            if (el == null) return;
            el.SetValueWithoutNotify(getter());
            el.RegisterValueChangedCallback(e => { setter(e.newValue); Save(); });
        }

        private void BindObject<T>(string name, Func<UnityEngine.Object> getter, Action<T> setter) where T : UnityEngine.Object
        {
            var el = _root.Q<ObjectField>(name);
            if (el == null) return;
            el.objectType = typeof(T);
            el.SetValueWithoutNotify(getter());
            el.RegisterValueChangedCallback(e => { setter(e.newValue as T); Save(); });
        }

        private void Save()
        {
            if (Config == null) return;
            EditorUtility.SetDirty(Config);
        }
    }
}

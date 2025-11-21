using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Tools; // 引用 MTPBrushContext

namespace MrTerrainPainter.Editor.Views.Tabs
{
    public class SettingsTabView
    {
        private readonly MrTerrainPainterWindow _window;
        private readonly MrTerrainPainterConfig _directConfig;
        private readonly VisualElement _root;

        // 优先使用窗口中的配置，如果是独立窗口则使用直接传入的配置
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
                v =>
                {
                    VegetationPool.ShowInHierarchy = v;
                    Config.showPoolInHierarchy = v;
                    VegetationPool.ApplyShowInHierarchyAll();
                });

            // 特殊处理 NormalDirection
            var normalToggle = _root.Q<Toggle>("NormalDirection");
            if (normalToggle != null)
            {
                normalToggle.SetValueWithoutNotify(Config.normalDirection);
                normalToggle.RegisterValueChangedCallback(e => ConfigTools.SetNormalDirection(Config, e.newValue));
                // 监听外部变化（如 Overlay 改变）同步 UI
                ConfigTools.NormalDirectionChanged += v => normalToggle.SetValueWithoutNotify(v);
            }

            // 资源引用绑定
            BindObject<VisualTreeAsset>("VegetationSharedUXML", () => Config.vegetationSharedUxml, v => Config.vegetationSharedUxml = v);
            BindObject<VisualTreeAsset>("BrushOverlayUXML", () => Config.brushOverlayUxml, v => Config.brushOverlayUxml = v);
            BindObject<StyleSheet>("StylesUSS", () => Config.stylesUss, v => Config.stylesUss = v);

            // 其他资源绑定 (可按需添加更多 BindObject)
            BindObject<VisualTreeAsset>("StartUXML", () => Config.startUxml, v => Config.startUxml = v);
            BindObject<VisualTreeAsset>("ControlUXML", () => Config.controlUxml, v => Config.controlUxml = v);
            BindObject<VisualTreeAsset>("PaintUXML", () => Config.paintUxml, v => Config.paintUxml = v);
            BindObject<VisualTreeAsset>("GenerateUXML", () => Config.generateUxml, v => Config.generateUxml = v);
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
            if (objField != null)
            {
                objField.objectType = typeof(Transform);
                objField.allowSceneObjects = true; // 允许拖拽场景中的根节点对象
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
            }

            // Enum Field
            var enumField = row.Q<EnumField>("PrefabType");
            if (enumField != null)
            {
                enumField.Init(entry.type);
                enumField.RegisterValueChangedCallback(e =>
                {
                    if (index < Config.mappingEntries.Count)
                    {
                        Config.mappingEntries[index].type = (Runtime.Profiles.PrefabType)e.newValue;
                        Save();
                    }
                });
            }

            // Delete
            row.Q<Button>("Delete")?.SetClickHandler(() =>
            {
                if (index < Config.mappingEntries.Count)
                {
                    Config.mappingEntries.RemoveAt(index);
                    Save();
                    onRefresh();
                }
            });

            parent.Add(row);
        }

        private void UpdateStatus(HelpBox box)
        {
            if (box == null) return;
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

                // 【关键优化】1. 立即更新画笔上下文
                MTPBrushContext.SetConfig(Config);

                if (ConfigTools.IsComplete(Config, out var reason))
                {
                    // 2. 通知宿主窗口(如果存在)更新UI状态
                    _window?.OnConfigurationCompleted();
                    _window?.CelebrateMappingCompleted();

                    // 3. 【关键优化】广播全局事件，通知所有监听者(Overlay等)刷新
                    ConfigTools.NotifyConfigUpdated();

                    EditorUtility.DisplayDialog("成功", "配置已保存并同步。", "确定");
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", reason, "确定");
                }
            }

            // _root.Q<Button>("SaveConfiguration")?.SetClickHandler(HandleSave);
            _root.Q<Button>("Confirm")?.SetClickHandler(HandleSave);
            _root.Q<Button>("BindDefaultResources")?.SetClickHandler(BindDefaults);
        }

        private void BindDefaults()
        {
            // 仅填充空缺的默认资源
            if (Config.brushOverlayUxml == null) Config.brushOverlayUxml = ConfigTools.GetBrushOverlayUxml(Config);
            if (Config.stylesUss == null) Config.stylesUss = ConfigTools.GetStylesUss(Config);

            if (Config.startUxml == null) Config.startUxml = ConfigTools.GetStartUxml(Config);
            if (Config.controlUxml == null) Config.controlUxml = ConfigTools.GetControlUxml(Config);
            if (Config.paintUxml == null) Config.paintUxml = ConfigTools.GetPaintUxml(Config);
            if (Config.generateUxml == null) Config.generateUxml = ConfigTools.GetGenerateUxml(Config);
            if (Config.vegetationSharedUxml == null) Config.vegetationSharedUxml = ConfigTools.GetVegetationSharedUxml(Config);
            if (Config.vegetationProfileRowUxml == null) Config.vegetationProfileRowUxml = ConfigTools.GetVegetationProfileRowUxml(Config);
            if (Config.prefabIconUxml == null) Config.prefabIconUxml = ConfigTools.GetPrefabIconUxml(Config);
            if (Config.draggableAreaUxml == null) Config.draggableAreaUxml = ConfigTools.GetDraggableAreaUxml(Config);

            Save();

            // 【关键优化】同步更新
            MTPBrushContext.SetConfig(Config);
            ConfigTools.NotifyConfigUpdated();

            SetupBasicSettings(); // 刷新 UI 显示
            EditorUtility.DisplayDialog("成功", "已绑定默认资源。", "确定");
        }

        // --- Generic Helpers ---

        private void BindField<TElement, TValue>(string name, Func<TValue> getter, Action<TValue> setter)
            where TElement : VisualElement, INotifyValueChanged<TValue>
        {
            var el = _root.Q<TElement>(name);
            if (el == null) return;
            el.SetValueWithoutNotify(getter());
            // 注意：普通字段修改不立即触发全局刷新，只有点击保存时触发
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

        private void Save() => EditorUtility.SetDirty(Config);
    }

    // 如果没有引用 UIExtensions, 确保有 SetClickHandler
    internal static class SettingsExtensions
    {
        public static void SetClickHandler(this Button b, Action a) { if (b != null) b.clicked += a; }
    }
}
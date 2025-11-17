using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using MrTerrainPainter.Editor.Config;

namespace MrTerrainPainter.Editor.Views.Tabs
{
    public class SettingsTabView
    {
        private readonly MrTerrainPainterWindow _window;
        private readonly MrTerrainPainterConfig _directConfig;
        private readonly VisualElement _root;

        // 回调缓存
        private readonly Action _onCompleted;
        private readonly Action _onCelebrate;

        // UI 缓存
        private Foldout _mappingFoldout;
        private VisualTreeAsset _mappingTemplate;

        // 属性访问器：优先使用 Window 中的 Config，否则使用直接传入的 Config
        private MrTerrainPainterConfig Config => _window != null ? _window.config : _directConfig;

        #region Constructors

        public SettingsTabView(MrTerrainPainterWindow window, VisualElement root)
        {
            _window = window;
            _root = root;
            _directConfig = null;
            _onCompleted = window.OnConfigurationCompleted;
            _onCelebrate = window.CelebrateMappingCompleted;
        }

        public SettingsTabView(MrTerrainPainterConfig config, VisualElement root)
        {
            _window = null;
            _directConfig = config;
            _root = root;
            _onCompleted = null;
            _onCelebrate = null;
        }

        #endregion

        public void Setup()
        {
            if (Config == null) return;

            SetupBasicSettings();
            SetupMappingList();
            SetupActionButtons();
        }

        #region 1. Basic Settings (基础设置)

        private void SetupBasicSettings()
        {
            // 路径与开关
            BindTextField(_root, "RecipeGenerationPath",
                () => Config.recipeGenerationPath,
                v => { Config.recipeGenerationPath = v; SaveConfig(); });

            BindToggle(_root, "ShowPool",
                () => VegetationPool.ShowInHierarchy,
                v => { VegetationPool.ShowInHierarchy = v; Config.showPoolInHierarchy = v; SaveConfig(); VegetationPool.ApplyShowInHierarchyAll(); });

            // 法线方向 Toggle (含外部事件监听)
            var normalToggle = _root.Q<Toggle>("NormalDirection");
            if (normalToggle != null)
            {
                normalToggle.SetValueWithoutNotify(Config.normalDirection);
                normalToggle.RegisterValueChangedCallback(e => ConfigTools.SetNormalDirection(Config, e.newValue));

                // 监听外部变化以同步 UI
                ConfigTools.NormalDirectionChanged += v => normalToggle.SetValueWithoutNotify(v);
            }

            // 资源引用绑定
            BindObjectField(_root, "VegetationSharedUXML", typeof(VisualTreeAsset),
                () => Config.vegetationSharedUxml,
                v => { Config.vegetationSharedUxml = v as VisualTreeAsset; SaveConfig(); });

            BindObjectField(_root, "BrushOverlayUXML", typeof(VisualTreeAsset),
                () => Config.brushOverlayUxml,
                v => { Config.brushOverlayUxml = v as VisualTreeAsset; SaveConfig(); });

            BindObjectField(_root, "StylesUSS", typeof(StyleSheet),
                () => Config.stylesUss,
                v => { Config.stylesUss = v as StyleSheet; SaveConfig(); });
        }

        #endregion

        #region 2. Mapping List Logic (映射列表逻辑)

        private void SetupMappingList()
        {
            _mappingFoldout = _root.Q<Foldout>("MappingList");
            _mappingTemplate = ConfigTools.GetSettingsMappingUxml();

            if (_mappingFoldout == null || _mappingTemplate == null) return;

            // 确保列表初始化
            if (Config.mappingEntries == null)
                Config.mappingEntries = new List<MrTerrainPainterConfig.MappingEntry>();

            // 初始刷新
            RefreshMappingList();

            // 绑定添加按钮
            var btnAdd = _root.Q<Button>("Add");
            if (btnAdd != null)
            {
                btnAdd.clicked += () =>
                {
                    var entry = new MrTerrainPainterConfig.MappingEntry
                    {
                        type = Config.defaultGenerationType
                    };
                    Config.mappingEntries.Add(entry);
                    SaveConfig();
                    RefreshMappingList();
                };
            }
        }

        private void RefreshMappingList()
        {
            if (_mappingFoldout == null) return;

            _mappingFoldout.Clear();
            int count = Config.mappingEntries?.Count ?? 0;

            for (int i = 0; i < count; i++)
            {
                CreateMappingRow(i);
            }
        }

        private void CreateMappingRow(int index)
        {
            var rowRoot = _mappingTemplate.Instantiate();
            var mapRoot = rowRoot.Q<VisualElement>("Mapping");

            // 1. 绑定 Transform 字段
            var objectField = mapRoot.Q<ObjectField>("ObjectField");
            if (objectField != null)
            {
                objectField.objectType = typeof(Transform);
                objectField.allowSceneObjects = true;
                objectField.SetValueWithoutNotify(Config.mappingEntries[index].node);
                objectField.RegisterValueChangedCallback(e =>
                {
                    // 注意：这里需要重新获取 index，防止列表变动导致的引用错误，但在全量刷新模式下直接用 index 暂无问题
                    if (index < Config.mappingEntries.Count)
                    {
                        Config.mappingEntries[index].node = e.newValue as Transform;
                        SaveConfig();
                        // 这里不刷新整个列表，避免输入焦点丢失
                    }
                });
            }

            // 2. 绑定 Enum 字段
            var typeField = mapRoot.Q<EnumField>("PrefabType");
            if (typeField != null)
            {
                var initialType = Config.mappingEntries[index].type;
                typeField.Init(initialType);
                typeField.SetValueWithoutNotify(initialType);
                typeField.RegisterValueChangedCallback(e =>
                {
                    if (index < Config.mappingEntries.Count)
                    {
                        Config.mappingEntries[index].type = (Runtime.Profiles.PrefabType)e.newValue;
                        SaveConfig();
                    }
                });
            }

            // 3. 绑定删除按钮
            var btnDel = rowRoot.Q<Button>("Delete");
            if (btnDel != null)
            {
                btnDel.clicked += () =>
                {
                    if (Config.mappingEntries != null && index < Config.mappingEntries.Count)
                    {
                        Config.mappingEntries.RemoveAt(index);
                        SaveConfig();
                        RefreshMappingList(); // 删除必须刷新列表
                    }
                };
            }

            _mappingFoldout.Add(rowRoot);
        }

        #endregion

        #region 3. Action Buttons (操作按钮)

        private void SetupActionButtons()
        {
            // Save / Confirm 按钮
            var btnSave = _root.Q<Button>("SaveConfiguration") ?? _root.Q<Button>("Save");
            var btnConfirm = _root.Q<Button>("Confirm");

            Action confirmHandler = HandleConfirmAndSave;

            if (btnSave != null) btnSave.clicked += confirmHandler;
            if (btnConfirm != null) btnConfirm.clicked += confirmHandler;

            // Check 按钮
            BindButtonAction("CheckConfiguration", () =>
            {
                if (ConfigTools.IsComplete(Config, out var reason))
                    EditorUtility.DisplayDialog("检查结果", "配置完整。", "确定");
                else
                    EditorUtility.DisplayDialog("检查结果", reason, "确定");
            });

            // Fix 按钮
            BindButtonAction("FixMissingResources", () =>
            {
                if (!string.IsNullOrEmpty(Config.recipeGenerationPath))
                    ConfigTools.EnsureFolder(Config.recipeGenerationPath);
                EditorUtility.DisplayDialog("已修复", "已尝试修复部分缺失资源路径。", "确定");
            });

            // Bind Defaults 按钮
            BindButtonAction("BindDefaultResources", BindDefaultResources);
        }

        private void HandleConfirmAndSave()
        {
            ConfigTools.Save(Config);
            if (ConfigTools.IsComplete(Config, out var reason))
            {
                _onCompleted?.Invoke();
                _onCelebrate?.Invoke();
                EditorUtility.DisplayDialog("已保存", "配置完整，已保存。", "确定");

                // 如果是单独配置模式（无窗口引用），尝试打开窗口
                if (_window == null)
                {
                    var win = MrTerrainPainterWindow.GetOrOpen();
                    EditorApplication.delayCall += () => win?.OpenPaintingSettings();
                }
            }
            else
            {
                EditorUtility.DisplayDialog("提示", reason, "确定");
                FocusFirstIncompleteMapping(_root);
            }
        }

        private void BindDefaultResources()
        {
            // 仅当为空时才赋值
            if (Config.brushOverlayUxml == null) Config.brushOverlayUxml = ConfigTools.GetBrushOverlayUxml(Config);
            if (Config.stylesUss == null) Config.stylesUss = ConfigTools.GetStylesUss(Config);

            Config.startUxml ??= ConfigTools.GetStartUxml(Config);
            Config.controlUxml ??= ConfigTools.GetControlUxml(Config);
            Config.paintUxml ??= ConfigTools.GetPaintUxml(Config);
            Config.generateUxml ??= ConfigTools.GetGenerateUxml(Config);
            Config.vegetationSharedUxml ??= ConfigTools.GetVegetationSharedUxml(Config);
            Config.vegetationProfileRowUxml ??= ConfigTools.GetVegetationProfileRowUxml(Config);
            Config.prefabIconUxml ??= ConfigTools.GetPrefabIconUxml(Config);
            Config.draggableAreaUxml ??= ConfigTools.GetDraggableAreaUxml(Config);

            SaveConfig();
            EditorUtility.DisplayDialog("已绑定", "已绑定可用的默认资源引用。", "确定");

            // 刷新界面显示
            SetupBasicSettings();
        }

        #endregion

        #region Helpers (辅助方法)

        private void SaveConfig()
        {
            EditorUtility.SetDirty(Config);
            // 如果需要频繁保存 Asset，可以在这里调用 AssetDatabase.SaveAssets(); 
            // 但通常 SetDirty 就足够编辑器行为了，ConfigTools.Save 可能会做更多事
        }

        private void BindButtonAction(string buttonName, Action action)
        {
            var btn = _root.Q<Button>(buttonName);
            if (btn != null) btn.clicked += action;
        }

        private void BindTextField(VisualElement root, string name, Func<string> getter, Action<string> setter)
        {
            var tf = root.Q<TextField>(name);
            if (tf == null) return;
            tf.SetValueWithoutNotify(getter());
            tf.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        private void BindToggle(VisualElement root, string name, Func<bool> getter, Action<bool> setter)
        {
            var t = root.Q<Toggle>(name);
            if (t == null) return;
            t.SetValueWithoutNotify(getter());
            t.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        private void BindObjectField(VisualElement root, string name, Type type, Func<UnityEngine.Object> getter, Action<UnityEngine.Object> setter)
        {
            var of = root.Q<ObjectField>(name);
            if (of == null) return;
            of.objectType = type;
            of.allowSceneObjects = false;
            of.SetValueWithoutNotify(getter());
            of.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        private void FocusFirstIncompleteMapping(VisualElement page)
        {
            var fold = page.Q<Foldout>("MappingList");
            if (fold == null) return;
            fold.value = true; // 展开列表

            // 1. 查找空节点
            int idx = -1;
            if (Config.mappingEntries != null)
            {
                for (int i = 0; i < Config.mappingEntries.Count; i++)
                {
                    var e = Config.mappingEntries[i];
                    if (e == null || e.node == null) { idx = i; break; }
                }
            }

            if (idx >= 0)
            {
                var row = fold.childCount > idx ? fold.ElementAt(idx) : null;
                var of = row?.Q<ObjectField>("ObjectField");
                if (of != null) of.Focus();
                else row?.Focus();
                return;
            }

            // 2. 查找是否缺少 Plant 类型
            bool hasPlantBound = Config.mappingEntries != null &&
                Config.mappingEntries.Any(e => e != null && e.type == Runtime.Profiles.PrefabType.Plant && e.node != null);

            if (!hasPlantBound)
            {
                // 如果没有任何行，聚焦添加按钮
                if (fold.childCount == 0)
                {
                    page.Q<Button>("Add")?.Focus();
                }
                else
                {
                    // 否则聚焦第一行的类型选择器（提示用户修改）
                    var firstRow = fold.ElementAt(0);
                    firstRow?.Q<EnumField>("PrefabType")?.Focus();
                }
            }
        }

        #endregion
    }
}

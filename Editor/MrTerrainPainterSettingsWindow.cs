using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor
{
    public class MrTerrainPainterSettingsWindow : EditorWindow
    {
        private VisualTreeAsset settingsUxml;
        private MrTerrainPainter.Editor.Config.MrTerrainPainterConfig config;

        private readonly List<Mapping> mappings = new List<Mapping>();

        private class Mapping
        {
            public Transform node;
            public Runtime.Profiles.PrefabType type = Runtime.Profiles.PrefabType.Prop;
        }

        public static void Open()
        {
            var win = GetWindow<MrTerrainPainterSettingsWindow>(true, "Mr Terrain Painter Settings");
            win.minSize = new Vector2(420, 260);
            win.Show();
        }

        private void OnEnable()
        {
            settingsUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/MrTerrPainterV1/Editor/MrTerrainPainterSettings.uxml");
            config = ConfigTools.LoadOrCreateAsset();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            VisualElement root = settingsUxml != null ? settingsUxml.Instantiate() : new VisualElement();
            rootVisualElement.Add(root);

            // 绑定路径与对象池显示
            var tfRecipePath = root.Q<TextField>("RecipeGenerationPath");
            if (tfRecipePath != null)
            {
                tfRecipePath.SetValueWithoutNotify(config.recipeGenerationPath);
                tfRecipePath.RegisterValueChangedCallback(e => { config.recipeGenerationPath = e.newValue; EditorUtility.SetDirty(config); });
            }
            var toggleShowPool = root.Q<Toggle>("ShowPool");
            if (toggleShowPool != null)
            {
                toggleShowPool.SetValueWithoutNotify(VegetationPool.ShowInHierarchy);
                toggleShowPool.RegisterValueChangedCallback(e =>
                {
                    VegetationPool.ShowInHierarchy = e.newValue;
                    config.showPoolInHierarchy = e.newValue;
                    foreach (var t in Terrain.activeTerrains)
                    {
                        var r = t.transform.Find($"VegetationPool_{t.name}");
                        if (r != null)
                        {
                            r.gameObject.hideFlags = VegetationPool.ShowInHierarchy ? HideFlags.None : HideFlags.HideInHierarchy;
                            EditorUtility.SetDirty(r.gameObject);
                        }
                    }
                    EditorUtility.SetDirty(config);
                });
            }

            // —— 严格按 UXML 布局构建 Mapping 列表 ——
            var fold = root.Q<Foldout>("MappingList");
            if (fold == null) return; // 提前返回：UXML 未提供 MappingList

            // 使用UXML中已有的第一行作为初始行，并在Add时克隆该行
            var templateRow = fold.Q<VisualElement>("Mapping");
            if (templateRow == null) return; // 提前返回：缺少模板行

            var btnAdd = root.Q<Button>("Add");
            var btnDelete = root.Q<Button>("Delete");

            // 绑定一行控件到数据
            void BindRow(VisualElement row, Mapping map, int index)
            {
                var of = row.Q<ObjectField>("ObjectField");
                if (of != null)
                {
                    of.objectType = typeof(Transform);
                    of.allowSceneObjects = true;
                    // 初始值：从配置加载
                    var initialGo = (config.objectList != null && index < config.objectList.Length) ? config.objectList[index] : null;
                    var initialTf = initialGo != null ? initialGo.transform : null;
                    of.SetValueWithoutNotify(initialTf);
                    of.RegisterValueChangedCallback(e =>
                    {
                        map.node = e.newValue as Transform;
                        // 同步到配置
                        var list = config.objectList?.ToList() ?? new List<GameObject>();
                        while (index >= list.Count) list.Add(null);
                        list[index] = map.node != null ? map.node.gameObject : null;
                        config.objectList = list.ToArray();
                        EditorUtility.SetDirty(config);
                    });
                }
                var typeField = row.Q<EnumField>("PrefabType");
                if (typeField != null)
                {
                    // 初始值：从配置加载
                    var initialType = (config.objectTypeList != null && index < config.objectTypeList.Length)
                        ? config.objectTypeList[index]
                        : config.defaultGenerationType;
                    typeField.Init(initialType);
                    typeField.SetValueWithoutNotify(initialType);
                    typeField.RegisterValueChangedCallback(e =>
                    {
                        map.type = (Runtime.Profiles.PrefabType)e.newValue;
                        // 同步到配置
                        var types = config.objectTypeList?.ToList() ?? new List<Runtime.Profiles.PrefabType>();
                        while (index >= types.Count) types.Add(config.defaultGenerationType);
                        types[index] = map.type;
                        config.objectTypeList = types.ToArray();
                        EditorUtility.SetDirty(config);
                    });
                }
            }

            // 初始化：读取配置并初始化所有行
            int existingCount = Mathf.Max(config.objectList != null ? config.objectList.Length : 0,
                                          config.objectTypeList != null ? config.objectTypeList.Length : 0);
            if (existingCount <= 0)
            {
                existingCount = 1; // 至少一个
            }
            for (int i = 0; i < existingCount; i++)
            {
                var map = new Mapping();
                // 从配置填充映射
                if (config.objectList != null && i < config.objectList.Length)
                {
                    var go = config.objectList[i];
                    map.node = go != null ? go.transform : null;
                }
                if (config.objectTypeList != null && i < config.objectTypeList.Length)
                {
                    map.type = config.objectTypeList[i];
                }
                mappings.Add(map);
                if (i == 0)
                {
                    BindRow(templateRow, map, i);
                }
                else
                {
                    // 通过重新实例化 UXML 并提取名为 "Mapping" 的元素，作为新行
                    var tree = settingsUxml != null ? settingsUxml.Instantiate() : null;
                    var row = tree != null ? tree.Q<VisualElement>("Mapping") : null;
                    if (row == null) continue; // 提前返回：无法获取到模板行
                    row.RemoveFromHierarchy(); // 从临时树中分离
                    BindRow(row, map, i);
                    fold.Add(row);
                }
            }

            if (btnAdd != null)
            {
                btnAdd.clicked += () =>
                {
                    var map = new Mapping();
                    // 默认值取配置默认类型
                    map.type = config.defaultGenerationType;
                    mappings.Add(map);

                    // 通过重新实例化 UXML 并提取名为 "Mapping" 的元素，作为新行
                    var tree = settingsUxml != null ? settingsUxml.Instantiate() : null;
                    var row = tree != null ? tree.Q<VisualElement>("Mapping") : null;
                    if (row == null) return; // 提前返回：无法获取到模板行
                    row.RemoveFromHierarchy();
                    BindRow(row, map, mappings.Count - 1);
                    fold.Add(row); // 直接追加到Foldout中

                    // 扩展配置数组（保持索引一致）
                    var list = config.objectList?.ToList() ?? new List<GameObject>();
                    list.Add(null);
                    config.objectList = list.ToArray();
                    var types = config.objectTypeList?.ToList() ?? new List<Runtime.Profiles.PrefabType>();
                    types.Add(config.defaultGenerationType);
                    config.objectTypeList = types.ToArray();
                    EditorUtility.SetDirty(config);
                };
            }

            if (btnDelete != null)
            {
                btnDelete.clicked += () =>
                {
                    // 仅删除最后一行（包含初始行）
                    var rows = fold.Query<VisualElement>("Mapping").ToList();
                    if (rows.Count == 0) return; // 提前返回
                    var lastRow = rows[rows.Count - 1];
                    fold.Remove(lastRow);
                    if (mappings.Count > 0) mappings.RemoveAt(mappings.Count - 1);

                    // 收缩配置数组
                    if (config.objectList != null && config.objectList.Length > 0)
                    {
                        config.objectList = config.objectList.Take(config.objectList.Length - 1).ToArray();
                    }
                    if (config.objectTypeList != null && config.objectTypeList.Length > 0)
                    {
                        config.objectTypeList = config.objectTypeList.Take(config.objectTypeList.Length - 1).ToArray();
                    }
                    EditorUtility.SetDirty(config);
                };
            }

            var ofStart = root.Q<ObjectField>("StartUXML");
            if (ofStart != null)
            {
                ofStart.objectType = typeof(VisualTreeAsset);
                ofStart.allowSceneObjects = false;
                ofStart.SetValueWithoutNotify(config.startUxml);
                ofStart.RegisterValueChangedCallback(e => { config.startUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofCtrl = root.Q<ObjectField>("ControlUXML");
            if (ofCtrl != null)
            {
                ofCtrl.objectType = typeof(VisualTreeAsset);
                ofCtrl.allowSceneObjects = false;
                ofCtrl.SetValueWithoutNotify(config.controlUxml);
                ofCtrl.RegisterValueChangedCallback(e => { config.controlUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofPaint = root.Q<ObjectField>("PaintUXML");
            if (ofPaint != null)
            {
                ofPaint.objectType = typeof(VisualTreeAsset);
                ofPaint.allowSceneObjects = false;
                ofPaint.SetValueWithoutNotify(config.paintUxml);
                ofPaint.RegisterValueChangedCallback(e => { config.paintUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofGen = root.Q<ObjectField>("GenerateUXML");
            if (ofGen != null)
            {
                ofGen.objectType = typeof(VisualTreeAsset);
                ofGen.allowSceneObjects = false;
                ofGen.SetValueWithoutNotify(config.generateUxml);
                ofGen.RegisterValueChangedCallback(e => { config.generateUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofRow = root.Q<ObjectField>("VegetationProfileUXML");
            if (ofRow != null)
            {
                ofRow.objectType = typeof(VisualTreeAsset);
                ofRow.allowSceneObjects = false;
                ofRow.SetValueWithoutNotify(config.vegetationProfileRowUxml);
                ofRow.RegisterValueChangedCallback(e => { config.vegetationProfileRowUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofIcon = root.Q<ObjectField>("PrefabIconUXML");
            if (ofIcon != null)
            {
                ofIcon.objectType = typeof(VisualTreeAsset);
                ofIcon.allowSceneObjects = false;
                ofIcon.SetValueWithoutNotify(config.prefabIconUxml);
                ofIcon.RegisterValueChangedCallback(e => { config.prefabIconUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofDrag = root.Q<ObjectField>("DraggableAreaUXML");
            if (ofDrag != null)
            {
                ofDrag.objectType = typeof(VisualTreeAsset);
                ofDrag.allowSceneObjects = false;
                ofDrag.SetValueWithoutNotify(config.draggableAreaUxml);
                ofDrag.RegisterValueChangedCallback(e => { config.draggableAreaUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofStyles = root.Q<ObjectField>("StylesUSS");
            if (ofStyles != null)
            {
                ofStyles.objectType = typeof(StyleSheet);
                ofStyles.allowSceneObjects = false;
                ofStyles.SetValueWithoutNotify(config.stylesUss);
                ofStyles.RegisterValueChangedCallback(e => { config.stylesUss = e.newValue as StyleSheet; EditorUtility.SetDirty(config); });
            }

            var btnSave = root.Q<Button>("SaveConfiguration");
            if (btnSave != null)
            {
                btnSave.clicked += () =>
                {
                    if (!ConfigTools.IsComplete(config, out var reason))
                    {
                        EditorUtility.DisplayDialog("配置不完整", reason, "确定");
                        return;
                    }
                    ConfigTools.Save(config);
                    MrTerrainPainterWindow.Open();
                    Close();
                };
            }
        }
    }
}

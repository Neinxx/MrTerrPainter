using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using MrTerrainPainter.Editor.Config;
using UnityEngine;
using System.Linq;

namespace MrTerrainPainter.Editor.Views.Tabs
{
    public class SettingsTabView
    {
        private readonly MrTerrainPainterWindow window;
        private readonly VisualElement root;

        public SettingsTabView(MrTerrainPainterWindow window, VisualElement root)
        {
            this.window = window;
            this.root = root;
        }

        public void Setup()
        {
            var page = root;
            var useJobsToggle = page.Q<Toggle>("UseJobs");
            if (useJobsToggle == null)
            {
                useJobsToggle = new Toggle("UseJobs") { name = "UseJobs" };
                page.Add(useJobsToggle);
            }
            useJobsToggle.SetValueWithoutNotify(window.config.defaultUseJobs);
            useJobsToggle.RegisterValueChangedCallback(e => { window.config.defaultUseJobs = e.newValue; EditorUtility.SetDirty(window.config); });
            BindTextField(page, "RecipeGenerationPath", () => window.config.recipeGenerationPath, v => { window.config.recipeGenerationPath = v; EditorUtility.SetDirty(window.config); });
            BindToggle(page, "ShowPool", () => VegetationPool.ShowInHierarchy, v => { VegetationPool.ShowInHierarchy = v; window.config.showPoolInHierarchy = v; EditorUtility.SetDirty(window.config); });
            BindObjectField(page, "VegetationSharedUXML", typeof(VisualTreeAsset), () => window.config.vegetationSharedUxml, v => { window.config.vegetationSharedUxml = v as VisualTreeAsset; EditorUtility.SetDirty(window.config); });
            BindObjectField(page, "BrushOverlayUXML", typeof(VisualTreeAsset), () => window.config.brushOverlayUxml, v => { window.config.brushOverlayUxml = v as VisualTreeAsset; EditorUtility.SetDirty(window.config); });
            BindObjectField(page, "StylesUSS", typeof(StyleSheet), () => window.config.stylesUss, v => { window.config.stylesUss = v as StyleSheet; EditorUtility.SetDirty(window.config); });

            var btnSave = page.Q<Button>("SaveConfiguration") ?? page.Q<Button>("Save");
            if (btnSave != null)
            {
                btnSave.clicked += () => { ConfigTools.Save(window.config); EditorUtility.DisplayDialog("已保存", "配置已保存。", "确定"); };
            }

            var fold = page.Q<Foldout>("MappingList");
            var mappingTemplate = ConfigTools.GetSettingsMappingUxml();
            if (fold != null && mappingTemplate != null)
            {
                window.config.mappingEntries ??= new System.Collections.Generic.List<MrTerrainPainterConfig.MappingEntry>();
                
                void Refresh()
                {
                    fold.Clear();
                    int count = window.config.mappingEntries != null ? window.config.mappingEntries.Count : 0;
                    for (int i = 0; i < count; i++)
                    {
                        var rowRoot = mappingTemplate.Instantiate();
                        var mapRoot = rowRoot.Q<VisualElement>("Mapping");
                        var of = mapRoot.Q<ObjectField>("ObjectField");
                        if (of != null)
                        {
                            int idxLocal = i;
                            of.objectType = typeof(Transform);
                            of.allowSceneObjects = true;
                            var initialTf = window.config.mappingEntries[idxLocal].node;
                            of.SetValueWithoutNotify(initialTf);
                            of.RegisterValueChangedCallback(e =>
                            {
                                window.config.mappingEntries[idxLocal].node = e.newValue as Transform;
                                EditorUtility.SetDirty(window.config);
                                Refresh();
                            });
                        }
                        var typeField = mapRoot.Q<EnumField>("PrefabType");
                        if (typeField != null)
                        {
                            int idxLocal2 = i;
                            var initialType = window.config.mappingEntries[idxLocal2].type;
                            typeField.Init(initialType);
                            typeField.SetValueWithoutNotify(initialType);
                            typeField.RegisterValueChangedCallback(e =>
                            {
                                window.config.mappingEntries[idxLocal2].type = (Runtime.Profiles.PrefabType)e.newValue;
                                EditorUtility.SetDirty(window.config);
                                Refresh();
                            });
                        }
                        var layerField = mapRoot.Q<IntegerField>("Layer");
                        if (layerField == null)
                        {
                            layerField = new IntegerField("Layer") { name = "Layer" };
                            layerField.style.minWidth = 100;
                            mapRoot.Add(layerField);
                        }
                        if (layerField != null)
                        {
                            int idxLocal3 = i;
                            layerField.SetValueWithoutNotify(window.config.mappingEntries[idxLocal3].layer);
                            layerField.RegisterValueChangedCallback(e =>
                            {
                                window.config.mappingEntries[idxLocal3].layer = e.newValue;
                                EditorUtility.SetDirty(window.config);
                            });
                        }
                        var btnDel = rowRoot.Q<Button>("Delete");
                        if (btnDel != null)
                        {
                            int idx = i;
                            btnDel.clicked += () =>
                            {
                                if (window.config.mappingEntries != null && idx < window.config.mappingEntries.Count) window.config.mappingEntries.RemoveAt(idx);
                                SyncArraysFromEntries();
                                EditorUtility.SetDirty(window.config);
                                Refresh();
                            };
                        }
                        fold.Add(rowRoot);
                    }
                }
                Refresh();
                var btnAdd = page.Q<Button>("Add");
                if (btnAdd != null)
                {
                    btnAdd.clicked += () =>
                    {
                        var entry = new MrTerrainPainterConfig.MappingEntry();
                        entry.type = window.config.defaultGenerationType;
                        window.config.mappingEntries.Add(entry);
                        SyncArraysFromEntries();
                        EditorUtility.SetDirty(window.config);
                        Refresh();
                    };
                }
            }

            var btnCheck = page.Q<Button>("CheckConfiguration");
            if (btnCheck != null) btnCheck.clicked += () =>
            {
                if (ConfigTools.IsComplete(window.config, out var reason))
                {
                    EditorUtility.DisplayDialog("检查结果", "配置完整。", "确定");
                }
                else
                {
                    EditorUtility.DisplayDialog("检查结果", reason, "确定");
                }
            };

            var btnFix = page.Q<Button>("FixMissingResources");
            if (btnFix != null) btnFix.clicked += () =>
            {
                if (!string.IsNullOrEmpty(window.config.recipeGenerationPath))
                {
                    ConfigTools.EnsureFolder(window.config.recipeGenerationPath);
                }
                EditorUtility.DisplayDialog("已修复", "已尝试修复部分缺失资源路径。", "确定");
            };

            var btnBindDefaults = page.Q<Button>("BindDefaultResources");
            if (btnBindDefaults != null) btnBindDefaults.clicked += () =>
            {
                if (window.config.brushOverlayUxml == null)
                {
                    window.config.brushOverlayUxml = ConfigTools.GetBrushOverlayUxml(window.config);
                }
                if (window.config.stylesUss == null)
                {
                    window.config.stylesUss = ConfigTools.GetStylesUss(window.config);
                }
                if (window.config.startUxml == null) window.config.startUxml = ConfigTools.GetStartUxml(window.config);
                if (window.config.controlUxml == null) window.config.controlUxml = ConfigTools.GetControlUxml(window.config);
                if (window.config.paintUxml == null) window.config.paintUxml = ConfigTools.GetPaintUxml(window.config);
                if (window.config.generateUxml == null) window.config.generateUxml = ConfigTools.GetGenerateUxml(window.config);
                if (window.config.vegetationSharedUxml == null) window.config.vegetationSharedUxml = ConfigTools.GetVegetationSharedUxml(window.config);
                if (window.config.vegetationProfileRowUxml == null) window.config.vegetationProfileRowUxml = ConfigTools.GetVegetationProfileRowUxml(window.config);
                if (window.config.prefabIconUxml == null) window.config.prefabIconUxml = ConfigTools.GetPrefabIconUxml(window.config);
                if (window.config.draggableAreaUxml == null) window.config.draggableAreaUxml = ConfigTools.GetDraggableAreaUxml(window.config);
                EditorUtility.SetDirty(window.config);
                EditorUtility.DisplayDialog("已绑定", "已绑定可用的默认资源引用（如叠加层与样式）。", "确定");
            };
        }

        private void BindTextField(VisualElement page, string name, System.Func<string> getter, System.Action<string> setter)
        {
            var tf = page.Q<TextField>(name);
            if (tf == null) return;
            tf.SetValueWithoutNotify(getter());
            tf.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        private void BindToggle(VisualElement page, string name, System.Func<bool> getter, System.Action<bool> setter)
        {
            var t = page.Q<Toggle>(name);
            if (t == null) return;
            t.SetValueWithoutNotify(getter());
            t.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        private void BindObjectField(VisualElement page, string name, System.Type type, System.Func<Object> getter, System.Action<Object> setter)
        {
            var of = page.Q<ObjectField>(name);
            if (of == null) return;
            of.objectType = type;
            of.allowSceneObjects = false;
            of.SetValueWithoutNotify(getter());
            of.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        private void SyncArraysFromEntries() { }
    }
}

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
            var mappingTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/MrTerrPainterV1/Editor/MTPTerrainPainterSettingsMappinger.uxml");
            if (fold != null && mappingTemplate != null)
            {
                if (window.config.mappingEntries == null) window.config.mappingEntries = new System.Collections.Generic.List<MrTerrainPainterConfig.MappingEntry>();
                if (window.config.mappingEntries.Count == 0 && (window.config.objectList != null || window.config.objectTypeList != null))
                {
                    int max = Mathf.Max(window.config.objectList != null ? window.config.objectList.Length : 0, window.config.objectTypeList != null ? window.config.objectTypeList.Length : 0);
                    for (int i = 0; i < max; i++)
                    {
                        var entry = new MrTerrainPainterConfig.MappingEntry();
                        if (window.config.objectList != null && i < window.config.objectList.Length) entry.node = window.config.objectList[i];
                        if (window.config.objectTypeList != null && i < window.config.objectTypeList.Length) entry.type = window.config.objectTypeList[i];
                        window.config.mappingEntries.Add(entry);
                    }
                }
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
                            of.objectType = typeof(UnityEngine.Transform);
                            of.allowSceneObjects = true;
                            var initialGo = window.config.mappingEntries[i].node;
                            of.SetValueWithoutNotify(initialGo != null ? initialGo.transform : null);
                            of.RegisterValueChangedCallback(e =>
                            {
                                window.config.mappingEntries[i].node = (e.newValue as UnityEngine.Transform)?.gameObject;
                                SyncArraysFromEntries();
                                EditorUtility.SetDirty(window.config);
                            });
                        }
                        var typeField = mapRoot.Q<EnumField>("PrefabType");
                        if (typeField != null)
                        {
                            var initialType = window.config.mappingEntries[i].type;
                            typeField.Init(initialType);
                            typeField.SetValueWithoutNotify(initialType);
                            typeField.RegisterValueChangedCallback(e =>
                            {
                                window.config.mappingEntries[i].type = (MrTerrainPainter.Runtime.Profiles.PrefabType)e.newValue;
                                SyncArraysFromEntries();
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

        private void BindObjectField(VisualElement page, string name, System.Type type, System.Func<UnityEngine.Object> getter, System.Action<UnityEngine.Object> setter)
        {
            var of = page.Q<ObjectField>(name);
            if (of == null) return;
            of.objectType = type;
            of.allowSceneObjects = false;
            of.SetValueWithoutNotify(getter());
            of.RegisterValueChangedCallback(e => setter(e.newValue));
        }

        private void SyncArraysFromEntries()
        {
            var list = window.config.mappingEntries ?? new System.Collections.Generic.List<MrTerrainPainterConfig.MappingEntry>();
            window.config.objectList = list.Select(e => e.node).ToArray();
            window.config.objectTypeList = list.Select(e => e.type).ToArray();
        }
    }
}

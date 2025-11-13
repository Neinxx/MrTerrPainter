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
                void Refresh()
                {
                    fold.Clear();
                    int count = Mathf.Max(window.config.objectList != null ? window.config.objectList.Length : 0,
                                          window.config.objectTypeList != null ? window.config.objectTypeList.Length : 0);
                    for (int i = 0; i < count; i++)
                    {
                        var rowRoot = mappingTemplate.Instantiate();
                        var mapRoot = rowRoot.Q<VisualElement>("Mapping");
                        var of = mapRoot.Q<ObjectField>("ObjectField");
                        if (of != null)
                        {
                            of.objectType = typeof(UnityEngine.Transform);
                            of.allowSceneObjects = true;
                            var initialGo = (window.config.objectList != null && i < window.config.objectList.Length) ? window.config.objectList[i] : null;
                            of.SetValueWithoutNotify(initialGo != null ? initialGo.transform : null);
                            of.RegisterValueChangedCallback(e =>
                            {
                                var list = window.config.objectList?.ToList() ?? new System.Collections.Generic.List<UnityEngine.GameObject>();
                                while (i >= list.Count) list.Add(null);
                                list[i] = (e.newValue as UnityEngine.Transform)?.gameObject;
                                window.config.objectList = list.ToArray();
                                EditorUtility.SetDirty(window.config);
                            });
                        }
                        var typeField = mapRoot.Q<EnumField>("PrefabType");
                        if (typeField != null)
                        {
                            var initialType = (window.config.objectTypeList != null && i < window.config.objectTypeList.Length)
                                ? window.config.objectTypeList[i]
                                : window.config.defaultGenerationType;
                            typeField.Init(initialType);
                            typeField.SetValueWithoutNotify(initialType);
                            typeField.RegisterValueChangedCallback(e =>
                            {
                                var types = window.config.objectTypeList?.ToList() ?? new System.Collections.Generic.List<MrTerrainPainter.Runtime.Profiles.PrefabType>();
                                while (i >= types.Count) types.Add(window.config.defaultGenerationType);
                                types[i] = (MrTerrainPainter.Runtime.Profiles.PrefabType)e.newValue;
                                window.config.objectTypeList = types.ToArray();
                                EditorUtility.SetDirty(window.config);
                            });
                        }
                        var btnDel = rowRoot.Q<Button>("Delete");
                        if (btnDel != null)
                        {
                            int idx = i;
                            btnDel.clicked += () =>
                            {
                                if (window.config.objectList != null && idx < window.config.objectList.Length)
                                    window.config.objectList = window.config.objectList.Where((_, k) => k != idx).ToArray();
                                if (window.config.objectTypeList != null && idx < window.config.objectTypeList.Length)
                                    window.config.objectTypeList = window.config.objectTypeList.Where((_, k) => k != idx).ToArray();
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
                        var list = window.config.objectList?.ToList() ?? new System.Collections.Generic.List<UnityEngine.GameObject>();
                        list.Add(null);
                        window.config.objectList = list.ToArray();
                        var types = window.config.objectTypeList?.ToList() ?? new System.Collections.Generic.List<Runtime.Profiles.PrefabType>();
                        types.Add(window.config.defaultGenerationType);
                        window.config.objectTypeList = types.ToArray();
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
    }
}

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MrTerrainPainter.Editor.Views;
using MrTerrainPainter.Editor.Utils;
using MrTerrainPainter.Editor.Views.Tabs;
using static MrTerrainPainter.Editor.Services.VegetationGenerator;
using UnityEditor.UIElements;
using MrTerrainPainter.Editor.Tools;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Services;

namespace MrTerrainPainter.Editor
{
    // Control 页相关逻辑（窗口只做装配与绑定）
    public partial class MrTerrainPainterWindow
    {
        private void BuildControlSection()
        {
            if (controlRoot != null) return;
            controlRoot = PageAssembler.Assemble(pageContainer, uxmlControl);
            controlRoot.AddToClassList("mt-frame");

            // TabContent 容器
            controlTabContent = controlRoot.Q<VisualElement>("TabContent");
            if (controlTabContent == null)
            {
                controlTabContent = new VisualElement();
                controlRoot.Add(controlTabContent);
            }

            // TabBar 与 TabButton 样式
            var tabBar = controlRoot.Q<VisualElement>("TabBar");
            tabBar?.AddToClassList("mt-tabbar");
            var tabBtnPainting = controlRoot.Q<Button>("Painting");
            var tabBtnGenerate = controlRoot.Q<Button>("Generate");
            var tabBtnSettings = controlRoot.Q<Button>("Settings");
            tabBtnPainting?.AddToClassList("mt-tabbutton");
            tabBtnGenerate?.AddToClassList("mt-tabbutton");

            var controlTabView = new MrTerrainPainter.Editor.Views.Tabs.ControlTabView(this, controlRoot);
            controlTabView.SetupTabEvents();
            controlTabView.SetupNamedControls();
            UpdatePropertyPanelFromSelectedItem();
            // 地形列表刷新由 Generate 页负责，避免在控制页根上查询不存在的容器

            // 默认选中 Painting 标签
            var btnPainting = controlRoot.Q<Button>("Painting");
            var btnGenerate = controlRoot.Q<Button>("Generate");
            bool isComplete = ConfigTools.IsComplete(config, out var _);

            if (!isComplete)
            {
                btnPainting?.SetEnabled(false);
                btnGenerate?.SetEnabled(false);
                if (tabBtnSettings != null)
                {
                    tabBtnSettings.AddToClassList("mt-tabbutton--active");
                    btnPainting?.RemoveFromClassList("mt-tabbutton--active");
                    btnGenerate?.RemoveFromClassList("mt-tabbutton--active");
                }
                controlTabContent?.Clear();
                LoadSettingsTab();
                return;
            }

            if (btnPainting != null && btnGenerate != null)
            {
                SetTabActive(btnPainting, btnGenerate);
                controlTabContent?.Clear();
                LoadPaintingTab();
            }
        }





        public void OpenPaintingSettings()
        {
            if (controlRoot == null)
            {
                BuildControlSection();
            }
            if (controlRoot != null)
            {
                var btnPainting = controlRoot.Q<Button>("Painting");
                var btnGenerate = controlRoot.Q<Button>("Generate");
                if (btnPainting != null && btnGenerate != null) SetTabActive(btnPainting, btnGenerate);
                if (controlTabContent == null)
                {
                    controlTabContent = new VisualElement();
                    controlRoot.Add(controlTabContent);
                }
                controlTabContent.Clear();
                LoadPaintingTab();
            }
        }

        public void SetTabActive(Button active, Button inactive)
        {
            if (active == null || inactive == null) return;
            // 使用 USS 类控制激活状态，避免内联样式与主题冲突
            active.AddToClassList("mt-tabbutton--active");
            inactive.RemoveFromClassList("mt-tabbutton--active");
        }


        public void LoadPaintingTab()
        {
            if (controlTabContent == null) return;
            controlTabContent.Clear();
            bool isComplete = ConfigTools.IsComplete(config, out var _);
            if (!isComplete)
            {
                var btnSettings1 = controlRoot?.Q<Button>("Settings");
                var btnPaintingLocal = controlRoot?.Q<Button>("Painting");
                var btnGenerateLocal = controlRoot?.Q<Button>("Generate");
                btnPaintingLocal?.SetEnabled(false);
                btnGenerateLocal?.SetEnabled(false);
                btnSettings1?.AddToClassList("mt-tabbutton--active");
                btnPaintingLocal?.RemoveFromClassList("mt-tabbutton--active");
                btnGenerateLocal?.RemoveFromClassList("mt-tabbutton--active");
                LoadSettingsTab();
                return;
            }
            var btnPainting = controlRoot?.Q<Button>("Painting");
            var btnGenerate = controlRoot?.Q<Button>("Generate");
            var btnSettings = controlRoot?.Q<Button>("Settings");
            if (btnPainting != null && btnGenerate != null)
            {
                SetTabActive(btnPainting, btnGenerate);
                btnSettings?.RemoveFromClassList("mt-tabbutton--active");
            }
            settingsOpen = false;
            mode = Mode.Paint;
            NotifyWindowStateChanged();
            var scroll = new ScrollView();
            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.AddToClassList("mt-scroll");
            var vegRoot = PageAssembler.Assemble(scroll, uxmlVegetationShared);
            var paintRoot = PageAssembler.Assemble(scroll, uxmlPaint);
            controlTabContent.Add(scroll);
            ReloadAvailableProfiles();
            SetupVegetationProfileListPublic(vegRoot);
            BindPropertyPanelViewFromRoot(vegRoot);
            var view = new MrTerrainPainter.Editor.Views.Tabs.PaintingTabView(this, paintRoot);
            view.Setup();
        }

        public void LoadGenerateTab()
        {
            if (controlTabContent == null) return;
            controlTabContent.Clear();
            bool isComplete = ConfigTools.IsComplete(config, out var _);
            if (!isComplete)
            {
                var btnSettings1 = controlRoot?.Q<Button>("Settings");
                var btnPaintingLocal = controlRoot?.Q<Button>("Painting");
                var btnGenerateLocal = controlRoot?.Q<Button>("Generate");
                btnPaintingLocal?.SetEnabled(false);
                btnGenerateLocal?.SetEnabled(false);
                btnSettings1?.AddToClassList("mt-tabbutton--active");
                btnPaintingLocal?.RemoveFromClassList("mt-tabbutton--active");
                btnGenerateLocal?.RemoveFromClassList("mt-tabbutton--active");
                LoadSettingsTab();
                return;
            }
            var btnPainting = controlRoot?.Q<Button>("Painting");
            var btnGenerate = controlRoot?.Q<Button>("Generate");
            var btnSettings = controlRoot?.Q<Button>("Settings");
            if (btnPainting != null && btnGenerate != null)
            {
                SetTabActive(btnGenerate, btnPainting);
                btnSettings?.RemoveFromClassList("mt-tabbutton--active");
            }
            var scroll = new ScrollView();
            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.AddToClassList("mt-scroll");
            var vegRoot = PageAssembler.Assemble(scroll, uxmlVegetationShared);
            var genRoot = PageAssembler.Assemble(scroll, uxmlGenerate);
            controlTabContent.Add(scroll);
            ReloadAvailableProfiles();
            SetupVegetationProfileListPublic(vegRoot);
            BindPropertyPanelViewFromRoot(vegRoot);
            var tab = new MrTerrainPainter.Editor.Views.Tabs.GenerateTabView(this, genRoot);
            tab.Setup();
            settingsOpen = false;
            mode = Mode.Generate;
            NotifyWindowStateChanged();
        }

        public void UpdateGenerateActionsVisibility(VisualElement genParam)
        {
            if (genParam == null) return;
            bool hasTerrains = terrainListUIData != null && terrainListUIData.Count > 0;
            var btnGenerate = genParam.Q<Button>("GenerateTerrainObject");
            var btnClear = genParam.Q<Button>("ClearTerrainObject");
            if (btnGenerate != null)
                btnGenerate.style.display = hasTerrains ? DisplayStyle.Flex : DisplayStyle.None;
            if (btnClear != null)
                btnClear.style.display = hasTerrains ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void LoadSettingsTab()
        {
            var root = controlTabContent;
            if (root == null) return;
            var settingsUxml = ConfigTools.GetSettingsUxml();
            root.Clear();
            var page = PageAssembler.Assemble(root, settingsUxml);
            var view = new SettingsTabView(this, page);
            view.Setup();
            settingsOpen = true;
            NotifyWindowStateChanged();
        }

        public void OpenSettingsTab()
        {
            if (controlRoot == null)
            {
                BuildControlSection();
            }
            if (controlRoot != null)
            {
                if (controlTabContent == null)
                {
                    controlTabContent = new VisualElement();
                    controlRoot.Add(controlTabContent);
                }
                controlTabContent.Clear();
                var btnSettings = controlRoot.Q<Button>("Settings");
                var btnPainting = controlRoot.Q<Button>("Painting");
                var btnGenerate = controlRoot.Q<Button>("Generate");
                if (btnSettings != null)
                {
                    btnSettings.AddToClassList("mt-tabbutton--active");
                    btnPainting?.RemoveFromClassList("mt-tabbutton--active");
                    btnGenerate?.RemoveFromClassList("mt-tabbutton--active");
                }
                LoadSettingsTab();
            }
        }

        public void OnConfigurationCompleted()
        {
            var btnPainting = controlRoot?.Q<Button>("Painting");
            var btnGenerate = controlRoot?.Q<Button>("Generate");
            if (btnPainting != null) btnPainting.SetEnabled(true);
            if (btnGenerate != null) btnGenerate.SetEnabled(true);
            if (controlTabContent != null)
            {
                SetTabActive(btnPainting, btnGenerate);
                controlTabContent.Clear();
                LoadPaintingTab();
            }
        }

        public void CreateNewVegetationAndRefresh()
        {
            prefabAssignment?.CreateNewVegetationItem();
            refreshController?.RefreshAllUI();
        }

        private PreviewGridView previewGridView;
        public void SetPreviewListContainer(VisualElement ve)
        {
            uiPreviewPrefabList = ve;
            previewGridView = new PreviewGridView(
                uiPreviewPrefabList,
                () => GetProfileItemsSnapshot(),
                () => selectedItemIndex,
                i => SetSelectedThumbIndex(i),
                () => currentProfile,
                idx => prefabAssignment?.RemoveItemAt(idx),
                (idx, type) => prefabAssignment?.SetItemType(currentProfile, idx, type),
                () => RefreshVegetationListUI(),
                () => RefreshPreviewListUI()
            );
        }

        public void SetupVegetationProfileListPublic(VisualElement hostRoot)
        {
            SetupVegetationProfileList(hostRoot);
        }



        public void BindPropertyPanelViewFromRoot(VisualElement queryRoot)
        {
            if (queryRoot == null) return;
            propertyPanelView = new PropertyPanelView(queryRoot);
            propertyPanelView.Bind(new PropertyPanelView.PropertyPanelCallbacks
            {
                GetSelectedItem = GetSelectedItem,
                GetCurrentProfile = () => currentProfile,
                GetSelectedItemIndex = () => selectedItemIndex,
                RemoveItemAt = idx => { prefabAssignment?.RemoveItemAt(idx); },
                AssignPrefabToItem = (profile, index, go) => { prefabAssignment?.AssignPrefabToItem(profile, index, go); currentPrefab = go; },
                RefreshPreviewListUI = () => RefreshPreviewListUI(),
                RefreshVegetationListUI = () => RefreshVegetationListUI(),
                UpdatePropertyPanelFromSelectedItem = () => UpdatePropertyPanelFromSelectedItem(),
                MarkCurrentProfileDirty = () => { if (currentProfile != null) EditorUtility.SetDirty(currentProfile); }
            });
        }

        /// <summary>
        /// 初始化生成标签页的UI结构
        /// </summary>




        /// <summary>
        /// 处理生成植被的逻辑
        /// </summary>
        public void HandleGenerateAction()
        {
            // 自动补充选中地形
            if (!AutoPopulateSelectedTerrains())
            {
                EditorUtility.DisplayDialog(
                    "提示",
                    "没有可用地形或未选择Profile。请先在Control页添加选中地形。",
                    "确定"
                );
                return;
            }

            EnsureRandom();
            var filter = BuildFilterSettings();
            var placementOverrides = BuildPlacementOverrides();

            // 生成主Profile的植被
            Services.VegetationGenerator.GenerateOnTerrains(
                selectedTerrains,
                currentProfile,
                null,
                filter,
                placementOverrides
            );

            // 生成额外Profile的植被
            GenerateExtraProfilesVegetation(filter, placementOverrides);

            MrTerrainPainter.Editor.Utils.EditorSceneUtils.MarkSceneDirty();
        }

        /// <summary>
        /// 自动填充选中的地形
        /// </summary>
        /// <returns>是否有有效的选中地形和Profile</returns>
        private bool AutoPopulateSelectedTerrains()
        {
            if (selectedTerrains.Count == 0)
            {
                AddTerrainsFromSelection();

                // 如果选择中没有地形，尝试添加所有活跃地形
                if (selectedTerrains.Count == 0)
                {
                    AddActiveTerrains();
                }

                // 刷新地形列表UI
                RefreshTerrainListUI();
            }

            return selectedTerrains.Count > 0 && currentProfile != null;
        }

        /// <summary>
        /// 从选择的对象中添加地形
        /// </summary>
        private void AddTerrainsFromSelection()
        {
            foreach (var obj in Selection.gameObjects)
            {
                var terrain = obj.GetComponent<Terrain>();
                if (terrain != null && !selectedTerrains.Contains(terrain))
                {
                    selectedTerrains.Add(terrain);
                }
            }
        }

        /// <summary>
        /// 添加所有活跃的地形
        /// </summary>
        private void AddActiveTerrains()
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain != null && !selectedTerrains.Contains(terrain))
                {
                    selectedTerrains.Add(terrain);
                }
            }
        }

        /// <summary>
        /// 刷新地形列表UI
        /// </summary>
        private void RefreshTerrainListUI()
        {
            if (controlRoot == null) return;
            PopulateTerrainListUI(controlRoot);
            MrTerrainPainter.Editor.Tools.MTPBrushContext.SetSelectedTerrains(selectedTerrains);
        }

        /// <summary>
        /// 生成额外Profile的植被
        /// </summary>
        private void GenerateExtraProfilesVegetation(FilterSettings filter, PlacementOverrides overrides)
        {
            foreach (var profile in MrTerrainPainter.Editor.Tools.MTPBrushContext.ExtraProfiles)
            {
                if (profile == null || profile.IsEmpty()) continue;

                Services.VegetationGenerator.GenerateOnTerrains(
                    selectedTerrains,
                    profile,
                    null,
                    filter,
                    overrides
                );
            }
        }

        /// <summary>
        /// 处理清除植被的逻辑
        /// </summary>
        public void HandleClearAction()
        {
            foreach (var terrain in selectedTerrains)
            {
                if (terrain == null) continue;

                VegetationPool.RecycleAllInstances(terrain, true, "Clear Vegetation Instances");
            }

            MrTerrainPainter.Editor.Utils.EditorSceneUtils.MarkSceneDirty();

            // mode = Mode.Generate;
            // if (controlRoot != null)
            // {
            //     var btnPainting = controlRoot.Q<Button>("Painting");
            //     var btnGenerate = controlRoot.Q<Button>("Generate");
            //     btnPainting?.RemoveFromClassList("mt-tabbutton--active");
            //     btnGenerate?.RemoveFromClassList("mt-tabbutton--active");
            // }
            // controlTabContent?.Clear();
        }

        // —— 绑定：Generate 页过滤控件 ——
        public void BindGenerateFilterControls(VisualElement root)
        {
            if (root == null) return; // 提前返回
            generateFilterView = new GenerateFilterView(root);
            // 统一在窗口持有一个 FilterSettings 实例，包含噪声与分布参数
            if (genFilter == null) genFilter = new FilterSettings();
            genFilter.noise = noise;
            generateFilterView.Bind(genFilter);
            var useBurstGen = root.Q<Toggle>("UseBurstPoissonGen");
            if (useBurstGen != null)
            {
                useBurstGen.SetValueWithoutNotify(Services.VegetationGenerator.UseBurstPoisson);
                useBurstGen.RegisterValueChangedCallback(evt => { Services.VegetationGenerator.UseBurstPoisson = evt.newValue; });
            }
        }

        // —— 绑定：Paint 页笔刷控件 ——
        public void BindBrushControls(VisualElement root)
        {
            if (root == null) return; // 提前返回
            brushView = new BrushView(root);
            brushView.Bind(brush);
        }



        public void AddPrefabsToCurrentProfile(GameObject[] gos)
        {
            if (gos == null || gos.Length == 0) return;
            prefabAssignment?.AddPrefabsToProfile(gos);
            RefreshPreviewListUI();
            RefreshVegetationListUI();
            UpdatePropertyPanelFromSelectedItem();
        }

        private readonly System.Collections.Generic.List<Runtime.Profiles.VegetationProfile> availableProfiles = new();

        // 统一构建 VegetationProfile 列表与交互
        private void SetupVegetationProfileList(VisualElement hostRoot)
        {
            if (hostRoot == null) return;

            if (controlView == null)
            {
                controlView = new ControlView(hostRoot, uxmlVegetationProfileRow);
            }

            var cb = new ControlViewCallbacks
            {
                CreateNewVegetationProfileAsset = CreateNewVegetationProfileAsset,
                ReloadAvailableProfiles = ReloadAvailableProfiles,
                RefreshAllUI = RefreshAllUI,
                SetListSelectionToCurrentProfile = SetListSelectionToCurrentProfile,
                DeleteVegetationProfileAsset = DeleteVegetationProfileAsset,
                SetCurrentProfile = p => { currentProfile = p; MrTerrainPainter.Editor.Tools.MTPBrushContext.CurrentProfile = p; },
                ResetSelectionForProfileChange = () => { selectedItemIndex = -1; selectedThumbIndices.Clear(); },
                GetCurrentProfile = () => currentProfile,
                OnListContentWidthMeasured = w => vegetationListContentWidth = w
            };

            // 使用专用视图承载缩略图与拖拽新增区域
            var thumbView = new ThumbListView(
                uxmlVegetationProfilePrefabIcon,
                new ThumbListView.ThumbListViewCallbacks
                {
                    GetCurrentProfile = () => currentProfile,
                    SetCurrentProfile = p => { currentProfile = p; MrTerrainPainter.Editor.Tools.MTPBrushContext.CurrentProfile = p; },
                    GetSelectedItemIndex = () => selectedItemIndex,
                    SetSelectedItemIndex = i => selectedItemIndex = i,
                    IsIndexSelected = i => selectedThumbIndices.Contains(i),
                    AddSelectedIndex = i => selectedThumbIndices.Add(i),
                    RemoveSelectedIndex = i => selectedThumbIndices.Remove(i),
                    ClearSelectedIndices = () => selectedThumbIndices.Clear(),
                    GetSelectedIndices = () => selectedThumbIndices.ToList(),
                    UpdatePropertyPanelFromSelectedItem = () => UpdatePropertyPanelFromSelectedItem(),
                    RefreshVegetationListUI = () => RefreshVegetationListUI(),
                    RefreshPreviewListUI = () => RefreshPreviewListUI(),
                    RemoveItemAtFromProfile = (profile, index) => prefabAssignment?.RemoveItemAtFromProfile(profile, index),
                    RemoveItemsAtFromProfile = (profile, indices) => prefabAssignment?.RemoveItemsAtFromProfile(profile, indices),
                    SetItemType = (profile, index, type) => prefabAssignment?.SetItemType(profile, index, type),
                    OpenPrefabPickerForItem = (profile, index) => OpenPrefabPickerForItem(profile, index),
                    GetAvailableTypes = () =>
                    {
                        var types = config?.mappingEntries?.Select(e => e.type).Distinct().ToList();
                        return types != null && types.Count > 0 ? types : ((Runtime.Profiles.PrefabType[])System.Enum.GetValues(typeof(Runtime.Profiles.PrefabType)));
                    }
                }
            );

            var addSlotView = new DraggableAddSlotView(
                uxmlVegetationProfileDraggableArea,
                new DraggableAddSlotView.DraggableAddSlotViewCallbacks
                {
                    OpenPrefabPickerForNewItem = p => OpenPrefabPickerForNewItem(p),
                    AddPrefabAsNewItem = (profile, go) => prefabAssignment?.AddPrefabAsNewItem(profile, go)
                }
            );

            controlView.SetupVegetationProfileList(
                availableProfiles,
                new System.Collections.Generic.List<Runtime.Profiles.VegetationProfile>(MrTerrainPainter.Editor.Tools.MTPBrushContext.ExtraProfiles as System.Collections.Generic.IEnumerable<Runtime.Profiles.VegetationProfile>),
                cb,
                addSlotView.MakeDraggableArea,
                thumbView.MakeThumb,
                ThumbRows);

            // 兼容窗口现有刷新函数
            uiVegetationList = controlView.ListView;
        }

        private void RefreshVegetationListUI()
        {
            if (uiVegetationList == null) return;
            var current = uiVegetationList.itemsSource as System.Collections.IList;
            uiVegetationList.itemsSource = availableProfiles;
            if (current == null || current.Count != availableProfiles.Count)
            {
                uiVegetationList.Rebuild();
            }
            uiVegetationList.RefreshItems();
        }

        private void RefreshPreviewListUI()
        {
            if (uiPreviewPrefabList == null) return;
            Utils.UIThrottle.RunOnPanel(uiPreviewPrefabList, () =>
            {
                previewGridView?.Render();
                uiPreviewListView = previewGridView?.ListView;
                UpdatePreviewSelectionVisuals();
            });
        }



        private void UpdatePreviewSelectionVisuals()
        {
            if (uiPreviewListView == null) return;
            var children = uiPreviewListView.contentContainer.Children().ToList();
            for (int ci = 0; ci < children.Count; ci++)
            {
                var ve = children[ci];
                var idx = ve.userData is int n ? n : -1;
                if (idx < 0) continue;
                if (idx == selectedItemIndex) ve.AddToClassList("preview-item--selected"); else ve.RemoveFromClassList("preview-item--selected");
            }
        }

        private void UpdatePropertyPanelFromSelectedItem()
        {
            propertyPanelView?.UpdateFromSelectedItem();
            var item = GetSelectedItem();
            currentPrefab = item != null ? item.prefab : null;
        }

        private void SetSelectedThumbIndex(int index)
        {
            selectedItemIndex = index;
            currentPrefab = GetSelectedItem()?.prefab;
            Utils.UIThrottle.RunOnPanel(uiPreviewListView, UpdatePreviewSelectionVisuals);
            UpdatePropertyPanelFromSelectedItem();
        }

        public void PopulateTerrainListUI(VisualElement root)
        {
            if (root == null) return;
            var container = root.Q<VisualElement>("TerrainList");
            if (container != null)
            {
                if (container is Foldout fold)
                {
                    fold.style.display = terrainListUIData.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }
                var listView = container.Q<ListView>("TerrainListLV");
                if (listView == null)
                {
                    container.Clear();
                    listView = new ListView
                    {
                        name = "TerrainListLV",
                        selectionType = SelectionType.None,
                        virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                        fixedItemHeight = 24
                    };
                    listView.AddToClassList("mt-terrain-list");
                    container.Add(listView);
                }
                listView.itemsSource = terrainListUIData;
                if (terrainListUIData.Count <= 10)
                {
                    listView.RemoveFromClassList("mt-terrain-list--max10");
                    listView.AddToClassList("mt-terrain-list--auto");
                }
                else
                {
                    listView.RemoveFromClassList("mt-terrain-list--auto");
                    listView.AddToClassList("mt-terrain-list--max10");
                }
                listView.makeItem = () =>
                {
                    var of = new ObjectField
                    {
                        objectType = typeof(Terrain),
                        allowSceneObjects = true,
                        label = string.Empty
                    };
                    of.AddToClassList("mt-terrain-list__item");
                    return of;
                };
                listView.bindItem = (elem, i) =>
                {
                    if (elem is not ObjectField of) return;
                    var t = (i >= 0 && i < terrainListUIData.Count) ? terrainListUIData[i] : null;
                    of.SetValueWithoutNotify(t);
                };
            }
        }
        public void RefreshPreviewListUIPublic()
        {
            RefreshPreviewListUI();
        }

    }
}

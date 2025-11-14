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

namespace MrTerrainPainter.Editor
{
    // Contral 页相关逻辑（窗口只做装配与绑定）
    public partial class MrTerrainPainterWindow
    {
        private void BuildContralSection()
        {
            if (contralRoot != null) return; // 已加载则提前返回
            contralRoot = PageAssembler.Assemble(pageContainer, uxmlContral);
            contralRoot.AddToClassList("mt-frame");

            // TabContent 容器
            contralTabContent = contralRoot.Q<VisualElement>("TabContent");
            if (contralTabContent == null)
            {
                contralTabContent = new VisualElement();
                contralRoot.Add(contralTabContent);
            }

            // TabBar 与 TabButton 样式
            var tabBar = contralRoot.Q<VisualElement>("TabBar");
            tabBar?.AddToClassList("mt-tabbar");
            var tabBtnPainting = contralRoot.Q<Button>("Painting");
            var tabBtnGenerate = contralRoot.Q<Button>("Generate");
            tabBtnPainting?.AddToClassList("mt-tabbutton");
            tabBtnGenerate?.AddToClassList("mt-tabbutton");

            var contralTabView = new MrTerrainPainter.Editor.Views.Tabs.ContralTabView(this, contralRoot);
            contralTabView.SetupTabEvents();
            contralTabView.SetupNamedControls();
            UpdatePropertyPanelFromSelectedItem();
            // 地形列表刷新由 Generate 页负责，避免在控制页根上查询不存在的容器

            // 默认选中 Painting 标签
            var btnPainting = contralRoot.Q<Button>("Painting");
            var btnGenerate = contralRoot.Q<Button>("Generate");
            if (btnPainting != null && btnGenerate != null)
            {
                SetTabActive(btnPainting, btnGenerate);
                contralTabContent?.Clear();
                LoadPaintingTab();
            }
        }

        // 根据地形列表数量切换 Contral 页可见性（清空或刷新时调用）
        private void ToggleContralPageVisibility()
        {
            if (contralRoot == null) return; // 未构建无需处理
            var hasTerrains = terrainListUIData != null && terrainListUIData.Count > 0;
            contralRoot.style.display = hasTerrains ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetupContralTabEvents()
        {
            if (contralRoot == null) return;
            var btnPainting = contralRoot.Q<Button>("Painting");
            var btnGenerate = contralRoot.Q<Button>("Generate");
            var btnSettings = contralRoot.Q<Button>("Settings");
            if (btnPainting != null)
                btnPainting.clicked += () =>
                {
                    // 始终保持一个标签激活
                    SetTabActive(btnPainting, btnGenerate);
                    btnSettings?.RemoveFromClassList("mt-tabbutton--active");
                    contralTabContent?.Clear();
                    LoadPaintingTab();
                };
            if (btnGenerate != null)
                btnGenerate.clicked += () =>
                {
                    SetTabActive(btnGenerate, btnPainting);
                    btnSettings?.RemoveFromClassList("mt-tabbutton--active");
                    contralTabContent?.Clear();
                    LoadGenerateTab();
                };
            if (btnSettings != null)
                btnSettings.clicked += () =>
                {
                    // 激活 Settings，并取消其他
                    btnSettings.AddToClassList("mt-tabbutton--active");
                    btnPainting?.RemoveFromClassList("mt-tabbutton--active");
                    btnGenerate?.RemoveFromClassList("mt-tabbutton--active");
                    contralTabContent?.Clear();
                    LoadSettingsTab();
                };

            // CreateNewVegetation 按钮
            var btnCreate = contralRoot.Q<Button>("CreateNewVegetation");
            if (btnCreate != null)
            {
                // btnCreate.AddToClassList("mt-button");
                btnCreate.clicked += () =>
                {
                    prefabAssignment?.CreateNewVegetationItem();
                    refreshController?.RefreshAllUI();
                };
            }

        }

        public void OpenPaintingSettings()
        {
            if (contralRoot == null)
            {
                BuildContralSection();
            }
            if (contralRoot != null)
            {
                var btnPainting = contralRoot.Q<Button>("Painting");
                var btnGenerate = contralRoot.Q<Button>("Generate");
                if (btnPainting != null && btnGenerate != null) SetTabActive(btnPainting, btnGenerate);
                if (contralTabContent == null)
                {
                    contralTabContent = new VisualElement();
                    contralRoot.Add(contralTabContent);
                }
                contralTabContent.Clear();
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
            if (contralTabContent == null) return;
            contralTabContent.Clear();
            var scroll = new ScrollView();
            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.contentContainer.style.paddingRight = 16;
            var vegRoot = PageAssembler.Assemble(scroll, uxmlVegetationShared);
            var paintRoot = PageAssembler.Assemble(scroll, uxmlPaint);
            contralTabContent.Add(scroll);
            ReloadAvailableProfiles();
            SetupVegetationProfileListPublic(vegRoot);
            BindPropertyPanelViewFromRoot(vegRoot);
            var view = new MrTerrainPainter.Editor.Views.Tabs.PaintingTabView(this, paintRoot);
            view.Setup();
        }

        public void LoadGenerateTab()
        {
            if (contralTabContent == null) return;
            contralTabContent.Clear();
            var scroll = new ScrollView();
            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.contentContainer.style.paddingRight = 16;
            var vegRoot = PageAssembler.Assemble(scroll, uxmlVegetationShared);
            var genRoot = PageAssembler.Assemble(scroll, uxmlGenerate);
            contralTabContent.Add(scroll);
            ReloadAvailableProfiles();
            SetupVegetationProfileListPublic(vegRoot);
            BindPropertyPanelViewFromRoot(vegRoot);
            var tab = new MrTerrainPainter.Editor.Views.Tabs.GenerateTabView(this, genRoot);
            tab.Setup();
            mode = Mode.Generate;
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
            var root = contralTabContent;
            if (root == null) return;
            var settingsUxml = ConfigTools.GetSettingsUxml();
            root.Clear();
            var page = PageAssembler.Assemble(root, settingsUxml);
            var view = new SettingsTabView(this, page);
            view.Setup();
        }

        public void OpenSettingsTab()
        {
            if (contralRoot == null)
            {
                BuildContralSection();
            }
            if (contralRoot != null)
            {
                if (contralTabContent == null)
                {
                    contralTabContent = new VisualElement();
                    contralRoot.Add(contralTabContent);
                }
                contralTabContent.Clear();
                LoadSettingsTab();
            }
        }

        public void CreateNewVegetationAndRefresh()
        {
            prefabAssignment?.CreateNewVegetationItem();
            refreshController?.RefreshAllUI();
        }

        public void SetPreviewListContainer(VisualElement ve)
        {
            uiPreviewPrefabList = ve;
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
                    "没有可用地形或未选择Profile。请先在Contral页添加选中地形。",
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

            MarkSceneDirty();
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
            if (contralRoot == null) return;
            PopulateTerrianListUI(contralRoot);
        }

        /// <summary>
        /// 生成额外Profile的植被
        /// </summary>
        private void GenerateExtraProfilesVegetation(FilterSettings filter, PlacementOverrides overrides)
        {
            foreach (var profile in extraProfiles)
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

            MarkSceneDirty();

            mode = Mode.Generate;
            if (contralRoot != null)
            {
                var btnPainting = contralRoot.Q<Button>("Painting");
                var btnGenerate = contralRoot.Q<Button>("Generate");
                btnPainting?.RemoveFromClassList("mt-tabbutton--active");
                btnGenerate?.RemoveFromClassList("mt-tabbutton--active");
            }
            contralTabContent?.Clear();
        }

        // —— 绑定：Generate 页过滤控件 ——
        public void BindGenerateFilterControls(VisualElement root)
        {
            if (root == null) return; // 提前返回
            generateFilterView = new GenerateFilterView(root);
            generateFilterView.Bind(noise);
        }

        // —— 绑定：Paint 页笔刷控件 ——
        public void BindBrushControls(VisualElement root)
        {
            if (root == null) return; // 提前返回
            brushView = new BrushView(root);
            brushView.Bind(brush);
        }

        public void BindContralNamedControls()
        {
            if (contralRoot == null) return;
            if (contralBindingsInitialized) return;
            var prefabRange = contralRoot.Q<VisualElement>("PrefabRange");
            var queryRoot = prefabRange ?? contralRoot;
            SetPreviewListContainer(contralRoot.Q<VisualElement>("PreviewPrefabList"));
            SetupVegetationProfileListPublic(queryRoot);
            // 如果列表仍为空，回退构建一个简单的列表以确保显示
            if (uiVegetationList == null)
            {
                var container = contralRoot.Q<VisualElement>("VegetationList") ?? queryRoot;
                if (container != null)
                {
                    var lv = new ListView
                    {
                        name = "VegetationProfileList",
                        itemsSource = availableProfiles,
                        selectionType = SelectionType.Single,
                        virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                        fixedItemHeight = 20,
                        makeItem = () => new Label(),
                        bindItem = (elem, i) =>
                        {
                            if (elem is Label lab)
                            {
                                var p = (i >= 0 && i < availableProfiles.Count) ? availableProfiles[i] : null;
                                lab.text = p != null ? p.name : "<None>";
                            }
                        }
                    };
                    container.Add(lv);
                    uiVegetationList = lv;
                }
            }
            // 属性面板视图绑定（依赖注入回调，降低耦合）
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

            if (uiPreviewPrefabList != null)
            {
                RefreshPreviewListUI();
            }
            if (uiVegetationList != null)
            {
                RefreshVegetationListUI();
            }
            contralBindingsInitialized = true;
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

            if (contralView == null)
            {
                contralView = new ContralView(hostRoot, uxmlVegetationProfileRow);
            }

            var cb = new ContralViewCallbacks
            {
                CreateNewVegetationProfileAsset = CreateNewVegetationProfileAsset,
                ReloadAvailableProfiles = ReloadAvailableProfiles,
                RefreshAllUI = RefreshAllUI,
                SetListSelectionToCurrentProfile = SetListSelectionToCurrentProfile,
                DeleteVegetationProfileAsset = DeleteVegetationProfileAsset,
                SetCurrentProfile = p => { currentProfile = p; },
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
                    SetCurrentProfile = p => currentProfile = p,
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

            contralView.SetupVegetationProfileList(
                availableProfiles,
                extraProfiles,
                cb,
                addSlotView.MakeDraggableArea,
                thumbView.MakeThumb,
                ThumbRows);

            // 兼容窗口现有刷新函数
            uiVegetationList = contralView.ListView;
        }

        private void RefreshVegetationListUI()
        {
            if (uiVegetationList == null) return;
            uiVegetationList.itemsSource = availableProfiles;
            uiVegetationList.Rebuild();
        }

        private void RefreshPreviewListUI()
        {
            if (uiPreviewPrefabList == null) return;
            Utils.UIThrottle.RunOnPanel(uiPreviewPrefabList, EnsurePreviewListView);
        }

        private void EnsurePreviewListView()
        {
            if (currentProfile != null)
            {
                prefabAssignment?.CleanNullPrefabItems(currentProfile);
            }
            var items = GetProfileItemsSnapshot();
            if (uiPreviewListView == null)
            {
                var lv = new ListView
                {
                    selectionType = SelectionType.None,
                    itemsSource = items,
                    virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                    fixedItemHeight = 72
                };
                lv.style.flexGrow = 1;
                lv.makeItem = () =>
                {
                    var box = new VisualElement();
                    box.AddToClassList("preview-item");
                    var img = new Image();
                    img.AddToClassList("preview-item__image");
                    box.Add(img);
                    return box;
                };
                lv.bindItem = (elem, i) =>
                {
                    var it = (i >= 0 && i < items.Count) ? items[i] : null;
                    var img = elem.Q<Image>();
                    Texture2D tex = null;
                    if (it != null && it.prefab != null)
                    {
                        var id = it.prefab.GetInstanceID();
                        if (!previewTexCache.TryGetValue(id, out tex) || tex == null)
                        {
                            tex = AssetPreview.GetAssetPreview(it.prefab) ?? AssetPreview.GetMiniThumbnail(it.prefab);
                            previewTexCache[id] = tex;
                            if (tex == null) Utils.UIThrottle.RunNextFrame(() => RefreshPreviewListUI());
                        }
                    }
                    img.image = tex;
                    elem.userData = i;
                    var sel = i == selectedItemIndex;
                    if (sel) elem.AddToClassList("preview-item--selected"); else elem.RemoveFromClassList("preview-item--selected");
                    elem.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button == 0)
                        {
                            SetSelectedThumbIndex(i);
                            evt.StopPropagation();
                        }
                        else if (evt.button == 1)
                        {
                            var menu = new GenericMenu();
                            menu.AddItem(new GUIContent("删除"), false, () =>
                            {
                                var idx = i;
                                Utils.UIThrottle.RunNextFrame(() =>
                                {
                                    prefabAssignment?.RemoveItemAt(idx);
                                    RefreshVegetationListUI();
                                    RefreshPreviewListUI();
                                });
                            });
                            var values = (MrTerrainPainter.Runtime.Profiles.PrefabType[])System.Enum.GetValues(typeof(MrTerrainPainter.Runtime.Profiles.PrefabType));
                            for (int vi = 0; vi < values.Length; vi++)
                            {
                                var val = values[vi];
                                bool isCurrent = it != null && it.prefabType == val;
                                menu.AddItem(new GUIContent($"类型/{val}"), isCurrent, () =>
                                {
                                    var idx = i;
                                    prefabAssignment?.SetItemType(currentProfile, idx, val);
                                    if (currentProfile != null) EditorUtility.SetDirty(currentProfile);
                                    Utils.UIThrottle.RunOnPanel(uiPreviewListView, () => UpdatePreviewSelectionVisuals());
                                });
                            }
                            menu.ShowAsContext();
                            evt.StopPropagation();
                        }
                    });
                };
                uiPreviewListView = lv;
                uiPreviewPrefabList.Clear();
                uiPreviewPrefabList.Add(uiPreviewListView);
            }
            else
            {
                uiPreviewListView.itemsSource = items;
                uiPreviewListView.Rebuild();
            }
            Utils.UIThrottle.RunOnPanel(uiPreviewListView, UpdatePreviewSelectionVisuals);
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

        public void PopulateTerrianListUI(VisualElement root)
        {
            if (root == null) return;
            var container = root.Q<VisualElement>("TerrainList");
            if (container != null)
            {
                if (container is Foldout fold)
                {
                    fold.style.display = terrainListUIData.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }
                container.style.flexGrow = 0;
                container.Clear();
                var listView = new ListView
                {
                    name = "TerrainListLV",
                    itemsSource = terrainListUIData,
                    selectionType = SelectionType.None,
                    virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                    fixedItemHeight = 24,
                    makeItem = () =>
                    {
                        var of = new ObjectField
                        {
                            objectType = typeof(Terrain),
                            allowSceneObjects = true,
                            label = string.Empty
                        };
                        of.style.marginBottom = 2;
                        return of;
                    },
                    bindItem = (elem, i) =>
                    {
                        if (elem is not ObjectField of) return;
                        var t = (i >= 0 && i < terrainListUIData.Count) ? terrainListUIData[i] : null;
                        of.SetValueWithoutNotify(t);
                    }
                };
                var max = 10 * (listView.fixedItemHeight + 4);
                listView.style.height = max;
                listView.style.maxHeight = max;
                listView.style.flexGrow = 0;
                container.Add(listView);
            }
        }
        public void RefreshPreviewListUIPublic()
        {
            RefreshPreviewListUI();
        }

    }
}

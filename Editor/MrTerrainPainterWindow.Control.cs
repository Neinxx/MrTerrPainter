using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MrTerrainPainter.Editor.Views;
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
            contralRoot = InstantiatePage(uxmlContral);
            pageContainer.Add(contralRoot);
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

            // 初始不加载任何模块：等待用户选择 Tab
            SetupContralTabEvents();

            BindContralNamedControls();
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

            // AddSelectedTerrain（Contral 页按钮，委托到视图）
            var selectionActionsContral = new SelectionActionsView(contralRoot);
            selectionActionsContral.Bind(new SelectionActionsView.SelectionActionsCallbacks
            {
                GetSelectionObjects = () => Selection.gameObjects,
                ClearTerrainLists = () => terrainController.ClearTerrainLists(selectedTerrains, terrainListUIData, scannedTerrainNames),
                AddTerrainToLists = t => terrainController.AddTerrainToLists(t, selectedTerrains, terrainListUIData, scannedTerrainNames),
                RefreshStartListUI = () =>
                {
                    if (startRoot != null)
                    {
                        if (startTerrainListView != null) startTerrainListView.Refresh(terrainListUIData);
                        else { startTerrainListView = new TerrainListView(startRoot); startTerrainListView.Refresh(terrainListUIData); }
                    }
                },
                RefreshContralListUI = () => { contralTerrainListView?.Refresh(terrainListUIData); },
                BuildContralSection = null
            });

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

        private void SetTabActive(Button active, Button inactive)
        {
            if (active == null || inactive == null) return;
            // 使用 USS 类控制激活状态，避免内联样式与主题冲突
            active.AddToClassList("mt-tabbutton--active");
            inactive.RemoveFromClassList("mt-tabbutton--active");
        }


        private void LoadPaintingTab()
        {
            if (contralTabContent == null) return;
            contralTabContent.Clear();
            var scroll = new ScrollView();
            if (uxmlVegetationShared != null)
            {
                var vegRoot = InstantiatePage(uxmlVegetationShared);
                scroll.Add(vegRoot);
            }
            var paintRoot = InstantiatePage(uxmlPaint);
            scroll.Add(paintRoot);
            contralTabContent.Add(scroll);
            var paintParam = paintRoot.Q<VisualElement>("PaintParameter") ?? paintRoot;
            BindBrushControls(paintParam);
            contralBindingsInitialized = false;
            BindContralNamedControls();
            // Painting 激活时默认进入绘制模式
            mode = Mode.Paint;
        }

        private void LoadGenerateTab()
        {
            if (contralTabContent == null) return;
            InitializeGenerateTabUI();
            var genRoot = GetGenerateParameterElement();
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

        private void LoadSettingsTab()
        {
            var root = contralTabContent;
            if (root == null) return;
            var settingsUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/MrTerrPainterV1/Editor/MrTerrainPainterSettings.uxml");
            root.Clear();
            VisualElement page = settingsUxml != null ? settingsUxml.Instantiate() : new VisualElement();
            root.Add(page);

            var tfRecipePath = page.Q<TextField>("RecipeGenerationPath");
            if (tfRecipePath != null)
            {
                tfRecipePath.SetValueWithoutNotify(config.recipeGenerationPath);
                tfRecipePath.RegisterValueChangedCallback(e => { config.recipeGenerationPath = e.newValue; EditorUtility.SetDirty(config); });
            }
            var toggleShowPool = page.Q<Toggle>("ShowPool");
            if (toggleShowPool != null)
            {
                toggleShowPool.SetValueWithoutNotify(VegetationPool.ShowInHierarchy);
                toggleShowPool.RegisterValueChangedCallback(e =>
                {
                    VegetationPool.ShowInHierarchy = e.newValue;
                    config.showPoolInHierarchy = e.newValue;
                    EditorUtility.SetDirty(config);
                });
            }

            var fold = page.Q<Foldout>("MappingList");
            if (fold != null)
            {
                var mappingTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/MrTerrPainterV1/Editor/MTPTerrainPainterSettingsMappinger.uxml");
                if (mappingTemplate != null)
                {
                    void Refresh()
                    {
                        fold.Clear();
                        int count = Mathf.Max(config.objectList != null ? config.objectList.Length : 0,
                                              config.objectTypeList != null ? config.objectTypeList.Length : 0);
                        for (int i = 0; i < count; i++)
                        {
                            var rowRoot = mappingTemplate.Instantiate();
                            var mapRoot = rowRoot.Q<VisualElement>("Mapping");
                            var of = mapRoot.Q<ObjectField>("ObjectField");
                            if (of != null)
                            {
                                of.objectType = typeof(Transform);
                                of.allowSceneObjects = true;
                                var initialGo = (config.objectList != null && i < config.objectList.Length) ? config.objectList[i] : null;
                                of.SetValueWithoutNotify(initialGo != null ? initialGo.transform : null);
                                of.RegisterValueChangedCallback(e =>
                                {
                                    var list = config.objectList?.ToList() ?? new System.Collections.Generic.List<GameObject>();
                                    while (i >= list.Count) list.Add(null);
                                    list[i] = (e.newValue as Transform)?.gameObject;
                                    config.objectList = list.ToArray();
                                    EditorUtility.SetDirty(config);
                                });
                            }
                            var typeField = mapRoot.Q<EnumField>("PrefabType");
                            if (typeField != null)
                            {
                                var initialType = (config.objectTypeList != null && i < config.objectTypeList.Length)
                                    ? config.objectTypeList[i]
                                    : config.defaultGenerationType;
                                typeField.Init(initialType);
                                typeField.SetValueWithoutNotify(initialType);
                                typeField.RegisterValueChangedCallback(e =>
                                {
                                    var types = config.objectTypeList?.ToList() ?? new System.Collections.Generic.List<Runtime.Profiles.PrefabType>();
                                    while (i >= types.Count) types.Add(config.defaultGenerationType);
                                    types[i] = (Runtime.Profiles.PrefabType)e.newValue;
                                    config.objectTypeList = types.ToArray();
                                    EditorUtility.SetDirty(config);
                                });
                            }
                            var btnDel = rowRoot.Q<Button>("Delete");
                            if (btnDel != null)
                            {
                                int idx = i;
                                btnDel.clicked += () =>
                                {
                                    if (config.objectList != null && idx < config.objectList.Length)
                                        config.objectList = config.objectList.Where((_, k) => k != idx).ToArray();
                                    if (config.objectTypeList != null && idx < config.objectTypeList.Length)
                                        config.objectTypeList = config.objectTypeList.Where((_, k) => k != idx).ToArray();
                                    EditorUtility.SetDirty(config);
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
                            var list = config.objectList?.ToList() ?? new System.Collections.Generic.List<GameObject>();
                            list.Add(null);
                            config.objectList = list.ToArray();
                            var types = config.objectTypeList?.ToList() ?? new System.Collections.Generic.List<Runtime.Profiles.PrefabType>();
                            types.Add(config.defaultGenerationType);
                            config.objectTypeList = types.ToArray();
                            EditorUtility.SetDirty(config);
                            Refresh();
                        };
                    }
                }
            }
            var ofVegShared = page.Q<ObjectField>("VegetationSharedUXML");
            if (ofVegShared != null)
            {
                ofVegShared.objectType = typeof(VisualTreeAsset);
                ofVegShared.allowSceneObjects = false;
                ofVegShared.SetValueWithoutNotify(config.vegetationSharedUxml);
                ofVegShared.RegisterValueChangedCallback(e => { config.vegetationSharedUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofOverlay = page.Q<ObjectField>("BrushOverlayUXML");
            if (ofOverlay != null)
            {
                ofOverlay.objectType = typeof(VisualTreeAsset);
                ofOverlay.allowSceneObjects = false;
                ofOverlay.SetValueWithoutNotify(config.brushOverlayUxml);
                ofOverlay.RegisterValueChangedCallback(e => { config.brushOverlayUxml = e.newValue as VisualTreeAsset; EditorUtility.SetDirty(config); });
            }

            var ofStyles = page.Q<ObjectField>("StylesUSS");
            if (ofStyles != null)
            {
                ofStyles.objectType = typeof(StyleSheet);
                ofStyles.allowSceneObjects = false;
                ofStyles.SetValueWithoutNotify(config.stylesUss);
                ofStyles.RegisterValueChangedCallback(e => { config.stylesUss = e.newValue as StyleSheet; EditorUtility.SetDirty(config); });
            }

            var btnSave = page.Q<Button>("SaveConfiguration");
            if (btnSave != null)
            {
                btnSave.clicked += () =>
                {
                    ConfigTools.Save(config);
                    EditorUtility.DisplayDialog("已保存", "配置已保存。", "确定");
                };
            }

        }

        /// <summary>
        /// 初始化生成标签页的UI结构
        /// </summary>
        private void InitializeGenerateTabUI()
        {
            contralTabContent.Clear();

            var scroll = new ScrollView();
            if (uxmlVegetationShared != null)
            {
                var vegRoot = InstantiatePage(uxmlVegetationShared);
                scroll.Add(vegRoot);
            }
            var genRoot = InstantiatePage(uxmlGenerate);
            scroll.Add(genRoot);

            contralTabContent.Add(scroll);
            contralBindingsInitialized = false;
            BindContralNamedControls();
        }

        /// <summary>
        /// 获取生成参数的容器元素
        /// </summary>
        public VisualElement GetGenerateParameterElement()
        {
            var genRoot = contralTabContent.Q<ScrollView>()?.Q<VisualElement>();
            return genRoot?.Q<VisualElement>("GenerateParameter") ?? genRoot;
        }



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

            if (contralTerrainListView != null)
            {
                contralTerrainListView.Refresh(terrainListUIData);
            }
            else
            {
                PopulateTerrianListUI(contralRoot);
            }
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
            // 优先在 PrefabRange 容器内查询控件，确保使用新的属性区域
            var prefabRange = contralRoot.Q<VisualElement>("PrefabRange");
            var queryRoot = prefabRange ?? contralRoot;
            uiPreviewPrefabList = contralRoot.Q<VisualElement>("PreviewPrefabList");
            // 构建并挂载 VegetationProfile 列表（统一 Profile 切换入口）
            SetupVegetationProfileList();
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

            // 列表与预览区初始化
            if (uiPreviewPrefabList != null)
            {
                HandlePreviewDragEvents(uiPreviewPrefabList);
                RefreshPreviewListUI();
            }
            contralBindingsInitialized = true;
        }

        // 预览区域拖拽接收：将拖入的 GameObject 作为Prefab添加到当前Profile
        private void HandlePreviewDragEvents(VisualElement dropArea)
        {
            if (dropArea == null) return; // 提前返回
            dropArea.RegisterCallback<DragUpdatedEvent>(e =>
            {
                var refs = DragAndDrop.objectReferences;
                var hasGO = refs != null && refs.Any(o => o is GameObject);
                DragAndDrop.visualMode = (currentProfile != null && hasGO)
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;
                e.StopPropagation();
            });

            dropArea.RegisterCallback<DragPerformEvent>(e =>
            {
                var gos = DragAndDrop.objectReferences?.OfType<GameObject>().ToArray();
                if (gos != null && gos.Length > 0)
                {
                    prefabAssignment?.AddPrefabsToProfile(gos);
                    RefreshPreviewListUI();
                    RefreshVegetationListUI();
                    UpdatePropertyPanelFromSelectedItem();
                }
                DragAndDrop.AcceptDrag();
                e.StopPropagation();
            });
        }

        private readonly System.Collections.Generic.List<Runtime.Profiles.VegetationProfile> availableProfiles = new();

        // 统一构建 VegetationProfile 列表与交互
        private void SetupVegetationProfileList()
        {
            if (contralRoot == null) return;

            // 初始化视图实例
            if (contralView == null)
            {
                contralView = new ContralView(contralRoot, uxmlVegetationProfileRow);
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
                    OpenPrefabPickerForItem = (profile, index) => OpenPrefabPickerForItem(profile, index)
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
            uiPreviewPrefabList.Clear();
            if (currentProfile != null)
            {
                prefabAssignment?.CleanNullPrefabItems(currentProfile);
            }
            var items = GetProfileItemsSnapshot();
            selectedItemIndex = Mathf.Clamp(selectedItemIndex, 0, Mathf.Max(0, items.Count - 1));
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var box = new VisualElement();
                box.AddToClassList("preview-item");
                box.pickingMode = PickingMode.Position;
                var img = new Image();
                img.AddToClassList("preview-item__image");
                Texture2D tex = null;
                if (it != null && it.prefab != null)
                {
                    tex = AssetPreview.GetAssetPreview(it.prefab) ?? AssetPreview.GetMiniThumbnail(it.prefab);
                }
                img.image = tex;
                box.Add(img);
                var index = i;
                box.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        SetSelectedThumbIndex(index);
                        evt.StopPropagation();
                    }
                });
                img.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        SetSelectedThumbIndex(index);
                        evt.StopPropagation();
                    }
                });
                box.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 1)
                    {
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("删除"), false, () =>
                        {
                            prefabAssignment?.RemoveItemAt(index);
                            RefreshVegetationListUI();
                            RefreshPreviewListUI();
                        });
                        var enumValues = System.Enum.GetValues(typeof(MrTerrainPainter.Runtime.Profiles.PrefabType));
                        foreach (MrTerrainPainter.Runtime.Profiles.PrefabType val in enumValues)
                        {
                            var tname = val.ToString();
                            bool isCurrent = it != null && it.prefabType == val;
                            menu.AddItem(new GUIContent($"类型/{tname}"), isCurrent, () =>
                            {
                                prefabAssignment?.SetItemType(currentProfile, index, val);
                                if (currentProfile != null) EditorUtility.SetDirty(currentProfile);
                                SetSelectedThumbIndex(index);
                            });
                        }
                        menu.ShowAsContext();
                        evt.StopPropagation();
                    }
                });
                if (index == selectedItemIndex)
                {
                    box.AddToClassList("preview-item--selected");
                }
                uiPreviewPrefabList.Add(box);
            }
            uiPreviewPrefabList.MarkDirtyRepaint();
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
            if (uiPreviewPrefabList != null)
            {
                var children = uiPreviewPrefabList.Children().ToList();
                for (int ci = 0; ci < children.Count; ci++)
                {
                    var child = children[ci];
                    child.RemoveFromClassList("preview-item--selected");
                    if (ci == index) child.AddToClassList("preview-item--selected");
                }
                uiPreviewPrefabList.MarkDirtyRepaint();
            }
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
    }
}

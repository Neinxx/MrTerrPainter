using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MrTerrainPainter.Editor.Views;
using static MrTerrainPainter.Editor.Services.VegetationGenerator;
using UnityEditor.UIElements;

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
            // 初始化 Contral 页地形列表视图并刷新
            contralTerrainListView = new TerrainListView(contralRoot);
            contralTerrainListView.Refresh(terrainListUIData);
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
                    // 若当前已激活，再次点击则取消激活并清空内容
                    if (btnPainting.ClassListContains("mt-tabbutton--active"))
                    {
                        btnPainting.RemoveFromClassList("mt-tabbutton--active");
                        btnGenerate?.RemoveFromClassList("mt-tabbutton--active");
                        contralTabContent?.Clear();
                        return; // 提前返回：不加载模块
                    }
                    // 设置 Painting 为激活，加载其页面
                    SetTabActive(btnPainting, btnGenerate);
                    contralTabContent?.Clear();
                    LoadPaintingTab();
                };
            if (btnGenerate != null)
                btnGenerate.clicked += () =>
                {
                    if (btnGenerate.ClassListContains("mt-tabbutton--active"))
                    {
                        btnGenerate.RemoveFromClassList("mt-tabbutton--active");
                        btnPainting?.RemoveFromClassList("mt-tabbutton--active");
                        contralTabContent?.Clear();
                        return; // 提前返回：不加载模块
                    }
                    SetTabActive(btnGenerate, btnPainting);
                    contralTabContent?.Clear();
                    LoadGenerateTab();
                };
            if (btnSettings != null)
                btnSettings.clicked += () =>
                {
                    SetTabActive(btnSettings, btnPainting);
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
            var paintRoot = InstantiatePage(uxmlPaint);
            scroll.Add(paintRoot);
            var paintParam = paintRoot.Q<VisualElement>("PaintParameter") ?? paintRoot;
            // 绑定：笔刷设置（强绑定到 brush）
            BindBrushControls(paintParam);
            contralTabContent.Add(scroll);
            // Painting 激活时默认进入绘制模式
            mode = Mode.Paint;
        }

        private void LoadGenerateTab()
        {
            if (contralTabContent == null) return;

            InitializeGenerateTabUI();

            // 绑定过滤控件
            var genParam = GetGenerateParameterElement();
            BindGenerateFilterControls(genParam);

            // 绑定生成与清除按钮事件
            BindGenerateActions(genParam);

            // 绑定地形列表与Scan/Add/Clear到Generate页
            var startActions = new StartActionsView(genParam);
            startActions.BindAll(new StartActionsView.StartActionsCallbacks
            {
                ScanSceneTerrains = () => terrainController.ScanSceneTerrains(terrainListUIData, scannedTerrainNames),
                ClearTerrainUIList = () => terrainController.ClearTerrainUIList(terrainListUIData),
                GetSelectionObjects = () => Selection.gameObjects,
                ClearTerrainLists = () => terrainController.ClearTerrainLists(selectedTerrains, terrainListUIData, scannedTerrainNames),
                AddTerrainToLists = t => terrainController.AddTerrainToLists(t, selectedTerrains, terrainListUIData, scannedTerrainNames),
                RefreshStartListUI = () => { },
                RefreshContralListUI = () =>
                {
                    var genTerrainListView = new TerrainListView(genParam);
                    genTerrainListView.Refresh(terrainListUIData);
                },
                BuildContralSection = null
            });

            mode = Mode.Generate;
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
            var templateRow = fold != null ? fold.Q<VisualElement>("Mapping") : null;
            if (fold != null && templateRow != null)
            {
                int existingCount = Mathf.Max(config.objectList != null ? config.objectList.Length : 0,
                                              config.objectTypeList != null ? config.objectTypeList.Length : 0);
                if (existingCount <= 0) existingCount = 1;
                for (int i = 0; i < existingCount; i++)
                {
                    var row = i == 0 ? templateRow : (settingsUxml != null ? settingsUxml.Instantiate().Q<VisualElement>("Mapping") : null);
                    if (row == null) continue;
                    if (i != 0) { row.RemoveFromHierarchy(); fold.Add(row); }
                    var of = row.Q<ObjectField>("ObjectField");
                    if (of != null)
                    {
                        of.objectType = typeof(Transform);
                        of.allowSceneObjects = true;
                        var initialGo = (config.objectList != null && i < config.objectList.Length) ? config.objectList[i] : null;
                        var initialTf = initialGo != null ? initialGo.transform : null;
                        of.SetValueWithoutNotify(initialTf);
                        of.RegisterValueChangedCallback(e =>
                        {
                            var list = config.objectList?.ToList() ?? new System.Collections.Generic.List<GameObject>();
                            while (i >= list.Count) list.Add(null);
                            list[i] = (e.newValue as Transform)?.gameObject;
                            config.objectList = list.ToArray();
                            EditorUtility.SetDirty(config);
                        });
                    }
                    var typeField = row.Q<EnumField>("PrefabType");
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
                }
                var btnAdd = page.Q<Button>("Add");
                var btnDelete = page.Q<Button>("Delete");
                if (btnAdd != null)
                {
                    btnAdd.clicked += () =>
                    {
                        var tree = settingsUxml != null ? settingsUxml.Instantiate() : null;
                        var row = tree != null ? tree.Q<VisualElement>("Mapping") : null;
                        if (row == null) return;
                        row.RemoveFromHierarchy();
                        fold.Add(row);
                        var list = config.objectList?.ToList() ?? new System.Collections.Generic.List<GameObject>();
                        list.Add(null);
                        config.objectList = list.ToArray();
                        var types = config.objectTypeList?.ToList() ?? new System.Collections.Generic.List<Runtime.Profiles.PrefabType>();
                        types.Add(config.defaultGenerationType);
                        config.objectTypeList = types.ToArray();
                        EditorUtility.SetDirty(config);
                    };
                }
                if (btnDelete != null)
                {
                    btnDelete.clicked += () =>
                    {
                        var rows = fold.Query<VisualElement>("Mapping").ToList();
                        if (rows.Count == 0) return;
                        var last = rows[rows.Count - 1];
                        fold.Remove(last);
                        if (config.objectList != null && config.objectList.Length > 0)
                            config.objectList = config.objectList.Take(config.objectList.Length - 1).ToArray();
                        if (config.objectTypeList != null && config.objectTypeList.Length > 0)
                            config.objectTypeList = config.objectTypeList.Take(config.objectTypeList.Length - 1).ToArray();
                        EditorUtility.SetDirty(config);
                    };
                }
            }
        }

        /// <summary>
        /// 初始化生成标签页的UI结构
        /// </summary>
        private void InitializeGenerateTabUI()
        {
            contralTabContent.Clear();

            var scroll = new ScrollView();
            var genRoot = InstantiatePage(uxmlGenerate);
            scroll.Add(genRoot);

            contralTabContent.Add(scroll);
        }

        /// <summary>
        /// 获取生成参数的容器元素
        /// </summary>
        private VisualElement GetGenerateParameterElement()
        {
            var genRoot = contralTabContent.Q<ScrollView>()?.Q<VisualElement>();
            return genRoot?.Q<VisualElement>("GenerateParameter") ?? genRoot;
        }

        /// <summary>
        /// 绑定生成和清除按钮的事件处理
        /// </summary>
        private void BindGenerateActions(VisualElement parent)
        {
            var actionsView = new GenerateActionsView(parent);
            actionsView.Bind(
                onGenerate: HandleGenerateAction,
                onClear: HandleClearAction
            );
        }

        /// <summary>
        /// 处理生成植被的逻辑
        /// </summary>
        private void HandleGenerateAction()
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
        private void HandleClearAction()
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
        private void BindGenerateFilterControls(VisualElement root)
        {
            if (root == null) return; // 提前返回
            generateFilterView = new GenerateFilterView(root);
            generateFilterView.Bind(noise);
        }

        // —— 绑定：Paint 页笔刷控件 ——
        private void BindBrushControls(VisualElement root)
        {
            if (root == null) return; // 提前返回
            brushView = new BrushView(root);
            brushView.Bind(brush);
        }

        private void BindContralNamedControls()
        {
            if (contralRoot == null) return;
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
    }
}

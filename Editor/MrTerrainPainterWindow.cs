using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Services;
using MrTerrainPainter.Editor.Controllers;
using MrTerrainPainter.Editor.State;
using MrTerrainPainter.Editor.Views;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using MrTerrainPainter.Editor.Utils;

namespace MrTerrainPainter.Editor
{
    public partial class MrTerrainPainterWindow : EditorWindow
    {
        private enum Page { Start, Contral, Generate, Paint }
        private enum Mode { Generate, Paint, Erase }

        public readonly List<Terrain> selectedTerrains = new();
        private VegetationProfile currentProfile;
        private Mode mode = Mode.Generate;

        private readonly BrushSettings brush = new();
        private System.Random rnd;
        private readonly VegetationGenerator.NoiseSettings noise = new();

        // 窗口级自定义范围（覆盖 Profile SO 范围）
        private Vector2 customScaleRange = new(1f, 1f);
        // 弱化默认旋转效果：窗口级默认旋转范围 0..30 度
        private Vector2 customYRotationRange = new(0f, 30f);
        private Vector2 customHeightRange = new(0f, 1000f);
        private Vector2 customSlopeRange = new(0f, 90f);

        // 多配方支持（用于批量生成）
        private readonly List<VegetationProfile> extraProfiles = new();

        // 配方条目 UI 状态
        private int selectedItemIndex = -1;
        // 预制体缩略图多选集合（当前 Profile 范围内）
        private readonly HashSet<int> selectedThumbIndices = new();

        public MrTerrainPainterConfig config;

        // 模块化：控制器与状态
        private EditorState editorState;
        private IRefreshController refreshController;
        private IPrefabPickerController prefabPicker;
        public TerrainController terrainController;
        private PrefabAssignmentController prefabAssignment;
        private ProfileController profileController;
        private PaintingController paintingController;
        // 视图：Contral 页列表视图
        private ContralView contralView;
        // 视图：Contral 页属性面板视图
        private PropertyPanelView propertyPanelView;
        // 视图：地形列表（Start/Contral 页）
        private TerrainListView startTerrainListView;
        // 视图：Paint/Generate 页模块化视图
        private BrushView brushView;
        private GenerateFilterView generateFilterView;

        // UI Toolkit: 资源与实例
        private VisualTreeAsset uxmlStart;
        private VisualTreeAsset uxmlContral;
        private VisualTreeAsset uxmlGenerate;
        private VisualTreeAsset uxmlPaint;
        private VisualTreeAsset uxmlVegetationShared;
        private VisualTreeAsset uxmlVegetationProfileRow; // VegetationProfile 列表行模板（UXML）
        private VisualTreeAsset uxmlVegetationProfilePrefabIcon; // 预制体缩略图图标（UXML）
        private VisualTreeAsset uxmlVegetationProfileDraggableArea; // 可拖拽新建区域（UXML）
        private VisualElement pageContainer;
        private VisualElement startRoot;
        private VisualElement contralRoot;

        private VisualElement contralTabContent;
        private Page page = Page.Start;
        private bool refreshingUI;
        private bool contralBindingsInitialized;

        // Contral 页面命名控件绑定

        private readonly ObjectField uiSelectPrefab;
        private readonly Slider uiWeigth;
        private readonly MinMaxSlider uiSceleRange;
        private readonly MinMaxSlider uiYrotationRange;
        private readonly MinMaxSlider uiHeigthRange;
        private readonly MinMaxSlider uiSlopeRange;
        private readonly Slider uiBaseDensity;
        private readonly Slider uiMinimumSpacing;
        private ListView uiVegetationList;
        private VisualElement uiPreviewPrefabList;
        private ListView uiPreviewListView;
        private readonly System.Collections.Generic.Dictionary<int, Texture2D> previewTexCache = new();
        private float vegetationListContentWidth = 600f; // 列表内容区域宽度缓存，用于行高估算
        private GameObject currentPrefab; // 当前选中的预制体（用于交互与显示）

        // PrefabRange SO 选择与操作
        // 移除 PrefabRangeSO 相关控件与状态（改回使用 PrefabRange 节点下的属性控件）

        [MenuItem("Tools/Mr Terrain Painter Main")]
        public static void Open()
        {
            GetOrOpen();
        }

        public static bool TryGet(out MrTerrainPainterWindow window)
        {
            window = null;
            if (EditorWindow.HasOpenInstances<MrTerrainPainterWindow>())
            {
                // 仅在已打开时检索现有实例，避免隐式创建
                window = Resources.FindObjectsOfTypeAll<MrTerrainPainterWindow>().FirstOrDefault();
            }
            return window != null;
        }

        public static MrTerrainPainterWindow GetOrOpen()
        {
            var win = GetWindow<MrTerrainPainterWindow>(false, "Mr Terrain Painter");
            win.Show();
            return win;
        }



        // 重新扫描并刷新 VegetationProfile 列表与相关 UI
        private void ReloadAvailableProfiles()
        {
            ScanVegetationProfiles();
            ValidateCurrentProfile();
            PruneExtraProfiles();
            RefreshProfileUI();
        }

        private void ScanVegetationProfiles()
        {
            availableProfiles?.Clear();
            var guids = AssetDatabase.FindAssets("t:VegetationProfile");
            for (int gi = 0; gi < guids.Length; gi++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[gi]);
                var asset = AssetDatabase.LoadAssetAtPath<VegetationProfile>(path);
                if (asset != null) availableProfiles.Add(asset);
            }
        }

        private void ValidateCurrentProfile()
        {
            if (currentProfile == null || !availableProfiles.Contains(currentProfile))
            {
                currentProfile = availableProfiles.Count > 0 ? availableProfiles[0] : null;
            }
        }

        private void PruneExtraProfiles()
        {
            if (extraProfiles == null) return;
            for (int i = extraProfiles.Count - 1; i >= 0; i--)
            {
                if (extraProfiles[i] == null) extraProfiles.RemoveAt(i);
            }
        }

        private void RefreshProfileUI()
        {
            if (uiVegetationList != null)
            {
                uiVegetationList.itemsSource = availableProfiles;
                uiVegetationList.Rebuild();
            }
            selectedThumbIndices.Clear();
            RefreshVegetationListUI();
            RefreshPreviewListUI();
            UpdatePropertyPanelFromSelectedItem();
        }

        private void OnProjectChangedRefreshProfiles()
        {
            ReloadAvailableProfiles();
        }

        private void OnEnable()
        {
            // 先注销，防止重复注册
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;

            EditorApplication.projectChanged -= OnProjectChangedRefreshProfiles;
            EditorApplication.projectChanged += OnProjectChangedRefreshProfiles;
            if (config == null)
            {
                config = ConfigTools.LoadOrCreateAsset();
            }
            brush.size = config.defaultBrushSize;
            brush.strength = config.defaultBrushStrength;
            brush.densityScale = config.defaultBrushDensityScale;
            brush.hardness = config.defaultBrushHardness;
            brush.preview = config.showPreview;

            // 应用配置到运行时状态
            VegetationPool.ShowInHierarchy = config.showPoolInHierarchy;
            // 移除旧设置页的对象列表同步逻辑（独立窗口管理，不在主窗口维护）

            uxmlStart = ConfigTools.GetStartUxml(config);
            uxmlContral = ConfigTools.GetControlUxml(config);
            uxmlGenerate = ConfigTools.GetGenerateUxml(config);
            uxmlPaint = ConfigTools.GetPaintUxml(config);
            uxmlVegetationShared = ConfigTools.GetVegetationSharedUxml(config);
            uxmlVegetationProfileRow = ConfigTools.GetVegetationProfileRowUxml(config);
            uxmlVegetationProfilePrefabIcon = ConfigTools.GetPrefabIconUxml(config);
            uxmlVegetationProfileDraggableArea = ConfigTools.GetDraggableAreaUxml(config);

            // 预加载 Profile 列表，确保后续页面构建有数据来源
            ReloadAvailableProfiles();

            // 初始化模块化状态与控制器
            editorState ??= new EditorState();
            refreshController = new RefreshController(
                editorState,
                RefreshVegetationListUI,
                RefreshPreviewListUI,
                UpdatePropertyPanelFromSelectedItem
            );
            // 预制体赋值控制器：集中处理新增/赋值/删除等业务逻辑
            prefabAssignment = new PrefabAssignmentController(
                refreshController,
                () => currentProfile,
                () => selectedItemIndex,
                i => selectedItemIndex = i,
                selectedThumbIndices
            );
            // 对象选择器桥接到控制器
            prefabPicker = new PrefabPickerController(
                (profile, prefab) => prefabAssignment.AddPrefabAsNewItem(profile, prefab),
                (profile, index, prefab) => prefabAssignment.AssignPrefabToItem(profile, index, prefab)
            );
            terrainController = new TerrainController();
            profileController = new ProfileController();
            paintingController = new PaintingController();

            // 构建UI Toolkit界面
            CreateGUI();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.projectChanged -= OnProjectChangedRefreshProfiles;
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.projectChanged -= OnProjectChangedRefreshProfiles;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            if (!Editor.Utils.PageAssembler.EnsureStylesAndValidate(config, root, out var reason)) return;

            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 4;
            root.style.paddingBottom = 4;
            root.Clear();

            pageContainer = new ScrollView();
            //  pageContainer.mode = ScrollViewMode.Vertical;
            //  pageContainer.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            pageContainer.style.flexGrow = 1;
            pageContainer.contentContainer.style.paddingRight = 16;
            root.Add(pageContainer);

            startRoot = PageAssembler.Assemble(pageContainer, uxmlStart);
            startRoot.AddToClassList("mt-frame");
            SetupStartPageEvents();
        }

        private VisualElement InstantiatePage(VisualTreeAsset vta)
        {
            if (vta == null)
            {
                var fallback = new VisualElement();
                fallback.Add(new Label("未找到UXML文件"));
                return fallback;
            }
            return vta.Instantiate();
        }






        public void GeneratePrefabsAtNodeByTypePublic(Transform parentNode, Runtime.Profiles.PrefabType type)
        {
            if (parentNode == null || currentProfile == null) return; // 提前返回
            var items = currentProfile.Items.Where(i => i != null && i.prefab != null && i.prefabType == type).ToList();
            if (items.Count == 0) return; // 提前返回
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            foreach (var (item, idx) in items.Select((v, i) => (v, i)))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab);
                if (go == null) continue;
                Undo.RegisterCreatedObjectUndo(go, "Generate Prefab");
                Undo.SetTransformParent(go.transform, parentNode, "Generate Prefab");
                go.transform.localPosition = Vector3.zero;
            }
            Undo.CollapseUndoOperations(group);
        }

        public VegetationProfile GetCurrentProfile() => currentProfile;











        /* ---------- 行模板 ---------- */
        private const float ThumbSize = 64;
        private const float ThumbGap = 8;



        // Prefab 选择器：新增条目用
        private void OpenPrefabPickerForNewItem(VegetationProfile profile)
        {
            if (profile == null) return; // 提前返回
            prefabPicker?.OpenForNew(profile);
        }

        /* ---------- 纯工具 ---------- */
        private int ThumbRows(int count)
        {
            int perRow = Mathf.Max(1, Mathf.FloorToInt(vegetationListContentWidth / (ThumbSize + ThumbGap)));
            return Mathf.CeilToInt(count / (float)perRow);
        }






        // 刷新所有相关 UI，减少重复调用

        private void RefreshAllUI()
        {
            if (refreshController != null)
            {
                refreshController.RefreshAllUI();
                return;
            }
            RefreshVegetationListUI();
            RefreshPreviewListUI();
            UpdatePropertyPanelFromSelectedItem();
        }

        private void OpenPrefabPickerForItem(VegetationProfile profile, int index)
        {
            if (profile == null || index < 0 || index >= profile.Items.Count) return; // 提前返回
            prefabPicker?.OpenForItem(profile, index);
        }

        // 在 EditorWindow 的 IMGUI 循环中处理对象选择器事件
        private void OnGUI()
        {
            var cmd = Event.current.commandName;
            // 仅在关闭时处理，避免 Updated 与 Closed 双重触发造成重复添加
            if (cmd == "ObjectSelectorClosed")
            {
                prefabPicker?.HandleObjectPickerClosed();
            }
        }

        // —— Profile SO 资产操作 ——
        private string DataFolderPath => !string.IsNullOrEmpty(config?.recipeGenerationPath)
            ? config.recipeGenerationPath
            : "Assets/MrTerrainPainter/Data";

        private void EnsureDataFolderExists()
        {
            profileController?.EnsureDataFolderExists(DataFolderPath);
        }

        private void CreateNewVegetationProfileAsset()
        {
            var profile = profileController?.CreateNewVegetationProfileAsset(DataFolderPath);
            currentProfile = profile;
            ReloadAvailableProfiles();
        }

        private void DeleteVegetationProfileAsset(VegetationProfile profile)
        {
            if (profile == null) return;
            bool confirm = EditorUtility.DisplayDialog("确认删除Profile",
                $"确定删除 Profile: {profile.name} ？此操作不可撤销。",
                "删除", "取消");
            if (!confirm) return;
            profileController?.DeleteVegetationProfileAsset(profile);
            ReloadAvailableProfiles();
        }

        private void SetListSelectionToCurrentProfile()
        {
            // 移除 SO 项高亮：不再设置选中索引，始终清空选择
            if (uiVegetationList == null) return; // 提前返回
                                                  // 清空选择以避免 ListView 默认选中样式（蓝色高亮）
            uiVegetationList.selectedIndex = -1;
            uiVegetationList.ClearSelection();
        }

        private List<VegetationItem> GetProfileItemsSnapshot()
        {
            if (currentProfile == null) return new List<VegetationItem>();
            return new List<VegetationItem>(currentProfile.Items);
        }

        private VegetationItem GetSelectedItem()
        {
            if (currentProfile == null) return null;
            var items = currentProfile.Items;
            if (items.Count == 0) return null;          // 空列表
            if (selectedItemIndex < 0 || selectedItemIndex >= items.Count) return null;
            return items[selectedItemIndex];
        }











        private class ThumbsDragHandlers
        {
            public EventCallback<DragUpdatedEvent> onUpdate;
            public EventCallback<DragPerformEvent> onPerform;
        }

        private void ScanSceneTerrains()
        {
            terrainController?.ScanSceneTerrains(terrainListUIData, scannedTerrainNames);
        }

        public readonly List<string> scannedTerrainNames = new();
        // 用于 UI 展示的地形引用列表（Foldout 子节点 ObjectField）
        public readonly List<Terrain> terrainListUIData = new();



        private void OnSceneGUI(SceneView sv)
        {
            if (UnityEditor.EditorTools.ToolManager.activeToolType == typeof(Tools.MTPBrushTool)) return;
            EnsureRandom();
            var e = Event.current;
            HandleLayoutControl(e);
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Terrain hitTerrain = null;
            Vector3 hitPos = Vector3.zero;
            Vector3 hitNormal = Vector3.up;
            bool hasHit = terrainController != null && terrainController.TryGetTerrainHit(ray, out hitTerrain, out hitPos, out hitNormal);
            RenderBrushPreview(hasHit, hitPos, hitNormal, e);
            if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            {
                if (!hasHit) return;
                if (mode == Mode.Generate)
                {
                    HandleGenerateMouse(e, hitPos);
                    return;
                }
                if (mode == Mode.Paint)
                {
                    HandlePaintMouse(e, hitTerrain, hitPos);
                    return;
                }
            }
        }

        private void HandleLayoutControl(Event e)
        {
            if (mode == Mode.Paint || (mode == Mode.Generate && e.shift))
            {
                if (e.type == EventType.Layout)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                }
            }
        }

        private void RenderBrushPreview(bool hasHit, Vector3 hitPos, Vector3 hitNormal, Event e)
        {
            if (e.type != EventType.Repaint) return;
            if (!hasHit || mode != Mode.Paint) return;
            BrushPainter.DrawPreview(hitPos, hitNormal, brush);
        }

        private void HandleGenerateMouse(Event e, Vector3 hitPos)
        {
            if (!e.shift) return;
            if (selectedTerrains.Count > 0 && currentProfile != null)
            {
                var filter = BuildFilterSettings();
                var ov = BuildPlacementOverrides();
                VegetationGenerator.GenerateInBrushArea(selectedTerrains, currentProfile, hitPos, brush.size, filter, ov);
                for (int i = 0; i < extraProfiles.Count; i++)
                {
                    var p = extraProfiles[i];
                    if (p == null || p.IsEmpty()) continue;
                    VegetationGenerator.GenerateInBrushArea(selectedTerrains, p, hitPos, brush.size, filter, ov);
                }
                MarkSceneDirty();
            }
            if (e.button != 2) e.Use();
        }

        private void HandlePaintMouse(Event e, Terrain hitTerrain, Vector3 hitPos)
        {
            var terrain = hitTerrain != null ? hitTerrain : (terrainController != null ? terrainController.NearestTerrain(hitPos, selectedTerrains) : null);
            if (terrain != null)
            {
                if (e.button == 1)
                {
                    BrushPainter.Erase(terrain, hitPos, brush, eraseAll: true);
                    MarkSceneDirty();
                }
                else if (e.button == 0)
                {
                    VegetationPainterOnTerrain(terrain, hitPos);
                }
            }
            if (e.button != 2) e.Use();
        }

        private bool TryGetTerrainHit(Ray ray, out Terrain terrain, out Vector3 pos, out Vector3 normal)
        {
            if (terrainController == null)
            {
                terrain = null;
                pos = Vector3.zero;
                normal = Vector3.up;
                return false;
            }
            return terrainController.TryGetTerrainHit(ray, out terrain, out pos, out normal);
        }

        private void VegetationPainterOnTerrain(Terrain terrain, Vector3 center)
        {
            if (terrain == null || currentProfile == null || currentProfile.IsEmpty()) return;
            var ov = BuildPlacementOverrides();
            paintingController?.PaintOnTerrain(terrain, center, currentProfile, extraProfiles, brush, rnd, ov, brush.mixExtraProfiles);
            MarkSceneDirty();
        }

        private Terrain NearestTerrain(Vector3 pos)
        {
            return terrainController != null ? terrainController.NearestTerrain(pos, selectedTerrains) : null;
        }

        private void EnsureRandom()
        {
            if (rnd == null)
            {
                int seed = currentProfile != null ? currentProfile.randomSeed : 12345;
                rnd = new System.Random(seed);
            }
        }

        private VegetationGenerator.FilterSettings BuildFilterSettings()
        {
            var filter = new VegetationGenerator.FilterSettings();
            filter.noise = noise ?? new VegetationGenerator.NoiseSettings();
            return filter;
        }

        private VegetationGenerator.PlacementOverrides BuildPlacementOverrides()
        {
            return new VegetationGenerator.PlacementOverrides
            {
                scaleRange = customScaleRange,
                yRotationRange = customYRotationRange,
                heightRange = customHeightRange,
                slopeRange = customSlopeRange
            };
        }




        private void OnLostFocus()
        {
            // if (config != null && config.switchToGenerateOnLostFocus)
            // {
            //     // 若控制页尚未构建，先构建
            //     if (contralRoot == null)
            //     {
            //         BuildContralSection();
            //     }
            //     // 切换到 Generate 标签并高亮，但允许手动继续绘制
            //     //  LoadGenerateTab();
            //     LoadPaintingTab();
            //     var btnPainting = contralRoot?.Q<Button>("Painting");
            //     var btnGenerate = contralRoot?.Q<Button>("Generate");
            //     if (btnPainting != null && btnGenerate != null)
            //     {
            //         SetTabActive(btnGenerate, btnPainting);
            //     }
            // }
        }

        private void MarkSceneDirty()
        {
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }
    }

}

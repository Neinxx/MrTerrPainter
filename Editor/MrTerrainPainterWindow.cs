using System;
using System.Collections.Generic;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.State;
using MrTerrainPainter.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor
{
    public partial class MrTerrainPainterWindow : EditorWindow
    {
        // --- 1. 静态访问 ---
        private static MrTerrainPainterWindow s_Current;

        public static bool TryGet(out MrTerrainPainterWindow window)
        {
            window = s_Current;
            return window != null;
        }

        public static MrTerrainPainterWindow GetOrOpen()
        {
            s_Current = GetWindow<MrTerrainPainterWindow>(false, "Mr Terrain Painter");
            s_Current.Show();
            return s_Current;
        }

        [MenuItem("Window/Mr Terrain Painter/Open Painting Settings", priority = 2001)]
        public static void OpenPaintingSettingsMenu()
        {
            var win = GetOrOpen();
            win.rootVisualElement.schedule.Execute(() => win.OpenPaintingSettings());
        }

        // --- 2. 核心依赖 ---
        public MrTerrainPainterConfig config;
        internal PainterSession session;
        public PainterSession Session => session;

        // --- 3. UI 状态字段 ---
        public enum TabType { Paint, Generate, Settings }
        private TabType currentTab = TabType.Paint; // 默认状态

        private VisualElement pageContainer;
        private VisualElement controlTabContent;

        // 必须声明为类字段，Start.cs 才能访问
        internal VisualElement startRoot;

        public static event Action<bool, bool, bool> WindowStateChanged;

        // --- 4. 兼容性代理属性 ---
        public Controllers.TerrainController terrainController => session?.TerrainController;
        public List<Terrain> SelectedTerrains => session?.SelectedTerrains;
        public List<Terrain> TerrainListUIData => session?.TerrainListUIData;
        public List<string> ScannedTerrainNames => session?.ScannedTerrainNames;

        private void OnEnable()
        {
            s_Current = this;
            if (config == null) config = ConfigTools.LoadOrCreateAsset();

            InitializeSession();
            InitializeUI();

            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.projectChanged += OnProjectChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
            ConfigTools.ConfigUpdated += OnConfigUpdatedFromExternal;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.projectChanged -= OnProjectChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnSceneOpened;
            ConfigTools.ConfigUpdated -= OnConfigUpdatedFromExternal;
            session?.Dispose();
            WindowStateChanged?.Invoke(false, false, false);
            VegetationPool.ClearAllIndexes();
            s_Current = null;
        }

        private void InitializeSession()
        {
            session = new PainterSession { Config = config };
            session.ApplyConfigDefaults();

            session.InitializeControllers(
                onRefreshList: RefreshProfileListUI,
                onRefreshPreview: RefreshPreviewUI,
                onUpdateProperties: UpdatePropertyPanel,
                isGenerateMode: () => currentTab == TabType.Generate,
                isPaintMode: () => currentTab == TabType.Paint,
                findNearestTerrain: (pos) => session.TerrainController.NearestTerrain(pos, session.SelectedTerrains),
                markSceneDirty: EditorSceneUtils.MarkSceneDirty
            );

            Tools.MTPBrushContext.SetSharedBrush(session.Brush);
            Tools.MTPBrushContext.SetConfig(config);
        }

        private void InitializeUI()
        {
            var root = rootVisualElement;
            if (!PageAssembler.EnsureStylesAndValidate(config, root, out _)) return;
            root.Clear();

            pageContainer = new ScrollView();
            pageContainer.AddToClassList("mt-scroll");
            root.Add(pageContainer);

            var startUxml = ConfigTools.GetStartUxml(config);
            startRoot = PageAssembler.Assemble(pageContainer, startUxml);
            startRoot.AddToClassList("mt-frame");

            SetupStartPageEvents();
            BuildControlLayout();
        }

        private void OnGUI()
        {
            if (Event.current?.commandName == "ObjectSelectorClosed")
                session?.PrefabPicker?.HandleObjectPickerClosed();
        }

        private void OnSceneGUI(SceneView sv) => session?.SceneService?.OnSceneGUI();

        private void OnProjectChanged()
        {
            session?.ReloadAvailableProfiles();
            Editor.Services.BrushPainter.ClearCache();
            RefreshAllUI();
        }

        private void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode)
        {
            VegetationPool.ClearAllIndexes();
            Editor.Services.BrushPainter.ClearCache();
        }
        // 处理外部（如独立设置窗口）修改配置后的刷新
        private void OnConfigUpdatedFromExternal()
        {
            // 1. 重新加载配置资产
            config = ConfigTools.LoadOrCreateAsset();

            // 2. 更新 Session 中的配置
            if (session != null)
            {
                session.Config = config;
                session.ApplyConfigDefaults();
                session.ReloadAvailableProfiles(); // 可能路径变了，重新扫描 Profile
            }
            else
            {
                InitializeSession();
            }

            // 3. 强制刷新 UI
            // 使用 schedule 避免布局冲突
            rootVisualElement.schedule.Execute(() =>
            {
                // 如果配置变完整了，自动切回 Paint 页
                if (currentTab == TabType.Settings && ConfigTools.IsComplete(config, out _))
                {
                    SwitchToTab(TabType.Paint);
                }
                else
                {
                    // 否则刷新当前页
                    SwitchToTab(currentTab);
                }

                // 刷新 Overlay 状态
                CelebrateMappingCompleted(); // 如果 Mapping 修复了，更新 Start 页状态
            });
        }

        // --- 5. 公共 API ---

        public void HandleGenerateAction() => session?.HandleGenerateAction();
        public void HandleClearAction() => session?.HandleClearAction();

        public void OpenPaintingSettings()
        {
            // 确保进入绘画模式前，配置是最新的
            session?.ApplyConfigDefaults();
            SwitchToTab(TabType.Paint);
        }

        // 供 Start.cs 使用
        public void OpenSettingsTab() => SwitchToTab(TabType.Settings);

        public Runtime.Profiles.VegetationProfile GetCurrentProfile() => session?.CurrentProfile;

        public void SetCurrentProfilePublic(Runtime.Profiles.VegetationProfile profile)
        {
            session?.SetCurrentProfile(profile);
            RefreshAllUI();
            SceneView.RepaintAll();
        }

        public void OnConfigurationCompleted()
        {
            // 1. 关键修复：强制将最新的 Config 同步到 Session 和 BrushContext
            session?.ApplyConfigDefaults();

            // 2. 重新加载 Profile (防止 Profile 引用的 Config 数据过时)
            session?.ReloadAvailableProfiles();

            // 3. 切换回绘画页
            SwitchToTab(TabType.Paint);

            // 4. 提示用户
            Debug.Log("[MTP] Configuration synced successfully.");
        }

        // --- Overlay 需要的状态查询方法 ---
        public bool IsPaintingModePublic() => currentTab == TabType.Paint;

        // *新增*: 修复 Overlay 报错
        public bool IsSettingsOpenPublic() => currentTab == TabType.Settings;

    }
}
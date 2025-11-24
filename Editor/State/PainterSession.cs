using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Controllers;
using MrTerrainPainter.Editor.Services;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using PrefabType = MrTerrainPainter.Runtime.Profiles.PrefabType;

namespace MrTerrainPainter.Editor.State
{
    /// <summary>
    /// 全局会话状态管理，作为 Window、Controller 和 View 的数据中枢。
    /// </summary>
    public class PainterSession : IDisposable
    {
        #region Core Data (配置与核心数据)

        public MrTerrainPainterConfig Config { get; set; }

        // 笔刷设置
        public BrushSettings Brush { get; } = new();

        // --- [关键修复] 数据源一致性 ---
        // Noise 作为独立对象存在，GenFilter.noise 必须始终指向这个对象
        public VegetationGenerator.NoiseSettings Noise { get; } = new();
        public VegetationGenerator.FilterSettings GenFilter { get; private set; } = new();

        // 地形数据
        public List<Terrain> SelectedTerrains { get; } = new();

        // UI 专用的地形列表数据（解耦 View 与 Window）
        public List<Terrain> TerrainListUIData { get; } = new();
        public List<string> ScannedTerrainNames { get; } = new();

        // Profile 数据
        public List<VegetationProfile> AvailableProfiles { get; } = new();
        public VegetationProfile CurrentProfile { get; private set; }

        #endregion

        #region UI State (UI 瞬时状态)

        /// <summary>
        /// 封装 UI 相关的临时状态，替代原 Window 中的字段
        /// </summary>
        public class UIStateContainer
        {
            // 列表选中状态
            public int SelectedItemIndex { get; set; } = -1;
            public HashSet<int> SelectedThumbIndices { get; } = new();
            public float ListContentWidth { get; set; } = 600f;

            // 自定义生成范围（覆盖 Profile 设置）
            public Vector2 CustomScaleRange { get; set; } = new(1f, 1f);
            public Vector2 CustomYRotationRange { get; set; } = new(0f, 30f);
            public Vector2 CustomHeightRange { get; set; } = new(0f, 1000f);
            public Vector2 CustomSlopeRange { get; set; } = new(0f, 90f);

            // 辅助方法
            public bool IsThumbSelected(int index) => SelectedThumbIndices.Contains(index);
            public void AddThumbSelection(int index) => SelectedThumbIndices.Add(index);
            public void RemoveThumbSelection(int index) => SelectedThumbIndices.Remove(index);
            public void ClearThumbSelection() => SelectedThumbIndices.Clear();
            public List<int> GetSelectedThumbIndices() => SelectedThumbIndices.ToList();

            public void ClearSelection()
            {
                SelectedItemIndex = -1;
                SelectedThumbIndices.Clear();
            }
        }

        public UIStateContainer UIState { get; } = new();

        #endregion

        #region Controllers (控制器)

        public EditorState EditorState { get; private set; }
        public IRefreshController RefreshController { get; private set; }
        public IPrefabPickerController PrefabPicker { get; private set; }
        public TerrainController TerrainController { get; private set; }
        public PrefabAssignmentController PrefabAssignment { get; private set; }
        public ProfileController ProfileController { get; private set; }
        public PaintingController PaintingController { get; private set; }
        public SceneInteractionService SceneService { get; private set; }

        #endregion

        #region Private Fields

        private IFilterStrategy _filterStrategy;
        private IPlacementOverrideStrategy _placementStrategy;
        private System.Random _rnd;

        #endregion

        #region Initialization

        /// <summary>
        /// 初始化所有控制器并建立依赖关系
        /// </summary>
        public void InitializeControllers(
            Action onRefreshList,
            Action onRefreshPreview,
            Action onUpdateProperties,
            Func<bool> isGenerateMode,
            Func<bool> isPaintMode,
            Func<Vector3, Terrain> findNearestTerrain,
            Action markSceneDirty)
        {
            EditorState = new EditorState();

            // 1. [关键修复] 确保 FilterSettings 引用正确的 Noise 对象
            // 这样 UI 修改 Session.Noise 时，GenFilter.noise 也会同步变化
            if (GenFilter.noise == null || GenFilter.noise != Noise)
            {
                GenFilter.noise = Noise;
            }

            // 2. 初始化控制器
            RefreshController = new RefreshController(
                EditorState,
                onRefreshList,
                onRefreshPreview,
                onUpdateProperties
            );

            PrefabAssignment = new PrefabAssignmentController(
                RefreshController,
                () => CurrentProfile,
                () => UIState.SelectedItemIndex,
                i => UIState.SelectedItemIndex = i,
                UIState.SelectedThumbIndices
            );

            PrefabPicker = new PrefabPickerController(
                (profile, prefab) => PrefabAssignment.AddPrefabAsNewItem(profile, prefab),
                (profile, index, prefab) => PrefabAssignment.AssignPrefabToItem(profile, index, prefab)
            );

            TerrainController = new TerrainController();
            ProfileController = new ProfileController();
            PaintingController = new PaintingController();

            // 3. 初始化策略
            _filterStrategy = new DefaultFilterStrategy(Noise);

            _placementStrategy = new DefaultPlacementOverrideStrategy(
                () => UIState.CustomScaleRange,
                () => UIState.CustomYRotationRange,
                () => UIState.CustomHeightRange,
                () => UIState.CustomSlopeRange
            );

            // 4. 初始化场景交互服务
            SceneService = new SceneInteractionService(
                TerrainController,
                PaintingController,
                () => CurrentProfile,
                () => SelectedTerrains,
                Brush,
                _filterStrategy,
                _placementStrategy,
                isGenerateMode,
                isPaintMode,
                markSceneDirty,
                findNearestTerrain,
                EnsureRandom,
                false,
                () => CachedFacadePaths
            );
        }

        public void ApplyConfigDefaults()
        {
            if (Config == null) return;

            Brush.size = Config.defaultBrushSize;
            Brush.strength = Config.defaultBrushStrength;
            Brush.densityScale = Config.defaultBrushDensityScale;
            Brush.hardness = Config.defaultBrushHardness;
            Brush.preview = Config.showPreview;

            // 同步到全局上下文
            Tools.MTPBrushContext.SetSharedBrush(Brush);
            Tools.MTPBrushContext.SetConfig(Config);

            VegetationPool.ShowInHierarchy = Config.showPoolInHierarchy;
            VegetationPool.ApplyShowInHierarchyAll();
        }

        #endregion

        #region Logic Methods (业务逻辑)

        public void ReloadAvailableProfiles()
        {
            AvailableProfiles.Clear();
            var guids = AssetDatabase.FindAssets("t:VegetationProfile");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<VegetationProfile>(path);
                if (asset != null) AvailableProfiles.Add(asset);
            }

            // 确保当前 Profile 有效
            if (CurrentProfile == null || !AvailableProfiles.Contains(CurrentProfile))
            {
                CurrentProfile = AvailableProfiles.FirstOrDefault();
            }

            // 同步到工具上下文
            Tools.MTPBrushContext.PruneExtrasNulls();
            Tools.MTPBrushContext.CurrentProfile = CurrentProfile;
        }

        public void SetCurrentProfile(VegetationProfile profile)
        {
            CurrentProfile = profile;
            Tools.MTPBrushContext.CurrentProfile = profile;
        }

        public System.Random EnsureRandom()
        {
            if (_rnd == null)
            {
                int seed = CurrentProfile != null ? CurrentProfile.randomSeed : 12345;
                _rnd = new System.Random(seed);
            }
            return _rnd;
        }

        /// <summary>
        /// 构建过滤器设置，确保引用一致性
        /// </summary>
        public VegetationGenerator.FilterSettings BuildFilterSettings()
        {
            // 再次检查引用，防止意外重置
            if (GenFilter.noise != Noise)
            {
                GenFilter.noise = Noise;
            }
            return GenFilter;
        }

        public VegetationGenerator.PlacementOverrides BuildPlacementOverrides()
        {
            return new VegetationGenerator.PlacementOverrides
            {
                scaleRange = UIState.CustomScaleRange,
                yRotationRange = UIState.CustomYRotationRange,
                heightRange = UIState.CustomHeightRange,
                slopeRange = UIState.CustomSlopeRange
            };
        }

        private bool AutoPopulateSelectedTerrains()
        {
            if (SelectedTerrains.Count == 0)
            {
                // 优先从选择中获取
                SelectedTerrains.AddRange(Selection.gameObjects
                    .Select(g => g.GetComponent<Terrain>())
                    .Where(t => t != null && !SelectedTerrains.Contains(t)));

                // 其次从场景激活地形获取
                if (SelectedTerrains.Count == 0)
                {
                    SelectedTerrains.AddRange(Terrain.activeTerrains
                        .Where(t => t != null && !SelectedTerrains.Contains(t)));
                }
            }
            return SelectedTerrains.Count > 0 && CurrentProfile != null;
        }

        public void HandleGenerateAction()
        {
            if (!AutoPopulateSelectedTerrains())
            {
                EditorUtility.DisplayDialog("提示", "没有可用地形或未选择Profile。请先在Control页添加选中地形。", "确定");
                return;
            }

            EnsureRandom();

            var filter = BuildFilterSettings();
            var placementOverrides = BuildPlacementOverrides();

            // 1. 生成当前 Profile
            Services.VegetationGenerator.GenerateOnTerrains(
                SelectedTerrains,
                CurrentProfile,
                null,
                filter,
                placementOverrides
            );

            // 2. 生成额外选中的 Profiles (多选支持)
            foreach (var profile in Tools.MTPBrushContext.ExtraProfiles)
            {
                if (profile == null || profile.IsEmpty()) continue;

                Services.VegetationGenerator.GenerateOnTerrains(
                    SelectedTerrains,
                    profile,
                    null,
                    filter,
                    placementOverrides
                );
            }

            Utils.EditorSceneUtils.MarkSceneDirty();
        }

        public void ScanSelectedTerrainsForFacades()
        {
            if (!AutoPopulateSelectedTerrains()) return;
            ClearFacadeCache();
            var prof = CurrentProfile;
            var item = prof?.Items?.FirstOrDefault(it => it != null && it.prefabType == PrefabType.Landscape);
            if (item == null) return;
            var cfg = ConfigTools.GetCachedConfig();
            float eps = cfg != null ? Mathf.Max(0.01f, cfg.facadeRdpEpsilon) : 0.5f;
            float spacing = item.CoreSpacing;
            foreach (var t in SelectedTerrains)
            {
                var bounds = new Bounds(t.transform.position + new Vector3(t.terrainData.size.x * 0.5f, 0f, t.terrainData.size.z * 0.5f), new Vector3(t.terrainData.size.x, 0.1f, t.terrainData.size.z));
                if (cfg != null && cfg.useContourDetection)
                {
                    var contourPaths = ContourDetectionService.ScanContours(t, bounds, cfg.contourSlopeDeg);
                    foreach (var c in contourPaths)
                    {
                        if (c.Points == null || c.Points.Count < 2) continue;
                        // RDP + 样条重采样（已近似在轮廓内消除锯齿）
                        var pts = MrTerrainPainter.Editor.Utils.GeometryUtils.SimplifyPathRDP(c.Points, eps);
                        if (pts == null || pts.Count < 2) continue;
                        var rebuilt = new List<FacadeDetectionService.CliffSlice>();
                        for (int i = 0; i < pts.Count - 1; i++)
                        {
                            Vector3 p0 = i > 0 ? pts[i - 1] : pts[i];
                            Vector3 p1 = pts[i];
                            Vector3 p2 = pts[i + 1];
                            Vector3 p3 = i < pts.Count - 2 ? pts[i + 2] : p2;
                            float segLen = Vector3.Distance(p1, p2);
                            int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / Mathf.Max(0.01f, spacing)));
                            for (int k = 0; k <= steps; k++)
                            {
                                float tParam = steps == 0 ? 0f : (k / (float)steps);
                                var pos = MrTerrainPainter.Editor.Utils.SplineUtils.GetPoint(p0, p1, p2, p3, tParam);
                                float lag = Mathf.Max(0.01f, spacing * 0.05f);
                                var ahead = MrTerrainPainter.Editor.Utils.SplineUtils.GetPoint(p0, p1, p2, p3, Mathf.Clamp01(tParam + lag));
                                var tan = ahead - pos; tan.y = 0f; if (tan.sqrMagnitude < 1e-6f) tan = p2 - p1; tan = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.right;
                                var up = Vector3.up;
                                var n = Vector3.Cross(tan, up).normalized;
                                // 近似高度：采样pos与沿-n方向偏移的高度差
                                float h0 = t.terrainData.GetInterpolatedHeight((pos.x - t.transform.position.x) / t.terrainData.size.x, (pos.z - t.transform.position.z) / t.terrainData.size.z);
                                var back = pos - n * Mathf.Max(0.5f, item.edgeReferenceWidthMeters);
                                float h1 = t.terrainData.GetInterpolatedHeight((back.x - t.transform.position.x) / t.terrainData.size.x, (back.z - t.transform.position.z) / t.terrainData.size.z);
                                float height = Mathf.Max(0f, h1 - h0);
                                var slice = new FacadeDetectionService.CliffSlice
                                {
                                    BottomPosition = new Vector3(pos.x, h0, pos.z),
                                    TopPosition = new Vector3(pos.x, h0 + height, pos.z),
                                    Normal = n,
                                    Direction = up
                                };
                                rebuilt.Add(slice);
                            }
                        }
                        if (rebuilt.Count > 1)
                        {
                            float total = 0f;
                            for (int i = 0; i < rebuilt.Count - 1; i++) total += Vector3.Distance(rebuilt[i].BottomPosition, rebuilt[i + 1].BottomPosition);
                            CachedFacadePaths.Add(new FacadeDetectionService.FacadePath { SourceTerrain = t, SmoothSlices = rebuilt, TotalLength = total });
                        }
                    }
                }
                else
                {
                    var paths = FacadeDetectionService.ScanTerrainForFacades(t, bounds, item, eps, spacing);
                    CachedFacadePaths.AddRange(paths);
                }
            }
            SceneView.RepaintAll();
        }

        public void BakeCachedFacades()
        {
            if (CachedFacadePaths.Count == 0) return;
            var prof = CurrentProfile;
            var item = prof?.Items?.FirstOrDefault(it => it != null && it.prefabType == PrefabType.Landscape);
            if (item == null) return;
            var rnd = EnsureRandom();
            foreach (var path in CachedFacadePaths)
            {
                var t = path.SourceTerrain;
                var parent = Services.VegetationGenerator.ResolveTargetParent(t, item);
                if (parent == null) continue;
                var grid = new Services.FacadeDetectionService.FacadeGrid(Mathf.Max(item.CoreSpacing, 0.01f));
                foreach (var s in path.SmoothSlices)
                {
                    var p2 = new Vector2(s.BottomPosition.x - t.transform.position.x, s.BottomPosition.z - t.transform.position.z);
                    float rendererW = Services.BrushPainter.GetPrefabHorizontalExtentMeters(item.prefab);
                    float rendererH = Services.BrushPainter.GetPrefabHeightMeters(item.prefab);
                    float minH = Config != null ? Mathf.Max(0.0001f, Config.minFacadeHeightMeters) : 0.0001f;
                    float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), s.Height / Mathf.Max(0.0001f, rendererH));
                    float spacingThresh = Mathf.Max(item.CoreSpacing, rendererW * uni);
                    if (grid.HasNearby(p2, spacingThresh)) continue;
                    grid.Add(p2);
                    var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
                    float h = s.Height;
                    float refH = Mathf.Max(item.edgeReferenceHeightMeters, 0.0001f);
                    bool stacking = item.edgeStacking && h > refH * 1.5f;
                    if (!stacking)
                    {
                        var go = VegetationPool.Get(t, item, 0, parent, "Bake Facade Instance");
                        if (go == null) continue;
                        go.transform.position = s.BottomPosition;
                        go.transform.rotation = Quaternion.LookRotation(s.Normal, s.Direction);
                        var baseScale = new Vector3(uni, uni, uni);
                        var finalScale = new Vector3(
                            Mathf.Max(0.0001f, baseScale.x + item.facadeScaleOffset.x),
                            Mathf.Max(0.0001f, baseScale.y + item.facadeScaleOffset.y),
                            Mathf.Max(0.0001f, baseScale.z + item.facadeScaleOffset.z));
                        go.transform.localScale = finalScale;
                        float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                        var offsConf = item.offsets;
                        go.transform.position += rightAxis * offsConf.x + s.Direction * offsConf.y + (-s.Normal.normalized) * (depth + Mathf.Max(0f, offsConf.z));
                        var vi = go.GetComponent<VegetationInstance>() ?? go.AddComponent<VegetationInstance>();
                        vi.sourceTerrain = t;
                        vi.profileItemIndex = 0;
                        vi.sourcePrefabName = item.prefab.name;
                        VegetationPool.IndexRegister(t, go);
                    }
                    else
                    {
                        int layers = Mathf.Max(1, Mathf.CeilToInt(h / refH));
                        float per = h / layers;
                        float used = 0f;
                        for (int L = 0; L < layers; L++)
                        {
                            float currH = L == layers - 1 ? (h - used) : per;
                            used += currH;
                            var go = VegetationPool.Get(t, item, 0, parent, "Bake Facade Instance");
                            if (go == null) continue;
                            var basePos = s.BottomPosition + s.Direction * (per * L + Mathf.Max(0f, item.edgeStackingOffsetMeters));
                            go.transform.position = basePos;
                            go.transform.rotation = Quaternion.LookRotation(s.Normal, s.Direction);
                            float uni2 = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), currH / Mathf.Max(0.0001f, rendererH));
                            if (L == layers - 1) uni2 *= Mathf.Max(0.0001f, item.edgeTopScaleBias);
                            var baseScale2 = new Vector3(uni2, uni2, uni2);
                            var finalScale2 = new Vector3(
                                Mathf.Max(0.0001f, baseScale2.x + item.facadeScaleOffset.x),
                                Mathf.Max(0.0001f, baseScale2.y + item.facadeScaleOffset.y),
                                Mathf.Max(0.0001f, baseScale2.z + item.facadeScaleOffset.z));
                            go.transform.localScale = finalScale2;
                            float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                            var offsConf2 = item.offsets;
                            go.transform.position += rightAxis * offsConf2.x + s.Direction * offsConf2.y + (-s.Normal.normalized) * (depth + Mathf.Max(0f, offsConf2.z));
                            var vi = go.GetComponent<VegetationInstance>() ?? go.AddComponent<VegetationInstance>();
                            vi.sourceTerrain = t;
                            vi.profileItemIndex = 0;
                            vi.sourcePrefabName = item.prefab.name;
                            VegetationPool.IndexRegister(t, go);
                        }
                    }
                }
            }
            ClearFacadeCache();
            SceneView.RepaintAll();
        }

        public void HandleClearAction()
        {
            foreach (var terrain in SelectedTerrains)
            {
                if (terrain == null) continue;
                VegetationPool.RecycleAllInstances(terrain, true, "Clear Vegetation Instances");
            }

            Utils.EditorSceneUtils.MarkSceneDirty();
        }

        public void Dispose()
        {
            _rnd = null;
            // 如果需要清理其他资源
        }

        #endregion
        public List<FacadeDetectionService.FacadePath> CachedFacadePaths { get; } = new List<FacadeDetectionService.FacadePath>();
        public void ClearFacadeCache() => CachedFacadePaths.Clear();
    }
}

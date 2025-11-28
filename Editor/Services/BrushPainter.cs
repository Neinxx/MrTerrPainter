using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Utils;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using MrTerrainPainter.Editor.Services;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine.Rendering;
using PrefabType = MrTerrainPainter.Runtime.Profiles.PrefabType;

namespace MrTerrainPainter.Editor.Services
{
    #region Enums & Data Structures
    public enum BrushShape { Circle, Square, Strip }
    public enum BrushSettingKey { Shape, Size, Strength, DensityScale, Hardness, Preview, FalloffCurve, MinSpacingJitter, Distribution, StrokeSeed, MaxPoints, Cluster, MixItemsWeighted, LimitPerItem, GlobalSpacingFactor, MixExtraProfiles, UseBurstPoisson, PreviewStyle, StrokeSpacingFactor, StrokeSpacingAbsolute, UseAbsoluteStrokeSpacing }

    [System.Serializable]
    public struct BrushPreviewStyle
    {
        public Color fillColor;
        public Color ringColor;
        public Color innerColor;
        public float ringWidth;
        public float innerWidth;
        public bool showLabel;
        public Color labelColor;
        public Vector2 labelOffset;

        public static BrushPreviewStyle Default => new BrushPreviewStyle
        {
            fillColor = new Color(0.24f, 0.65f, 1f, 0.15f),
            ringColor = new Color(1f, 1f, 1f, 0.9f),
            innerColor = new Color(0.24f, 0.65f, 1f, 0.35f),
            ringWidth = 6f,
            innerWidth = 4f,
            showLabel = true,
            labelColor = Color.white,
            labelOffset = Vector2.zero
        };
    }

    /// <summary>
    /// 笔刷绘制上下文 - 封装Paint/PaintMixed的所有参数
    /// </summary>
    public struct PaintContext
    {
        public Terrain Terrain;
        public VegetationProfile Profile;
        public IReadOnlyList<VegetationProfile> Profiles; // 用于PaintMixed
        public Vector3 Center;
        public BrushSettings BrushSettings;
        public System.Random Random;
        public VegetationGenerator.PlacementOverrides? Overrides;
    }

    /// <summary>
    /// 擦除上下文 - 封装Erase的所有参数
    /// </summary>
    public struct EraseContext
    {
        public Terrain Terrain; // 可选，为null时擦除所有地形
        public Vector3 Center;
        public BrushSettings BrushSettings;
        public bool EraseAll;
        public IReadOnlyList<GameObject> OnlyTypes;
    }
    #endregion

    #region Jobs
    // [Fix] 将 Job 设为 public 并添加绝对安全的边界检查，防止 IndexOutOfRangeException
    [BurstCompile]
    public struct TerrainSampleJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> pointsWorld;
        [ReadOnly] public NativeArray<float> heightsPatch;
        public int xBase;
        public int zBase;
        public int width;
        public int height;
        public int hmMax;
        public float dxWorld;
        public float dzWorld;
        public float3 terrainPos;
        public float sizeX;
        public float sizeZ;
        public float sizeY;

        [WriteOnly] // 显式标记为只写，提高安全性
        public NativeArray<float> outHeightLocal;
        [WriteOnly]
        public NativeArray<float3> outNormal;
        [WriteOnly]
        public NativeArray<float> outSlope;

        // [修复] 更健壮的安全获取方法
        private float GetHeightSafe(int lx, int lz)
        {
            // 这里的检查虽然是冗余的（因为调用处已经钳制），但为了防止任何计算漏网之鱼
            if (lx < 0 || lx >= width || lz < 0 || lz >= height) return 0f;
            if (!heightsPatch.IsCreated || heightsPatch.Length == 0) return 0f;

            // 计算索引
            int idx = lz * width + lx;

            // 再次检查 NativeArray 边界
            if (idx < 0 || idx >= heightsPatch.Length) return 0f;

            return heightsPatch[idx];
        }

        private float SampleHeight01(float2 uv)
        {
            if (width <= 0 || height <= 0 || hmMax <= 0) return 0f;

            float u = math.clamp(uv.x, 0f, hmMax);
            float v = math.clamp(uv.y, 0f, hmMax);
            int xi = (int)math.floor(u);
            int zi = (int)math.floor(v);
            float fu = u - xi;
            float fv = v - zi;

            int lx = xi - xBase;
            int lz = zi - zBase;

            // [关键修复] 使用 math.max(0, width - 1) 确保 max >= min，防止 width=0 时 clamp 异常
            int maxX = math.max(0, width - 1);
            int maxZ = math.max(0, height - 1);

            lx = math.clamp(lx, 0, maxX);
            lz = math.clamp(lz, 0, maxZ);

            int lx1 = math.min(lx + 1, maxX);
            int lz1 = math.min(lz + 1, maxZ);

            float h00 = GetHeightSafe(lx, lz);
            float h10 = GetHeightSafe(lx1, lz);
            float h01 = GetHeightSafe(lx, lz1);
            float h11 = GetHeightSafe(lx1, lz1);

            return math.lerp(math.lerp(h00, h10, fu), math.lerp(h01, h11, fu), fv);
        }

        private float3 ComputeNormal(float2 uv)
        {
            if (width <= 0 || height <= 0 || hmMax <= 1) return new float3(0, 1, 0);

            float u = math.clamp(uv.x, 1f, hmMax - 1f);
            float v = math.clamp(uv.y, 1f, hmMax - 1f);
            int xi = (int)math.floor(u);
            int zi = (int)math.floor(v);

            int maxX = math.max(0, width - 1);
            int maxZ = math.max(0, height - 1);

            int lxC = math.clamp(xi - xBase, 0, maxX);
            int lzC = math.clamp(zi - zBase, 0, maxZ);

            int lxL = math.max(lxC - 1, 0);
            int lxR = math.min(lxC + 1, maxX);
            int lzU = math.max(lzC - 1, 0);
            int lzD = math.min(lzC + 1, maxZ);

            float hL = GetHeightSafe(lxL, lzC);
            float hR = GetHeightSafe(lxR, lzC);
            float hU = GetHeightSafe(lxC, lzU);
            float hD = GetHeightSafe(lxC, lzD);

            float safeDx = math.max(dxWorld, 0.0001f);
            float safeDz = math.max(dzWorld, 0.0001f);

            float dhdx = ((hR - hL) * sizeY) / (2f * safeDx);
            float dhdz = ((hD - hU) * sizeY) / (2f * safeDz);
            return math.normalizesafe(new float3(-dhdx, 1f, -dhdz));
        }

        public void Execute(int index)
        {
            // 严格边界检查：保护所有 NativeArray 访问，避免越界
            if (index < 0 || index >= pointsWorld.Length) return;
            int outLen = math.min(math.min(outHeightLocal.Length, outNormal.Length), outSlope.Length);
            if (index >= outLen) return;

            // [关键修复] 熔断机制：如果 heightPatch 未创建或为空，或者尺寸无效，直接填充默认值并返回
            // 这避免了所有潜在的越界访问
            if (!heightsPatch.IsCreated || heightsPatch.Length == 0 || width <= 0 || height <= 0 || heightsPatch.Length < (width * height))
            {
                outHeightLocal[index] = 0f;
                outNormal[index] = new float3(0, 1, 0);
                outSlope[index] = 0f;
                return;
            }

            float2 pw = pointsWorld[index];
            float2 pl = new float2(pw.x - terrainPos.x, pw.y - terrainPos.z);

            float sx = math.max(sizeX, 0.001f);
            float sz = math.max(sizeZ, 0.001f);

            float2 uv = new float2((pl.x / sx) * hmMax, (pl.y / sz) * hmMax);

            float h01 = SampleHeight01(uv);
            float hLocal = h01 * sizeY;

            float3 n = ComputeNormal(uv);
            float slope = SampleSlope(n);

            outHeightLocal[index] = hLocal;
            outNormal[index] = n;
            outSlope[index] = slope;
        }

        private float SampleSlope(float3 n)
        {
            float cosTheta = math.clamp(n.y, -1f, 1f);
            float theta = math.acos(cosTheta);
            return theta * 57.2957795f;
        }
    }

    [BurstCompile]
    struct RelaxationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> inputPoints;

        // [Fix] 移除 [WriteOnly] 并添加 DisableParallelForRestriction 以解决 InvalidOperationException
        [NativeDisableParallelForRestriction]
        public NativeArray<float2> outputPoints;

        public float repelDistSq;
        public float2 center;
        public float radiusSq;
        public float strength;

        public void Execute(int index)
        {
            float2 p = inputPoints[index];
            float2 force = float2.zero;

            for (int i = 0; i < inputPoints.Length; i++)
            {
                if (i == index) continue;
                float2 other = inputPoints[i];
                float2 dir = p - other;
                float distSq = math.lengthsq(dir);

                if (distSq < repelDistSq && distSq > 0.00001f)
                {
                    float dist = math.sqrt(distSq);
                    float repelRadius = math.sqrt(repelDistSq);
                    float strengthFactor = 1.0f - (dist / repelRadius);
                    force += (dir / dist) * strengthFactor;
                }
            }

            p += force * strength;

            if (math.lengthsq(p - center) > radiusSq)
            {
                p = center + math.normalize(p - center) * math.sqrt(radiusSq);
            }
            outputPoints[index] = p;
        }
    }
    #endregion

    #region Settings Class
    public class BrushSettings
    {
        public event System.Action<string> Changed;
        public event System.Action<BrushSettingKey> ChangedKey;

        private static readonly Dictionary<BrushSettingKey, string> s_nameMap = new()
        {
            { BrushSettingKey.Shape, nameof(shape) },
            { BrushSettingKey.Size, nameof(size) },
            { BrushSettingKey.Strength, nameof(strength) },
            { BrushSettingKey.DensityScale, nameof(densityScale) },
            { BrushSettingKey.Hardness, nameof(hardness) },
            { BrushSettingKey.Preview, nameof(preview) },
            { BrushSettingKey.FalloffCurve, nameof(falloffCurve) },
            { BrushSettingKey.MinSpacingJitter, nameof(minSpacingJitter) },
            { BrushSettingKey.Distribution, nameof(distribution) },
            { BrushSettingKey.StrokeSeed, nameof(strokeSeed) },
            { BrushSettingKey.MaxPoints, nameof(maxPoints) },
            { BrushSettingKey.Cluster, nameof(cluster) },
            { BrushSettingKey.MixItemsWeighted, nameof(mixItemsWeighted) },
            { BrushSettingKey.LimitPerItem, nameof(limitPerItem) },
            { BrushSettingKey.GlobalSpacingFactor, nameof(globalSpacingFactor) },
            { BrushSettingKey.MixExtraProfiles, nameof(mixExtraProfiles) },
            { BrushSettingKey.UseBurstPoisson, nameof(useBurstPoisson) },
            { BrushSettingKey.PreviewStyle, nameof(previewStyle) },
            { BrushSettingKey.StrokeSpacingFactor, nameof(strokeSpacingFactor) },
            { BrushSettingKey.StrokeSpacingAbsolute, nameof(strokeSpacingAbsolute) },
            { BrushSettingKey.UseAbsoluteStrokeSpacing, nameof(useAbsoluteStrokeSpacing) },
            { (BrushSettingKey)1001, nameof(adaptiveMinFactor) },
            { (BrushSettingKey)1002, nameof(adaptiveMaxFactor) },
            { (BrushSettingKey)1003, nameof(adaptiveNoiseWeight) },
        };

        private bool Notify(BrushSettingKey key)
        {
            ChangedKey?.Invoke(key);
            if (s_nameMap.TryGetValue(key, out var n)) Changed?.Invoke(n);
            return true;
        }

        private bool SetValue<T>(ref T field, T value, BrushSettingKey key)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            return Notify(key);
        }

        private bool SetFloat(ref float field, float value, BrushSettingKey key, float? min = null, float? max = null, bool clamp01 = false)
        {
            var v = clamp01 ? Mathf.Clamp01(value) : value;
            if (min.HasValue) v = Mathf.Max(v, min.Value);
            if (max.HasValue) v = Mathf.Min(v, max.Value);
            if (Mathf.Approximately(field, v)) return false;
            field = v;
            return Notify(key);
        }

        private bool SetInt(ref int field, int value, BrushSettingKey key, int min = int.MinValue)
        {
            var v = min != int.MinValue ? Mathf.Max(value, min) : value;
            if (field == v) return false;
            field = v;
            return Notify(key);
        }

        private bool SetRef<T>(ref T field, T value, BrushSettingKey key) where T : class
        {
            if (ReferenceEquals(field, value)) return false;
            field = value;
            return Notify(key);
        }

        private BrushShape _shape = BrushShape.Circle;
        public BrushShape shape { get => _shape; set => SetValue(ref _shape, value, BrushSettingKey.Shape); }

        private float _size = 5f;
        public float size { get => _size; set => SetFloat(ref _size, value, BrushSettingKey.Size, min: 0.01f); }

        private float _strength = 1f;
        public float strength { get => _strength; set => SetFloat(ref _strength, value, BrushSettingKey.Strength, min: 0f); }

        private float _densityScale = 1f;
        public float densityScale { get => _densityScale; set => SetFloat(ref _densityScale, value, BrushSettingKey.DensityScale, min: 0f); }

        private float _hardness = 1f;
        public float hardness { get => _hardness; set => SetFloat(ref _hardness, value, BrushSettingKey.Hardness, clamp01: true); }

        private bool _preview = true;
        public bool preview { get => _preview; set => SetValue(ref _preview, value, BrushSettingKey.Preview); }

        private AnimationCurve _falloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        public AnimationCurve falloffCurve { get => _falloffCurve; set { if (value != null) SetRef(ref _falloffCurve, value, BrushSettingKey.FalloffCurve); } }

        private float _minSpacingJitter = 0f;
        public float minSpacingJitter { get => _minSpacingJitter; set => SetFloat(ref _minSpacingJitter, value, BrushSettingKey.MinSpacingJitter, min: 0f); }

        private DistributionType _distribution = DistributionType.Uniform;
        public DistributionType distribution { get => _distribution; set => SetValue(ref _distribution, value, BrushSettingKey.Distribution); }

        private int _strokeSeed = 0;
        public int strokeSeed { get => _strokeSeed; set => SetInt(ref _strokeSeed, value, BrushSettingKey.StrokeSeed); }

        private int _maxPoints = 1000;
        public int maxPoints { get => _maxPoints; set => SetInt(ref _maxPoints, value, BrushSettingKey.MaxPoints, min: 1); }

        private ClusterSettings _cluster = new ClusterSettings { clusterCount = 10, childPerCluster = 5, clusterRadius = 2f, childJitter = 0.2f };
        public ClusterSettings cluster { get => _cluster; set => SetValue(ref _cluster, value, BrushSettingKey.Cluster); }

        private bool _mixItemsWeighted = true;
        public bool mixItemsWeighted { get => _mixItemsWeighted; set => SetValue(ref _mixItemsWeighted, value, BrushSettingKey.MixItemsWeighted); }

        private bool _limitPerItem = true;
        public bool limitPerItem { get => _limitPerItem; set => SetValue(ref _limitPerItem, value, BrushSettingKey.LimitPerItem); }

        private float _globalSpacingFactor = 0f;
        public float globalSpacingFactor { get => _globalSpacingFactor; set => SetFloat(ref _globalSpacingFactor, value, BrushSettingKey.GlobalSpacingFactor, min: 0f); }

        private bool _mixExtraProfiles = false;
        public bool mixExtraProfiles { get => _mixExtraProfiles; set => SetValue(ref _mixExtraProfiles, value, BrushSettingKey.MixExtraProfiles); }

        private bool _useBurstPoisson = true;
        public bool useBurstPoisson { get => _useBurstPoisson; set => SetValue(ref _useBurstPoisson, value, BrushSettingKey.UseBurstPoisson); }

        private BrushPreviewStyle _previewStyle = BrushPreviewStyle.Default;
        public BrushPreviewStyle previewStyle { get => _previewStyle; set => SetValue(ref _previewStyle, value, BrushSettingKey.PreviewStyle); }

        private float _strokeSpacingFactor = 0.25f;
        public float strokeSpacingFactor { get => _strokeSpacingFactor; set => SetFloat(ref _strokeSpacingFactor, value, BrushSettingKey.StrokeSpacingFactor, min: 0f, max: 2f); }

        private float _strokeSpacingAbsolute = 0f;
        public float strokeSpacingAbsolute { get => _strokeSpacingAbsolute; set => SetFloat(ref _strokeSpacingAbsolute, value, BrushSettingKey.StrokeSpacingAbsolute, min: 0f); }

        private bool _useAbsoluteStrokeSpacing = false;
        public bool useAbsoluteStrokeSpacing { get => _useAbsoluteStrokeSpacing; set => SetValue(ref _useAbsoluteStrokeSpacing, value, BrushSettingKey.UseAbsoluteStrokeSpacing); }

        private float _adaptiveMinFactor = 0.7f;
        public float adaptiveMinFactor { get => _adaptiveMinFactor; set => SetFloat(ref _adaptiveMinFactor, value, (BrushSettingKey)1001, min: 0.1f); }

        private float _adaptiveMaxFactor = 1.8f;
        public float adaptiveMaxFactor { get => _adaptiveMaxFactor; set => SetFloat(ref _adaptiveMaxFactor, value, (BrushSettingKey)1002, min: 0.1f); }

        private float _adaptiveNoiseWeight = 1f;
        public float adaptiveNoiseWeight { get => _adaptiveNoiseWeight; set => SetFloat(ref _adaptiveNoiseWeight, value, (BrushSettingKey)1003, min: 0.0001f); }
    }
    #endregion

    #region Extensions (Physics & Jobs)
    public static class BrushEngineExtensions
    {
        public static void ApplyRelaxation(List<Vector2> points, Vector2 center, float radius, float repelDist, int iterations = 3)
        {
            if (points == null || points.Count < 2) return;

            int count = points.Count;
            var bufferA = new NativeArray<float2>(count, Allocator.TempJob);
            var bufferB = new NativeArray<float2>(count, Allocator.TempJob);

            for (int i = 0; i < count; i++) bufferA[i] = points[i];

            float radiusSq = radius * radius;
            float repelDistSq = repelDist * repelDist;
            var centerF2 = new float2(center.x, center.y);

            for (int i = 0; i < iterations; i++)
            {
                bool useAAsInput = (i % 2 == 0);
                var input = useAAsInput ? bufferA : bufferB;
                var output = useAAsInput ? bufferB : bufferA;

                var job = new RelaxationJob
                {
                    inputPoints = input,
                    outputPoints = output,
                    repelDistSq = repelDistSq,
                    center = centerF2,
                    radiusSq = radiusSq,
                    strength = 0.5f
                };

                job.Schedule(count, 64).Complete();
            }

            var finalBuffer = (iterations % 2 != 0) ? bufferB : bufferA;
            for (int i = 0; i < count; i++) points[i] = finalBuffer[i];

            bufferA.Dispose();
            bufferB.Dispose();
        }
    }
    #endregion

    #region Brush Painter Class
    public static class BrushPainter
    {
        private static readonly Dictionary<int, BrushSpatialGrid> s_itemGridCache = new Dictionary<int, BrushSpatialGrid>();
        private static BrushSpatialGrid s_sharedGrid;
        private static bool s_configCompleteCached;

        static BrushPainter()
        {
            RefreshConfigCache();
            ConfigTools.CompletenessChanged += v => s_configCompleteCached = v;
            ConfigTools.ConfigUpdated += RefreshConfigCache;
        }

        private static void RefreshConfigCache()
        {
            var c = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? ConfigTools.GetCachedConfig();
            s_configCompleteCached = c != null && ConfigTools.IsComplete(c, out _);
        }

        public static void ClearCache()
        {
            PrefabMetricsCache.ClearCache();
            s_itemGridCache.Clear();
            if (s_sharedGrid != null) s_sharedGrid.Clear();
            s_sharedGrid = null;
        }

        // ------------------------------------------------------
        // Preview System (Delegated to BrushPreviewRenderer)
        // ------------------------------------------------------
        #region Preview System
        public static void DrawPreview(SceneInteractionService.PreviewData data, BrushSettings bs)
        {
            BrushPreviewRenderer.DrawPreview(data, bs, s_configCompleteCached);
        }

        public static void DrawPreview(Vector3 center, Vector3 normal, BrushSettings bs)
        {
            BrushPreviewRenderer.DrawPreview(center, normal, bs, s_configCompleteCached);
        }
        #endregion

        // -----------------------------------------------------------------------
        // Painting Operations
        // -----------------------------------------------------------------------
        #region Painting Operations
        /// <summary>
        /// 笔刷绘制方法（优化版：从6个参数简化为1个）
        /// </summary>
        public static void Paint(PaintContext context)
        {
            if (context.Terrain == null || context.Profile == null || context.Profile.IsEmpty()) return;
            var td = context.Terrain.terrainData;
            if (td == null) return;

            // 提取局部变量以简化后续代码
            var terrain = context.Terrain;
            var profile = context.Profile;
            var center = context.Center;
            var bs = context.BrushSettings;
            var rnd = context.Random;
            var ov = context.Overrides;

            float radius = bs.size;
            var areaWorldShared = new Bounds(new Vector3(center.x, terrain.transform.position.y, center.z), new Vector3(radius * 2f, 1f, radius * 2f));
            var hbShared = TerrainUtils.FetchHeightsBlock(terrain, areaWorldShared, Allocator.TempJob);
            var typeToNode = VegetationGenerator.BuildTypeToNodeMapping();

            if (bs.mixItemsWeighted)
            {
                var allItems = BrushEngine.AcquireItemList(profile.Items.Count);
                for (int iA = 0; iA < profile.Items.Count; iA++) { var itA = profile.Items[iA]; if (itA != null && itA.IsValid()) allItems.Add(itA); }
                if (allItems.Count == 0) { if (hbShared.heights.IsCreated) hbShared.heights.Dispose(); BrushEngine.ReleaseItemList(allItems); return; }

                int seed = bs.strokeSeed != 0 ? bs.strokeSeed : profile.randomSeed;
                var centerXZ = new Vector2(center.x, center.z);
                int totalDesired = 0;
                var perItemLimit = new int[allItems.Count];

                for (int i = 0; i < allItems.Count; i++)
                {
                    var it = allItems[i];
                    int c = Mathf.Clamp(Mathf.RoundToInt(it.baseDensity * bs.densityScale * bs.strength * it.weight * 10f), 0, 500);
                    perItemLimit[i] = c;
                    totalDesired += c;
                }

                float minSpacingForAll = 0.5f;
                if (allItems.Count > 0)
                {
                    float best = bs.size;
                    for (int i = 0; i < allItems.Count; i++)
                    {
                        float s = Mathf.Max(Mathf.Max(allItems[i].CoreSpacing, allItems[i].CoreMinRadius), 0.01f);
                        if (s < best) best = s;
                    }
                    minSpacingForAll = best;
                }

                int candidateCount = VegetationGenerator.ComputeDesiredCandidateCount(bs.shape, radius, minSpacingForAll, Mathf.Max(1, totalDesired), bs.maxPoints);
                List<Vector2> candidates = null;

                bool useFacade = false;
                for (int i = 0; i < allItems.Count; i++) { if (allItems[i].prefabType == PrefabType.Landscape) { useFacade = true; break; } }

                if (useFacade)
                {
                    var handler = new FacadePlacementHandler.Handler();
                    if (handler.CanHandle(allItems[0], bs))
                    {
                        var ctx = new PaintContext { Terrain = terrain, Profile = profile, Center = center, BrushSettings = bs, Random = rnd, Overrides = ov };
                        handler.Paint(ctx);
                        for (int i = 0; i < allItems.Count; i++) if (allItems[i].prefabType == PrefabType.Landscape) perItemLimit[i] = 0;
                    }
                }

                if (candidates == null)
                {
                    var candReq = new VegetationGenerator.CandidateBuilder()
                        .Center(centerXZ).Radius(radius).Shape(bs.shape).Desired(candidateCount)
                        .MinSpacing(minSpacingForAll).Jitter(bs.minSpacingJitter).Seed(seed)
                        .FromBrush(bs).Random(rnd).Build();
                    candidates = VegetationGenerator.BuildCandidates(candReq);
                }

                if (candidates != null && candidates.Count > 0 && bs.distribution != DistributionType.EdgeLine)
                {
                    float repelDist = Mathf.Max(minSpacingForAll, 0.01f) * 0.8f;
                    BrushEngineExtensions.ApplyRelaxation(candidates, centerXZ, radius, repelDist, 3);
                }

                NativeArray<float> outH2 = default;
                NativeArray<float3> outN2 = default;
                NativeArray<float> outS2 = default;
                if (candidates.Count > 0)
                {
                    //var pts2 = new NativeArray<float2>(candidates.Count, Allocator.TempJob);
                    // for (int iPt = 0; iPt < candidates.Count; iPt++) pts2[iPt] = new float2(candidates[iPt].x, candidates[iPt].y);

                    outH2 = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                    outN2 = new NativeArray<float3>(candidates.Count, Allocator.TempJob);
                    outS2 = new NativeArray<float>(candidates.Count, Allocator.TempJob);

                    bool patchValid2 = hbShared.heights.IsCreated && hbShared.heights.Length >= (hbShared.width * hbShared.height) && hbShared.width > 0 && hbShared.height > 0;
                    if (patchValid2)
                    {
                        var pts2 = new NativeArray<float2>(candidates.Count, Allocator.TempJob);
                        for (int iPt = 0; iPt < candidates.Count; iPt++) pts2[iPt] = new float2(candidates[iPt].x, candidates[iPt].y);

                        var job2 = new TerrainSampleJob
                        {
                            pointsWorld = pts2,
                            heightsPatch = hbShared.heights,
                            xBase = hbShared.xBase,
                            zBase = hbShared.zBase,
                            width = hbShared.width,
                            height = hbShared.height,
                            hmMax = td.heightmapResolution - 1,
                            dxWorld = hbShared.dxWorld,
                            dzWorld = hbShared.dzWorld,
                            terrainPos = new float3(terrain.transform.position.x, terrain.transform.position.y, terrain.transform.position.z),
                            sizeX = td.size.x,
                            sizeZ = td.size.z,
                            sizeY = td.size.y,
                            outHeightLocal = outH2,
                            outNormal = outN2,
                            outSlope = outS2
                        };
                        job2.Schedule(candidates.Count, 64).Complete();
                        pts2.Dispose();
                    }
                    else
                    {
                        for (int i = 0; i < candidates.Count; i++) { outH2[i] = 0f; outN2[i] = new float3(0f, 1f, 0f); outS2[i] = 0f; }
                    }
                }

                var idxArr = new int[candidates.Count];
                for (int i = 0; i < candidates.Count; i++) idxArr[i] = i;
                for (int i = idxArr.Length - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = idxArr[i]; idxArr[i] = idxArr[j]; idxArr[j] = t; }

                var quota = new int[allItems.Count];
                for (int i = 0; i < allItems.Count; i++) quota[i] = perItemLimit[i];
                var allocCounts = new int[allItems.Count];
                var allocIndexArrays = new int[allItems.Count][];
                for (int i = 0; i < allItems.Count; i++) allocIndexArrays[i] = quota[i] > 0 ? new int[quota[i]] : System.Array.Empty<int>();

                for (int kk = 0; kk < idxArr.Length; kk++)
                {
                    int ci = idxArr[kk];
                    var wp = new Vector3(candidates[ci].x, terrain.transform.position.y + (outH2.IsCreated ? outH2[ci] : 0f), candidates[ci].y);
                    float hLoc = outH2.IsCreated ? outH2[ci] : 0f;
                    float slopeLoc = outS2.IsCreated ? outS2[ci] : 0f;
                    float wsum = 0f;
                    for (int i = 0; i < allItems.Count; i++)
                    {
                        if (quota[i] <= 0) continue;
                        var itM = allItems[i];
                        var f = new HeightSlopeFilter(itM);
                        if (!f.Pass(ci, wp, hLoc, slopeLoc)) continue;
                        wsum += Mathf.Max(0.0001f, itM.weight);
                    }
                    if (wsum <= 0f) continue;
                    double rPick = rnd.NextDouble() * wsum;
                    float acc = 0f; int chosen = -1;
                    for (int i = 0; i < allItems.Count; i++)
                    {
                        if (quota[i] <= 0) continue;
                        var itM = allItems[i];
                        var f = new HeightSlopeFilter(itM);
                        if (!f.Pass(ci, wp, hLoc, slopeLoc)) continue;
                        acc += Mathf.Max(0.0001f, itM.weight);
                        if (rPick <= acc) { chosen = i; break; }
                    }
                    if (chosen < 0) continue;
                    int pos = allocCounts[chosen];
                    if (pos < allocIndexArrays[chosen].Length)
                    {
                        allocIndexArrays[chosen][pos] = ci;
                        allocCounts[chosen] = pos + 1;
                        quota[chosen]--;
                    }
                }

                var globalGridMix = new BrushSpatialGrid(Mathf.Max(minSpacingForAll, 0.01f));

                for (int iItem = 0; iItem < allItems.Count; iItem++)
                {
                    if (allocCounts[iItem] == 0) continue;
                    var item = allItems[iItem];
                    var targetParent = typeToNode.TryGetValue(item.prefabType, out var tf) ? tf : null;
                    if (targetParent == null)
                    {
                        VegetationGenerator.LogMissingMappingOnce(item.prefabType);
                        var fallback = ResolveDefaultContainer(terrain);
                        if (fallback == null) continue;
                        targetParent = fallback;
                    }

                    var itemCandidates = BrushEngine.AcquireList(allocCounts[iItem]);
                    var cWorld = BrushEngine.AcquireList3(allocCounts[iItem]);
                    var hArr = new NativeArray<float>(allocCounts[iItem], Allocator.TempJob);
                    var nArr = new NativeArray<float3>(allocCounts[iItem], Allocator.TempJob);
                    var sArr = new NativeArray<float>(allocCounts[iItem], Allocator.TempJob);
                    for (int a = 0; a < allocCounts[iItem]; a++)
                    {
                        int ci = allocIndexArrays[iItem][a];
                        var v2 = candidates[ci];
                        itemCandidates.Add(v2);
                        cWorld.Add(new Vector3(v2.x, center.y, v2.y));
                        hArr[a] = outH2[ci];
                        nArr[a] = outN2[ci];
                        sArr[a] = outS2[ci];
                    }

                    var pipelineContext = new PipelineContext
                    {
                        Terrain = terrain,
                        Center = center,
                        Radius = radius,
                        Item = item,
                        ItemIndex = iItem,
                        Parent = targetParent
                    };
                    var pipelineData = new PipelineData
                    {
                        Candidates = cWorld,
                        Heights = hArr,
                        Slopes = sArr,
                        Normals = nArr
                    };

                    VegetationPipeline.Shared
                        .Setup(new CandidateSamplerFromList(itemCandidates, center.y), new HeightSlopeFilter(item), new StandardMutator(item), new GlobalGridSpawner(globalGridMix, minSpacingForAll, new PooledSpawner()))
                        .Run(pipelineContext, pipelineData);

                    hArr.Dispose();
                    nArr.Dispose();
                    sArr.Dispose();
                    BrushEngine.ReleaseList(itemCandidates);
                    BrushEngine.ReleaseList3(cWorld);
                }

                BrushEngine.ReleaseList(candidates);
                if (outH2.IsCreated) outH2.Dispose();
                if (outN2.IsCreated) outN2.Dispose();
                if (outS2.IsCreated) outS2.Dispose();
                if (hbShared.heights.IsCreated) hbShared.heights.Dispose();
                BrushEngine.ReleaseItemList(allItems);
                return;
            }

            var items = profile.Items;
            List<Vector2> globalCandidates = null;
            BrushSpatialGrid globalGridAll = null;
            float globalSpacingAll = 0.01f;
            NativeArray<float> outHG = default;
            NativeArray<float3> outNG = default;
            NativeArray<float> outSG = default;
            List<int>[] allocPerItem = null;
            int[] quotaPerItem = null;
            if (items != null && items.Count > 0 && bs.distribution != DistributionType.EdgeLine)
            {
                var validItems = BrushEngine.AcquireItemList(items.Count);
                for (int vi = 0; vi < items.Count; vi++) { var itm = items[vi]; if (itm != null && itm.IsValid()) validItems.Add(itm); }
                if (validItems.Count > 0)
                {
                    float minSpacingForAll2 = bs.size;
                    int totalDesired2 = 0;
                    for (int i = 0; i < validItems.Count; i++)
                    {
                        var vi = validItems[i];
                        float sAll = Mathf.Max(Mathf.Max(vi.CoreSpacing, vi.CoreMinRadius), 0.01f);
                        if (sAll < minSpacingForAll2) minSpacingForAll2 = sAll;
                        int cEst = Mathf.Clamp(Mathf.RoundToInt(vi.baseDensity * bs.densityScale * bs.strength * 10f), 0, 500);
                        totalDesired2 += cEst;
                    }
                    var centerXZ2 = new Vector2(center.x, center.z);
                    int desired2 = VegetationGenerator.ComputeDesiredCandidateCount(bs.shape, radius, minSpacingForAll2, Mathf.Max(1, totalDesired2), bs.maxPoints);
                    globalCandidates = VegetationGenerator.BuildCandidates(centerXZ2, radius, bs.shape, desired2, minSpacingForAll2, bs.minSpacingJitter,
                        bs.strokeSeed != 0 ? bs.strokeSeed : profile.randomSeed, bs.distribution, bs.useBurstPoisson, bs.cluster,
                        bs.adaptiveMinFactor, bs.adaptiveMaxFactor, bs.adaptiveNoiseWeight, rnd);
                    if (globalCandidates != null && globalCandidates.Count > 0)
                    {
                        float repelAll = Mathf.Max(minSpacingForAll2, 0.01f) * 0.8f;
                        BrushEngineExtensions.ApplyRelaxation(globalCandidates, centerXZ2, radius, repelAll, 3);
                        globalSpacingAll = Mathf.Max(minSpacingForAll2, 0.01f);
                        globalGridAll = new BrushSpatialGrid(globalSpacingAll);

                        var ptsG = new NativeArray<float2>(globalCandidates.Count, Allocator.TempJob);
                        for (int gi = 0; gi < globalCandidates.Count; gi++) ptsG[gi] = new float2(globalCandidates[gi].x, globalCandidates[gi].y);
                        outHG = new NativeArray<float>(globalCandidates.Count, Allocator.TempJob);
                        outNG = new NativeArray<float3>(globalCandidates.Count, Allocator.TempJob);
                        outSG = new NativeArray<float>(globalCandidates.Count, Allocator.TempJob);
                        var jobG = new TerrainSampleJob
                        {
                            pointsWorld = ptsG,
                            heightsPatch = hbShared.heights,
                            xBase = hbShared.xBase,
                            zBase = hbShared.zBase,
                            width = hbShared.width,
                            height = hbShared.height,
                            hmMax = td.heightmapResolution - 1,
                            dxWorld = hbShared.dxWorld,
                            dzWorld = hbShared.dzWorld,
                            terrainPos = new float3(terrain.transform.position.x, terrain.transform.position.y, terrain.transform.position.z),
                            sizeX = td.size.x,
                            sizeZ = td.size.z,
                            sizeY = td.size.y,
                            outHeightLocal = outHG,
                            outNormal = outNG,
                            outSlope = outSG
                        };
                        jobG.Schedule(globalCandidates.Count, 64).Complete();
                        ptsG.Dispose();

                        quotaPerItem = new int[items.Count];
                        for (int i = 0; i < items.Count; i++)
                        {
                            var it = items[i];
                            if (it == null || !it.IsValid()) { quotaPerItem[i] = 0; continue; }
                            int cEst = Mathf.Clamp(Mathf.RoundToInt(it.baseDensity * bs.densityScale * bs.strength * it.weight * 10f), 0, 500);
                            quotaPerItem[i] = cEst;
                        }
                        allocPerItem = new List<int>[items.Count];
                        for (int i = 0; i < items.Count; i++) allocPerItem[i] = new List<int>();
                        var orderG = new List<int>(globalCandidates.Count);
                        for (int i = 0; i < globalCandidates.Count; i++) orderG.Add(i);
                        for (int i = orderG.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = orderG[i]; orderG[i] = orderG[j]; orderG[j] = t; }
                        for (int k = 0; k < orderG.Count; k++)
                        {
                            int ci = orderG[k];
                            var wp = new Vector3(globalCandidates[ci].x, terrain.transform.position.y + outHG[ci], globalCandidates[ci].y);
                            var elig = new List<int>();
                            float wsum = 0f;
                            for (int i = 0; i < items.Count; i++)
                            {
                                var it = items[i];
                                if (it == null || !it.IsValid()) continue;
                                if (quotaPerItem[i] <= 0) continue;
                                if (!(new HeightSlopeFilter(it)).Pass(ci, wp, outHG[ci], outSG[ci])) continue;
                                elig.Add(i);
                                wsum += Mathf.Max(0.0001f, it.weight);
                            }
                            if (elig.Count == 0) continue;
                            double pick = rnd.NextDouble() * wsum;
                            float acc = 0f; int chosen = elig[0];
                            for (int e = 0; e < elig.Count; e++)
                            {
                                float iw = Mathf.Max(0.0001f, items[elig[e]].weight);
                                acc += iw;
                                if (pick <= acc) { chosen = elig[e]; break; }
                            }
                            allocPerItem[chosen].Add(ci);
                            quotaPerItem[chosen]--;
                        }
                    }
                    BrushEngine.ReleaseItemList(validItems);
                }
            }
            for (int it = 0; it < items.Count; it++)
            {
                var item = items[it];
                if (item == null || !item.IsValid()) continue;
                int count = Mathf.RoundToInt(item.baseDensity * bs.densityScale * bs.strength * item.weight * 10f);
                count = Mathf.Clamp(count, 0, 500);
                if (count <= 0) continue;

                float spacing = Mathf.Max(Mathf.Max(item.CoreSpacing, item.CoreMinRadius), 0.01f);
                int seed = bs.strokeSeed != 0 ? bs.strokeSeed : profile.randomSeed;
                var centerXZ = new Vector2(center.x, center.z);
                int desired = VegetationGenerator.ComputeDesiredCandidateCount(bs.shape, radius, spacing, Mathf.Min(count, bs.maxPoints), bs.maxPoints);

                List<Vector2> candidates = null;
                bool useFacade = (bs.distribution == DistributionType.EdgeLine && item.prefabType == PrefabType.Landscape);

                if (useFacade)
                {
                    var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                    var targetParent = typeToNode.TryGetValue(item.prefabType, out var tf) ? tf : null;
                    if (targetParent == null)
                    {
                        VegetationGenerator.LogMissingMappingOnce(item.prefabType);
                        var fallback = ResolveDefaultContainer(terrain);
                        if (fallback == null) continue;
                        targetParent = fallback;
                    }

                    var cfgLocal = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                    var reqL = new FacadeTraceRequest { Terrain = terrain, Start = center, Length = radius * 2f, ItemRef = item, Config = cfgLocal, Brush = bs };
                    var slicesL = FacadePathTracer.Trace(reqL);
                    if (slicesL != null)
                    {
                        for (int si = 0; si < slicesL.Count; si++)
                        {
                            FacadePlacementHandler.SpawnFacadeInstance(terrain, item, it, targetParent, slicesL[si], rnd);
                        }
                    }
                    continue;
                }
                else
                {
                    if (allocPerItem != null && allocPerItem[it] != null && allocPerItem[it].Count > 0)
                    {
                        candidates = new List<Vector2>(allocPerItem[it].Count);
                        for (int a = 0; a < allocPerItem[it].Count; a++)
                        {
                            int ci = allocPerItem[it][a];
                            var v2 = globalCandidates[ci];
                            candidates.Add(v2);
                        }
                    }
                    else
                    {
                        candidates = (bs.distribution == DistributionType.EdgeLine && item.prefabType == PrefabType.Landscape)
                            ? BuildFacadeStripCandidates(terrain, center, radius, bs, item)
                            : VegetationGenerator.BuildCandidates(centerXZ, radius, bs.shape, desired, spacing, bs.minSpacingJitter, seed + it,
                                bs.distribution, bs.useBurstPoisson, bs.cluster, bs.adaptiveMinFactor, bs.adaptiveMaxFactor, bs.adaptiveNoiseWeight, rnd);
                    }
                }

                if (globalCandidates == null && candidates != null && candidates.Count > 0 && bs.distribution != DistributionType.EdgeLine)
                {
                    float repelDist = Mathf.Max(Mathf.Max(item.CoreSpacing, item.CoreMinRadius), 0.01f) * 0.8f;
                    BrushEngineExtensions.ApplyRelaxation(candidates, centerXZ, radius, repelDist, 3);
                }

                var grid = GetSharedGrid(spacing);
                int placed = 0;
                NativeArray<float> outH = default;
                NativeArray<float3> outN = default;
                NativeArray<float> outS = default;

                if (!useFacade && candidates.Count > 0)
                {
                    // var pts = new NativeArray<float2>(candidates.Count, Allocator.TempJob);
                    // for (int iPt = 0; iPt < candidates.Count; iPt++) pts[iPt] = new float2(candidates[iPt].x, candidates[iPt].y);

                    outH = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                    outN = new NativeArray<float3>(candidates.Count, Allocator.TempJob);
                    outS = new NativeArray<float>(candidates.Count, Allocator.TempJob);

                    bool patchValid = hbShared.heights.IsCreated && hbShared.heights.Length >= (hbShared.width * hbShared.height) && hbShared.width > 0 && hbShared.height > 0;
                    if (patchValid)
                    {
                        if (allocPerItem != null && allocPerItem[it] != null && allocPerItem[it].Count > 0 && outHG.IsCreated)
                        {
                            for (int iPt = 0; iPt < candidates.Count; iPt++)
                            {
                                int ci = allocPerItem[it][iPt];
                                outH[iPt] = outHG[ci];
                                outN[iPt] = outNG[ci];
                                outS[iPt] = outSG[ci];
                            }
                        }
                        else
                        {
                            var pts = new NativeArray<float2>(candidates.Count, Allocator.TempJob);
                            for (int iPt = 0; iPt < candidates.Count; iPt++) pts[iPt] = new float2(candidates[iPt].x, candidates[iPt].y);
                            var job = new TerrainSampleJob
                            {
                                pointsWorld = pts,
                                heightsPatch = hbShared.heights,
                                xBase = hbShared.xBase,
                                zBase = hbShared.zBase,
                                width = hbShared.width,
                                height = hbShared.height,
                                hmMax = td.heightmapResolution - 1,
                                dxWorld = hbShared.dxWorld,
                                dzWorld = hbShared.dzWorld,
                                terrainPos = new float3(terrain.transform.position.x, terrain.transform.position.y, terrain.transform.position.z),
                                sizeX = td.size.x,
                                sizeZ = td.size.z,
                                sizeY = td.size.y,
                                outHeightLocal = outH,
                                outNormal = outN,
                                outSlope = outS,
                            };
                            job.Schedule(candidates.Count, 64).Complete();
                            pts.Dispose();
                        }
                    }
                    else
                    {
                        for (int i = 0; i < candidates.Count; i++) { outH[i] = 0f; outN[i] = new float3(0f, 1f, 0f); outS[i] = 0f; }
                    }
                }

                for (int ci = 0; ci < candidates.Count && placed < count; ci++)
                {
                    var c = candidates[ci];
                    Vector3 p = new Vector3(c.x, center.y, c.y);
                    if (!TerrainUtils.IsWithinTerrainBounds(terrain, p)) continue;

                    float h, slope;
                    Vector3 n;

                    // 安全读取，避免 NativeArray 越界
                    if (ci >= outH.Length || ci >= outN.Length || ci >= outS.Length)
                    {
                        Debug.LogErrorFormat("BrushPainter.Paint: sample index {0} out of range (H:{1}, N:{2}, S:{3}) on terrain '{4}'", ci, outH.Length, outN.Length, outS.Length, terrain != null ? terrain.name : "<null>");
                        continue;
                    }
                    h = outH[ci] + terrain.transform.position.y;
                    n = outN[ci];
                    slope = outS[ci];
                    p.y = h;

                    float dx0 = p.x - center.x;
                    float dz0 = p.z - center.z;
                    float t = Mathf.Clamp01(Mathf.Sqrt(dx0 * dx0 + dz0 * dz0) / radius);
                    float acceptance = bs.falloffCurve != null ? bs.falloffCurve.Evaluate(1f - t) : Mathf.Lerp(1f, (1f - t), Mathf.Clamp01(bs.hardness));
                    if (rnd.NextDouble() > acceptance) continue;

                    if (!VegetationGenerator.MatchTerrain(item, h - terrain.transform.position.y, slope, ov)) continue;

                    if (item.prefabType == PrefabType.Landscape && slope < Mathf.Clamp(item.edgeSlopeThreshold, 0f, 90f)) continue;

                    var p2 = new Vector2(p.x - terrain.transform.position.x, p.z - terrain.transform.position.z);
                    float minDist = spacing;
                    if (globalGridAll != null && globalGridAll.HasNearby(p2, globalSpacingAll)) continue;
                    if (bs.globalSpacingFactor > 0f && GetSharedGrid(spacing * bs.globalSpacingFactor).HasNearby(p2, spacing * bs.globalSpacingFactor)) continue;
                    if (grid.HasNearby(p2, minDist)) continue;

                    grid.Add(p2);
                    globalGridAll?.Add(p2);

                    var targetParent = typeToNode.TryGetValue(item.prefabType, out var tf) ? tf : null;
                    if (targetParent == null)
                    {
                        VegetationGenerator.LogMissingMappingOnce(item.prefabType);
                        var fallback = ResolveDefaultContainer(terrain);
                        if (fallback == null) continue;
                        targetParent = fallback;
                    }

                    var placementContext = new VegetationGenerator.PlacementContext
                    {
                        Item = item,
                        Position = p,
                        Normal = n,
                        Terrain = terrain,
                        ItemIndex = it,
                        Parent = targetParent,
                        Random = rnd,
                        Overrides = ov
                    };
                    VegetationGenerator.PlaceItem(placementContext);
                    placed++;
                }

                BrushEngine.ReleaseList(candidates);
                if (outH.IsCreated) outH.Dispose();
                if (outN.IsCreated) outN.Dispose();
                if (outS.IsCreated) outS.Dispose();
            }

            if (hbShared.heights.IsCreated) hbShared.heights.Dispose();
            if (outHG.IsCreated) outHG.Dispose();
            if (outNG.IsCreated) outNG.Dispose();
            if (outSG.IsCreated) outSG.Dispose();
        }

        /// <summary>
        /// 多Profile混合绘制方法（优化版：从6个参数简化为1个）
        /// </summary>
        public static void PaintMixed(PaintContext context)
        {
            if (context.Terrain == null || context.Profiles == null || context.Profiles.Count == 0) return;

            var allItems = new List<VegetationItem>();
            foreach (var p in context.Profiles)
            {
                if (p != null && !p.IsEmpty()) allItems.AddRange(p.Items.Where(i => i != null && i.IsValid()));
            }
            if (allItems.Count == 0) return;

            var tempProfile = ScriptableObject.CreateInstance<VegetationProfile>();
            foreach (var it in allItems) tempProfile.AddItem(it);

            bool originalMix = context.BrushSettings.mixItemsWeighted;
            context.BrushSettings.mixItemsWeighted = true;

            var paintContext = new PaintContext
            {
                Terrain = context.Terrain,
                Profile = tempProfile,
                Center = context.Center,
                BrushSettings = context.BrushSettings,
                Random = context.Random,
                Overrides = context.Overrides
            };
            Paint(paintContext);

            context.BrushSettings.mixItemsWeighted = originalMix;

            Object.DestroyImmediate(tempProfile);
        }

        /// <summary>
        /// 擦除方法（优化版：从5个参数简化为1个）
        /// 当context.Terrain为null时，使用activeTerrain
        /// </summary>
        public static void Erase(EraseContext context)
        {
            // 提取局部变量
            var terrain = context.Terrain;
            var center = context.Center;
            var bs = context.BrushSettings;
            var eraseAll = context.EraseAll;
            var onlyTypes = context.OnlyTypes;

            float radius = bs.size;

            // 如果未指定terrain，使用全局逻辑
            if (terrain == null)
            {
                var candidates = new List<GameObject>();
                var activeTerrain = Terrain.activeTerrain;
                if (activeTerrain != null)
                {
                    VegetationPool.QueryInRadius(activeTerrain, center, radius, candidates);
                }

                if (candidates.Count == 0)
                {
                    var hits = Physics.OverlapSphere(center, radius);
                    for (int i = 0; i < hits.Length; i++) candidates.Add(hits[i].gameObject);
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    var go = candidates[i];
                    var vi = go.GetComponent<VegetationInstance>();
                    if (vi == null) continue;

                    if (!eraseAll && onlyTypes != null && onlyTypes.Count > 0)
                    {
                        bool match = false;
                        for (int t = 0; t < onlyTypes.Count; t++)
                        {
                            if (onlyTypes[t] != null && go.name.StartsWith(onlyTypes[t].name)) { match = true; break; }
                        }
                        if (!match) continue;
                    }

                    VegetationPool.Recycle(vi.sourceTerrain, go, "Erase Vegetation Instance");
                }
                return;
            }

            // 指定terrain的逻辑
            var roots = new List<Transform>();
            var defaultContainer = terrain.transform.Find($"Vegetation_{terrain.name}");
            if (defaultContainer != null) roots.Add(defaultContainer);

            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            if (cfg != null && cfg.mappingEntries != null)
            {
                foreach (var entry in cfg.mappingEntries)
                {
                    if (entry != null && entry.node != null && !roots.Contains(entry.node))
                        roots.Add(entry.node);
                }
            }

            if (roots.Count == 0)
            {
                var fallbackContext = new EraseContext
                {
                    Terrain = null,
                    Center = center,
                    BrushSettings = bs,
                    EraseAll = eraseAll,
                    OnlyTypes = onlyTypes
                };
                Erase(fallbackContext);
                return;
            }

            var toRecycle = new List<GameObject>();
            foreach (var root in roots)
            {
                CollectInRadius(root, center, radius, eraseAll, onlyTypes, toRecycle);
            }

            foreach (var go in toRecycle)
            {
                var vi = go.GetComponent<VegetationInstance>();
                var src = vi != null ? vi.sourceTerrain : terrain;
                VegetationPool.Recycle(src, go, "Erase Vegetation Instance");
            }
        }
        #endregion

        // -----------------------------------------------------------------------
        // Private Helpers
        // -----------------------------------------------------------------------
        #region Helpers

        private static List<Vector2> BuildFacadeStripCandidates(Terrain terrain, Vector3 center, float radius, BrushSettings bs, VegetationItem item)
        {
            var list = BrushEngine.AcquireList(Mathf.CeilToInt((radius * 2f) / Mathf.Max(item != null ? item.CoreSpacing : 0.5f, 0.01f)) + 2);
            if (!TerrainUtils.TryGetHeightAndNormal(terrain, center, out _, out var n)) return list;

            var forward = Vector3.ProjectOnPlane(n, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            float step = item != null ? item.CoreSpacing : 0.5f;

            for (float d = -radius; d <= radius; d += step)
            {
                var p = center + right * d;
                list.Add(new Vector2(p.x, p.z));
            }
            return list;
        }

        private static void CollectInRadius(Transform root, Vector3 center, float radius, bool eraseAll, IReadOnlyList<GameObject> onlyTypes, List<GameObject> outList)
        {
            if (root == null) return;
            var stack = new Stack<Transform>();
            stack.Push(root);
            float r2 = radius * radius;

            while (stack.Count > 0)
            {
                var t = stack.Pop();
                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));

                var go = t.gameObject;
                var vi = go.GetComponent<VegetationInstance>();
                if (vi != null)
                {
                    if (Vector3.SqrMagnitude(t.position - center) <= r2)
                    {
                        if (eraseAll) outList.Add(go);
                        else if (onlyTypes != null)
                        {
                            bool match = false;
                            for (int k = 0; k < onlyTypes.Count; k++)
                                if (go.name.StartsWith(onlyTypes[k].name)) { match = true; break; }
                            if (match) outList.Add(go);
                        }
                        else outList.Add(go);
                    }
                }
            }
        }

        private static bool IsWithinBrush(Vector3 pos, Vector3 center, float radius, BrushShape shape)
        {
            float dx = pos.x - center.x;
            float dz = pos.z - center.z;
            if (shape == BrushShape.Circle) return (dx * dx + dz * dz) <= radius * radius;
            return Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius;
        }

        private static BrushSpatialGrid GetSharedGrid(float spacing) { if (s_sharedGrid == null) s_sharedGrid = new BrushSpatialGrid(spacing); else s_sharedGrid.Reset(spacing); return s_sharedGrid; }
        private static BrushSpatialGrid GetItemGrid(int idx, float spacing) { if (!s_itemGridCache.TryGetValue(idx, out var g)) { g = new BrushSpatialGrid(spacing); s_itemGridCache[idx] = g; } else g.Reset(spacing); return g; }

        private static Transform ResolveDefaultContainer(Terrain terrain)
        {
            if (terrain == null) return null;
            var name = $"Vegetation_{terrain.name}";
            var existing = terrain.transform.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(terrain.transform, false);
            return go.transform;
        }
        #endregion
    }
    #endregion
}

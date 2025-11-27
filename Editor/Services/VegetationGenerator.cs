using System;
using System.Collections.Generic;
using MrTerrainPainter.Editor.Utils;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using PrefabType = MrTerrainPainter.Runtime.Profiles.PrefabType;

namespace MrTerrainPainter.Editor.Services
{
    public static class VegetationGenerator
    {
        /// <summary>
        /// 物品放置上下文 - 封装PlaceItem的所有参数
        /// </summary>
        public struct PlacementContext
        {
            public VegetationItem Item;
            public Vector3 Position;
            public Vector3 Normal;
            public Terrain Terrain;
            public int ItemIndex;
            public Transform Parent;
            public System.Random Random;
            public PlacementOverrides? Overrides;
        }

        public class CandidateRequest
        {
            public UnityEngine.Vector2 centerXZ;
            public float radius;
            public BrushShape shape;
            public int desired;
            public float minSpacing;
            public float jitter;
            public int seed;
            public DistributionType distribution;
            public bool useBurst;
            public ClusterSettings cluster;
            public float minFactor;
            public float maxFactor;
            public float noiseWeight;
            public System.Random rnd;
        }

        public class CandidateBuilder
        {
            private readonly CandidateRequest r = new CandidateRequest();
            public CandidateBuilder Center(UnityEngine.Vector2 v) { r.centerXZ = v; return this; }
            public CandidateBuilder Radius(float v) { r.radius = v; return this; }
            public CandidateBuilder Shape(BrushShape v) { r.shape = v; return this; }
            public CandidateBuilder Desired(int v) { r.desired = v; return this; }
            public CandidateBuilder MinSpacing(float v) { r.minSpacing = v; return this; }
            public CandidateBuilder Jitter(float v) { r.jitter = v; return this; }
            public CandidateBuilder Seed(int v) { r.seed = v; return this; }
            public CandidateBuilder Distribution(DistributionType v) { r.distribution = v; return this; }
            public CandidateBuilder UseBurst(bool v) { r.useBurst = v; return this; }
            public CandidateBuilder Cluster(ClusterSettings v) { r.cluster = v; return this; }
            public CandidateBuilder MinFactor(float v) { r.minFactor = v; return this; }
            public CandidateBuilder MaxFactor(float v) { r.maxFactor = v; return this; }
            public CandidateBuilder NoiseWeight(float v) { r.noiseWeight = v; return this; }
            public CandidateBuilder Random(System.Random v) { r.rnd = v; return this; }
            public CandidateBuilder FromBrush(BrushSettings bs)
            {
                if (bs == null) return this;
                r.distribution = bs.distribution;
                r.useBurst = bs.useBurstPoisson;
                r.cluster = bs.cluster;
                r.minFactor = bs.adaptiveMinFactor;
                r.maxFactor = bs.adaptiveMaxFactor;
                r.noiseWeight = bs.adaptiveNoiseWeight;
                return this;
            }
            public CandidateRequest Build() { return r; }
        }
        public static bool UseBurstPoisson = true;
        private static readonly System.Collections.Generic.Dictionary<Runtime.Profiles.PrefabType, double> s_missingLogTimes = new();
        public static void LogMissingMappingOnce(Runtime.Profiles.PrefabType type, double throttleSecondsDefault = 3.0)
        {
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            double throttle = cfg != null ? cfg.missingMappingLogThrottleSeconds : throttleSecondsDefault;
            double now = EditorApplication.timeSinceStartup;
            if (s_missingLogTimes.TryGetValue(type, out var last))
            {
                if (now - last < throttle) return;
            }
            s_missingLogTimes[type] = now;
            string tmpl = cfg != null && !string.IsNullOrEmpty(cfg.missingMappingLogTemplate) ? cfg.missingMappingLogTemplate : "未找到父节点映射的类型: {0}";
            Debug.LogError(string.Format(tmpl, type));
            if (cfg != null && cfg.autoOpenSettingsOnMissingMapping)
            {
                MrTerrainPainter.Editor.MrTerrainPainterSettingsWindow.Open();
            }
        }

        public static int ComputeDesiredCandidateCount(BrushShape shape, float radius, float minSpacing, int requested, int maxPoints)
        {
            float area = shape == BrushShape.Circle ? (Mathf.PI * radius * radius) : ((radius * 2f) * (radius * 2f));
            float spacing = Mathf.Max(minSpacing, 0.01f);
            float capacity = area / (spacing * spacing);
            // 经验系数：允许一定冗余以保证分布算法充分
            int dynamicCap = Mathf.Clamp(Mathf.RoundToInt(capacity * 1.5f), 1, Mathf.Max(1, maxPoints));
            return Mathf.Clamp(requested, 1, dynamicCap);
        }
        public static System.Collections.Generic.List<Vector2> BuildCandidates(
            UnityEngine.Vector2 centerXZ,
            float radius,
            BrushShape shape,
            int desired,
            float minSpacing,
            float jitter,
            int seed,
            DistributionType type,
            bool useBurst,
            ClusterSettings cluster,
            float minFactor,
            float maxFactor,
            float noiseWeight,
            System.Random rnd)
        {
            switch (type)
            {
                case DistributionType.PoissonDisk:
                    return useBurst
                        ? BrushEngine.SamplePoissonBurst(centerXZ, radius, shape, desired, minSpacing, jitter, seed)
                        : BrushEngine.SamplePoisson(centerXZ, radius, shape, desired, minSpacing, jitter, seed);
                case DistributionType.Cluster:
                    return BrushEngine.SampleCluster(centerXZ, radius, shape, cluster, Mathf.Max(minSpacing, 0.01f), seed);
                case DistributionType.JitteredGrid:
                    return BrushEngine.SampleJittered(centerXZ, radius, shape, Mathf.Max(minSpacing, 0.01f), jitter, rnd);
                case DistributionType.Natural:
                    return BrushEngine.SampleNatural(centerXZ, radius, shape, desired, Mathf.Max(minSpacing, 0.01f), seed);
                case DistributionType.AdaptivePoisson:
                    return BrushEngine.SampleAdaptivePoisson(centerXZ, radius, shape, desired, Mathf.Max(minSpacing * Mathf.Max(0.1f, minFactor), 0.01f), minSpacing * Mathf.Max(minFactor, maxFactor), jitter, Mathf.Max(0.0001f, noiseWeight), seed);
                case DistributionType.Halton:
                    return BrushEngine.SampleHaltonUniform(centerXZ, radius, shape, desired, seed);
                case DistributionType.EdgeLine:
                    // 需要 Terrain 支持，使用专用方法；此处仅返回占位列表
                    return BrushEngine.AcquireList(desired);
                default:
                    return BrushEngine.SampleUniform(centerXZ, radius, shape, desired, rnd);
            }
        }

        public static System.Collections.Generic.List<Vector2> BuildCandidates(CandidateRequest req)
        {
            return BuildCandidates(req.centerXZ, req.radius, req.shape, req.desired, req.minSpacing, req.jitter, req.seed, req.distribution, req.useBurst, req.cluster, req.minFactor, req.maxFactor, req.noiseWeight, req.rnd);
        }

        public static System.Collections.Generic.List<Vector2> SampleEdgeLine(
            Terrain terrain,
            Vector3 center,
            float radius,
            BrushShape shape,
            float spacing,
            float slopeThreshold)
        {
            var list = new System.Collections.Generic.List<Vector2>(64);
            if (terrain == null) return list;
            float cell = Mathf.Max(spacing * 0.5f, 0.5f);
            int steps = Mathf.Max(2, Mathf.CeilToInt((radius * 2f) / cell));
            Vector3 c = center;
            float r = radius;
            for (int i = -steps; i < steps; i++)
            {
                for (int j = -steps; j < steps; j++)
                {
                    float x0 = c.x + (i) * cell;
                    float z0 = c.z + (j) * cell;
                    float x1 = c.x + (i + 1) * cell;
                    float z1 = c.z + (j + 1) * cell;
                    var p00 = new Vector3(x0, 0f, z0);
                    var p10 = new Vector3(x1, 0f, z0);
                    var p11 = new Vector3(x1, 0f, z1);
                    var p01 = new Vector3(x0, 0f, z1);
                    if (!PointInShape(new Vector2(p00.x, p00.z), new Vector2(c.x, c.z), r, shape) &&
                        !PointInShape(new Vector2(p11.x, p11.z), new Vector2(c.x, c.z), r, shape))
                    {
                        continue;
                    }
                    float v00 = GetSlopeValue(terrain, p00) - slopeThreshold;
                    float v10 = GetSlopeValue(terrain, p10) - slopeThreshold;
                    float v11 = GetSlopeValue(terrain, p11) - slopeThreshold;
                    float v01 = GetSlopeValue(terrain, p01) - slopeThreshold;
                    int mask = (v00 >= 0 ? 1 : 0) | (v10 >= 0 ? 2 : 0) | (v11 >= 0 ? 4 : 0) | (v01 >= 0 ? 8 : 0);
                    if (mask == 0 || mask == 15) continue;
                    Vector2 a, b;
                    if (TryMarch(p00, p10, v00, v10, out var e1) && TryMarch(p00, p01, v00, v01, out var e2))
                    { a = e1; b = e2; }
                    else if (TryMarch(p10, p11, v10, v11, out e1) && TryMarch(p10, p00, v10, v00, out e2))
                    { a = e1; b = e2; }
                    else if (TryMarch(p11, p10, v11, v10, out e1) && TryMarch(p11, p01, v11, v01, out e2))
                    { a = e1; b = e2; }
                    else if (TryMarch(p01, p00, v01, v00, out e1) && TryMarch(p01, p11, v01, v11, out e2))
                    { a = e1; b = e2; }
                    else continue;
                    float len = Vector2.Distance(a, b);
                    int count = Mathf.Max(1, Mathf.RoundToInt(len / Mathf.Max(spacing, 0.01f)));
                    for (int k = 0; k <= count; k++)
                    {
                        float t = (float)k / count;
                        var q = Vector2.Lerp(a, b, t);
                        if (PointInShape(q, new Vector2(c.x, c.z), r, shape)) list.Add(q);
                    }
                }
            }
            return list;
        }

        private static float GetSlopeValue(Terrain t, Vector3 p)
        {
            if (TerrainUtils.TryGetHeightAndNormal(t, p, out float h, out Vector3 n))
            {
                return TerrainUtils.ComputeSlope(n);
            }
            return 0f;
        }
        private static bool TryMarch(Vector3 a, Vector3 b, float va, float vb, out Vector2 q)
        {
            q = default;
            if ((va >= 0f) == (vb >= 0f)) return false;
            float t = va == vb ? 0.5f : Mathf.Clamp01(va / (va - vb));
            var p = Vector3.Lerp(a, b, t);
            q = new Vector2(p.x, p.z);
            return true;
        }
        private static bool PointInShape(Vector2 p, Vector2 c, float r, BrushShape s)
        {
            if (s == BrushShape.Circle) return (p - c).sqrMagnitude <= r * r;
            return Mathf.Abs(p.x - c.x) <= r && Mathf.Abs(p.y - c.y) <= r;
        }
        public static System.Collections.Generic.Dictionary<Runtime.Profiles.PrefabType, Transform> BuildTypeToNodeMapping()
        {
            var dict = new System.Collections.Generic.Dictionary<Runtime.Profiles.PrefabType, Transform>();
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            if (cfg != null && cfg.mappingEntries != null)
            {
                for (int i = 0; i < cfg.mappingEntries.Count; i++)
                {
                    var e = cfg.mappingEntries[i];
                    if (e == null || e.node == null) continue;
                    dict[e.type] = e.node;
                }
            }
            return dict;
        }

        [BurstCompile]
        private struct CandidateFilterJob : IJobParallelFor
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
            public float2 heightRange;
            public float2 slopeRange;
            public byte enableNoise;
            public float noiseScale;
            public int noiseSeed;
            public int noiseOctaves;
            public float noisePersistence;
            public float noiseLacunarity;
            public float noiseThreshold;
            public byte noiseInvert;

            public NativeArray<float> outHeightLocal;
            public NativeArray<float3> outNormal;
            public NativeArray<float> outSlope;
            public NativeArray<byte> outAccept;

            private float SampleHeight01(float2 uv)
            {
                float u = math.clamp(uv.x, 0f, hmMax);
                float v = math.clamp(uv.y, 0f, hmMax);
                int xi = (int)math.floor(u);
                int zi = (int)math.floor(v);
                int xi1 = math.min(xi + 1, xBase + width - 1);
                int zi1 = math.min(zi + 1, zBase + height - 1);
                xi = math.max(xi, xBase);
                zi = math.max(zi, zBase);
                float fu = u - xi;
                float fv = v - zi;
                int lx = xi - xBase;
                int lz = zi - zBase;
                int lx1 = xi1 - xBase;
                int lz1 = zi1 - zBase;
                float h00 = heightsPatch[lz * width + lx];
                float h10 = heightsPatch[lz * width + lx1];
                float h01 = heightsPatch[lz1 * width + lx];
                float h11 = heightsPatch[lz1 * width + lx1];
                float h0 = math.lerp(h00, h10, fu);
                float h1 = math.lerp(h01, h11, fu);
                return math.lerp(h0, h1, fv);
            }

            private float3 ComputeNormal(float2 uv)
            {
                float u = math.clamp(uv.x, 1f, hmMax - 1f);
                float v = math.clamp(uv.y, 1f, hmMax - 1f);
                int xi = (int)math.floor(u);
                int zi = (int)math.floor(v);
                int lxC = math.clamp(xi - xBase, 0, width - 1);
                int lzC = math.clamp(zi - zBase, 0, height - 1);
                int lxL = math.max(lxC - 1, 0);
                int lxR = math.min(lxC + 1, width - 1);
                int lzU = math.max(lzC - 1, 0);
                int lzD = math.min(lzC + 1, height - 1);
                float hL = heightsPatch[lzC * width + lxL];
                float hR = heightsPatch[lzC * width + lxR];
                float hU = heightsPatch[lzU * width + lxC];
                float hD = heightsPatch[lzD * width + lxC];
                float dhdx = ((hR - hL) * sizeY) / (2f * dxWorld);
                float dhdz = ((hD - hU) * sizeY) / (2f * dzWorld);
                float3 n = math.normalizesafe(new float3(-dhdx, 1f, -dhdz));
                return n;
            }

            private float SampleSlope(float3 n)
            {
                float cosTheta = math.clamp(n.y, -1f, 1f);
                float theta = math.acos(cosTheta);
                return theta * 57.2957795f;
            }

            private float SampleNoise(float2 p)
            {
                float2 off = new float2(123.456f * noiseSeed, 789.012f * noiseSeed);
                p = p / math.max(noiseScale, 0.0001f) + off;
                float amp = 1f;
                float freq = 1f;
                float sum = 0f;
                float norm = 0f;
                for (int o = 0; o < math.max(noiseOctaves, 1); o++)
                {
                    float v = noise.snoise(p * freq);
                    v = (v + 1f) * 0.5f;
                    sum += v * amp;
                    norm += amp;
                    amp *= noisePersistence;
                    freq *= noiseLacunarity;
                }
                float nv = norm <= 0f ? 0f : sum / norm;
                if (noiseInvert != 0) nv = 1f - nv;
                return nv;
            }

            public void Execute(int index)
            {
                float2 pw = pointsWorld[index];
                float2 pl = new float2(pw.x - terrainPos.x, pw.y - terrainPos.z);
                float2 uv = new float2((pl.x / sizeX) * hmMax, (pl.y / sizeZ) * hmMax);
                float h01 = SampleHeight01(uv);
                float hWorld = h01 * sizeY;
                float3 n = ComputeNormal(uv);
                float slope = SampleSlope(n);
                outHeightLocal[index] = hWorld;
                outNormal[index] = n;
                outSlope[index] = slope;
                byte ok = 1;
                if (hWorld < heightRange.x || hWorld > heightRange.y) ok = 0;
                if (slope < slopeRange.x || slope > slopeRange.y) ok = 0;
                if (enableNoise != 0)
                {
                    float nv = SampleNoise(pw);
                    if (nv < noiseThreshold) ok = 0;
                }
                outAccept[index] = ok;
            }
        }
        // 生成过滤参数：仅噪声（贴图过滤已移除）
        public class NoiseSettings
        {
            public bool enabled = false;
            public float scale = 20f; // 世界坐标缩放
            public int octaves = 3;
            public float persistence = 0.5f;
            public float lacunarity = 2f;
            public int seed = 0;
            public bool invert = false;
            public float threshold = 0.35f; // 0-1，门限
        }

        public class FilterSettings
        {
            public NoiseSettings noise = new NoiseSettings();
            public DistributionType distribution = DistributionType.PoissonDisk;
            public BrushShape shape = BrushShape.Square;
            public float minSpacingJitter = 0f;
            public int maxPoints = 50000;
            public ClusterSettings cluster = new ClusterSettings { clusterCount = 10, childPerCluster = 5, clusterRadius = 2f, childJitter = 0.2f };
            public float adaptiveMinFactor = 0.7f;
            public float adaptiveMaxFactor = 1.8f;
            public float adaptiveNoiseWeight = 1f;
        }

        // 放置范围覆盖：替代从Profile SO读取的范围
        public struct PlacementOverrides
        {
            public Vector2 scaleRange;
            public Vector2 yRotationRange;
            public Vector2 heightRange;
            public Vector2 slopeRange;
        }

        private class Grid
        {
            private readonly float cellSize;
            private readonly Dictionary<(int, int), List<Vector2>> cells = new();

            public Grid(float spacing)
            {
                cellSize = Mathf.Max(spacing, 0.01f);
            }

            private (int, int) Key(Vector2 p)
            {
                return (Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize));
            }

            public void Add(Vector2 p)
            {
                var k = Key(p);
                if (!cells.TryGetValue(k, out var list))
                {
                    list = new List<Vector2>();
                    cells[k] = list;
                }
                list.Add(p);
            }

            public bool HasNearby(Vector2 p, float minDist)
            {
                var k = Key(p);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var nk = (k.Item1 + dx, k.Item2 + dy);
                        if (!cells.TryGetValue(nk, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (Vector2.SqrMagnitude(list[i] - p) < minDist * minDist) return true;
                        }
                    }
                return false;
            }
        }

        public static void GenerateOnTerrains(IReadOnlyList<Terrain> terrains, VegetationProfile profile, Bounds? area = null)
        {
            if (terrains == null || terrains.Count == 0) return; // 提前返回
            if (profile == null || profile.IsEmpty()) return; // 提前返回

            var rnd = new System.Random(profile.randomSeed);

            foreach (var terrain in terrains)
            {
                if (terrain == null) continue;
                GenerateOnTerrain(terrain, profile, area, rnd, null, null);
            }
        }

        // 支持过滤的批量生成
        public static void GenerateOnTerrains(IReadOnlyList<Terrain> terrains, VegetationProfile profile, Bounds? area, FilterSettings filter)
        {
            if (terrains == null || terrains.Count == 0) return; // 提前返回
            if (profile == null || profile.IsEmpty()) return; // 提前返回

            var rnd = new System.Random(profile.randomSeed);

            foreach (var terrain in terrains)
            {
                if (terrain == null) continue;
                GenerateOnTerrain(terrain, profile, area, rnd, filter, null);
            }
        }

        // 支持过滤与放置覆盖的批量生成
        public static void GenerateOnTerrains(IReadOnlyList<Terrain> terrains, VegetationProfile profile, Bounds? area, FilterSettings filter, PlacementOverrides ov)
        {
            if (terrains == null || terrains.Count == 0) return; // 提前返回
            if (profile == null || profile.IsEmpty()) return; // 提前返回

            var rnd = new System.Random(profile.randomSeed);

            foreach (var terrain in terrains)
            {
                if (terrain == null) continue;
                GenerateOnTerrain(terrain, profile, area, rnd, filter, ov);
            }
        }

        // 在场景中以笔刷圆形范围进行区域生成（每个地形根据其世界Y校正Bounds中心）
        public static void GenerateInBrushArea(IReadOnlyList<Terrain> terrains, VegetationProfile profile, Vector3 center, float radius)
        {
            if (terrains == null || terrains.Count == 0) return; // 提前返回
            if (profile == null || profile.IsEmpty()) return; // 提前返回

            var rnd = new System.Random(profile.randomSeed);
            foreach (var terrain in terrains)
            {
                if (terrain == null) continue;
                var y = terrain.transform.position.y;
                var area = new Bounds(new Vector3(center.x, y, center.z), new Vector3(radius * 2f, 1f, radius * 2f));
                GenerateOnTerrain(terrain, profile, area, rnd, null, null);
            }
        }

        public static void GenerateInBrushArea(IReadOnlyList<Terrain> terrains, VegetationProfile profile, Vector3 center, float radius, FilterSettings filter)
        {
            if (terrains == null || terrains.Count == 0) return; // 提前返回
            if (profile == null || profile.IsEmpty()) return; // 提前返回

            var rnd = new System.Random(profile.randomSeed);
            foreach (var terrain in terrains)
            {
                if (terrain == null) continue;
                var y = terrain.transform.position.y;
                var area = new Bounds(new Vector3(center.x, y, center.z), new Vector3(radius * 2f, 1f, radius * 2f));
                GenerateOnTerrain(terrain, profile, area, rnd, filter, null);
            }
        }

        public static void GenerateInBrushArea(IReadOnlyList<Terrain> terrains, VegetationProfile profile, Vector3 center, float radius, FilterSettings filter, PlacementOverrides ov)
        {
            if (terrains == null || terrains.Count == 0) return; // 提前返回
            if (profile == null || profile.IsEmpty()) return; // 提前返回

            var rnd = new System.Random(profile.randomSeed);
            foreach (var terrain in terrains)
            {
                if (terrain == null) continue;
                var y = terrain.transform.position.y;
                var area = new Bounds(new Vector3(center.x, y, center.z), new Vector3(radius * 2f, 1f, radius * 2f));
                GenerateOnTerrain(terrain, profile, area, rnd, filter, ov);
            }
        }

        private static void GenerateOnTerrain(Terrain terrain, VegetationProfile profile, Bounds? area, System.Random rnd, FilterSettings filter, PlacementOverrides? ov)
        {
            var td = terrain.terrainData;
            if (td == null) return; // 提前返回

            var size = td.size;
            var worldPos = terrain.transform.position;

            // 条目级最小间距：为每个条目使用独立网格与间距
            var grids = new Dictionary<int, Grid>();

            var items = profile.Items;
            if (items == null || items.Count == 0) return; // 提前返回

            // 采样区域面积（XZ）
            float areaX = area.HasValue ? area.Value.size.x : size.x;
            float areaZ = area.HasValue ? area.Value.size.z : size.z;

            // 缺少映射的类型仅报错一次，避免刷屏
            // 统一日志入口：去重与节流
            for (int it = 0; it < items.Count; it++)
            {
                var item = items[it];
                if (item == null || !item.IsValid()) continue;

                // 二次细分：在条目密度的基础上叠加条目权重，保持类型比例直觉一致
                // 注意：仍以条目baseDensity为主驱动，weight作为细化因子避免违背“按条目密度严格计数”的要求
                int count = Mathf.RoundToInt(item.baseDensity * (areaX * areaZ) * 0.001f * Mathf.Max(item.weight, 0f));
                count = Mathf.Clamp(count, 0, 50000);
                if (count <= 0) continue;

                float spacing = Mathf.Max(item.CoreSpacing, 0.01f);
                if (!grids.TryGetValue(it, out var gridForItem)) { gridForItem = new Grid(spacing); grids[it] = gridForItem; }

                Vector2 centerLocal;
                float radiusLocal;
                BrushShape shape = BrushShape.Square;
                if (area.HasValue)
                {
                    var a = area.Value;
                    centerLocal = new Vector2(a.center.x - worldPos.x, a.center.z - worldPos.z);
                    radiusLocal = Mathf.Max(a.extents.x, a.extents.z);
                }
                else
                {
                    centerLocal = new Vector2(size.x * 0.5f, size.z * 0.5f);
                    radiusLocal = Mathf.Min(size.x, size.z) * 0.5f;
                }
                var dShape = filter != null ? filter.shape : BrushShape.Square;
                var dType = filter != null ? filter.distribution : DistributionType.PoissonDisk;
                var jitter = filter != null ? filter.minSpacingJitter : 0f;
                int maxPts = filter != null ? Mathf.Max(1, filter.maxPoints) : 50000;
                int requested = Mathf.Min(count, maxPts);
                int desired = ComputeDesiredCandidateCount(dShape, radiusLocal, spacing, requested, maxPts);
                float minFlt = filter != null ? filter.adaptiveMinFactor : 0.7f;
                float maxFlt = filter != null ? filter.adaptiveMaxFactor : 1.8f;
                float noiseWgt = filter != null ? filter.adaptiveNoiseWeight : 1f;
                List<Vector2> candidates = BuildCandidates(
                    centerLocal,
                    radiusLocal,
                    dShape,
                    desired,
                    spacing,
                    jitter,
                    profile.randomSeed + it,
                    dType,
                    UseBurstPoisson,
                    filter != null ? filter.cluster : new ClusterSettings { clusterCount = 10, childPerCluster = 5, clusterRadius = 2f, childJitter = 0.2f },
                    minFlt,
                    maxFlt,
                    noiseWgt,
                    rnd);
                if (dType == DistributionType.EdgeLine)
                {
                    var centerWorld = new Vector3(worldPos.x + centerLocal.x, worldPos.y, worldPos.z + centerLocal.y);
                    if (item.prefabType == PrefabType.Landscape)
                    {
                        if (MrTerrainPainter.Editor.Services.FacadeDetectionService.TryDetectFacade(terrain, centerWorld, item.edgeSlopeEnter, item.edgeSlopeExit, item.probeStep, item.probeMaxDist, out var info))
                        {
                            candidates = new List<Vector2>();
                            float length = radiusLocal * 2f;
                            float stepU = Mathf.Max(item.CoreSpacing, 0.01f);
                            for (float u = -length * 0.5f; u <= length * 0.5f + 0.0001f; u += stepU)
                            {
                                var p = info.bottomPos + info.right * u;
                                candidates.Add(new Vector2(p.x - worldPos.x, p.z - worldPos.z));
                            }
                        }
                        else
                        {
                            candidates = new List<Vector2>(0);
                        }
                    }
                    else
                    {
                        var nCenter = Vector3.up;
                        if (TerrainUtils.TryGetHeightAndNormal(terrain, centerWorld, out var hC, out var nC)) nCenter = nC;
                        var forward = Vector3.ProjectOnPlane(nCenter, Vector3.up);
                        if (forward.sqrMagnitude > 1e-6f)
                        {
                            forward.Normalize();
                            var right = Vector3.Cross(Vector3.up, forward);
                            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
                            right.Normalize();
                            forward = Vector3.Normalize(Vector3.Cross(right, Vector3.up));
                            candidates = new List<Vector2>();
                            float stepU = Mathf.Max(spacing, 0.01f);
                            float rail = Mathf.Max(items[it].edgeReferenceWidthMeters, 0.01f) * 0.5f;
                            for (float u = -radiusLocal; u <= radiusLocal + 0.0001f; u += stepU)
                            {
                                var pL = centerWorld + right * (u - rail);
                                var pR = centerWorld + right * (u + rail);
                                candidates.Add(new Vector2(pL.x - worldPos.x, pL.z - worldPos.z));
                                candidates.Add(new Vector2(pR.x - worldPos.x, pR.z - worldPos.z));
                            }
                        }
                        else
                        {
                            candidates = new List<Vector2>(0);
                        }
                    }
                }
                if (dType == DistributionType.EdgeLine && item.prefabType == PrefabType.Landscape)
                {
                    var parent = ResolveTargetParent(terrain, item);
                    if (parent == null) { LogMissingMappingOnce(item.prefabType); BrushEngine.ReleaseList(candidates); continue; }
                    var centerWorldUnified = new Vector3(worldPos.x + centerLocal.x, worldPos.y, worldPos.z + centerLocal.y);
                    FacadeDetectionService.ProcessFacadeAndPlace(terrain, centerWorldUnified, radiusLocal, item, dShape, s =>
                    {
                        var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
                        float h = s.Height;
                        float refH = Mathf.Max(item.edgeReferenceHeightMeters, 0.0001f);
                        bool stacking = item.edgeStacking && h > refH * 1.5f;
                        if (!stacking)
                        {
                            var go = VegetationPool.Get(terrain, item, it, parent, "Create Vegetation Instance");
                            if (go == null) return;
                            go.transform.position = s.BottomPosition;
                            go.transform.rotation = Quaternion.LookRotation(s.Normal, s.Direction);
                            float rendererH = PrefabMetricsCache.GetPrefabHeightMeters(item.prefab);
                            var cfgLocal = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                            float minH = cfgLocal != null ? Mathf.Max(0.0001f, cfgLocal.minFacadeHeightMeters) : 0.0001f;
                            float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), h / Mathf.Max(0.0001f, rendererH));
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
                            vi.sourceTerrain = terrain;
                            vi.profileItemIndex = it;
                            vi.sourcePrefabName = item.prefab.name;
                            VegetationPool.IndexRegister(terrain, go);
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
                                var go = VegetationPool.Get(terrain, item, it, parent, "Create Vegetation Instance");
                                if (go == null) continue;
                                var basePos = s.BottomPosition + s.Direction * (per * L + Mathf.Max(0f, item.edgeStackingOffsetMeters));
                                go.transform.position = basePos;
                                go.transform.rotation = Quaternion.LookRotation(s.Normal, s.Direction);
                                float rendererH2 = PrefabMetricsCache.GetPrefabHeightMeters(item.prefab);
                                var cfgLocal2 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                                float minH2 = cfgLocal2 != null ? Mathf.Max(0.0001f, cfgLocal2.minFacadeHeightMeters) : 0.0001f;
                                float uni2 = Mathf.Max(minH2 / Mathf.Max(0.0001f, rendererH2), currH / Mathf.Max(0.0001f, rendererH2));
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
                                vi.sourceTerrain = terrain;
                                vi.profileItemIndex = it;
                                vi.sourcePrefabName = item.prefab.name;
                                VegetationPool.IndexRegister(terrain, go);
                            }
                        }
                    });
                    BrushEngine.ReleaseList(candidates);
                }
                else
                {
                    var areaWorld = new Bounds(new Vector3(worldPos.x + centerLocal.x, worldPos.y, worldPos.z + centerLocal.y), new Vector3(radiusLocal * 2f, 1f, radiusLocal * 2f));
                    var hb = TerrainUtils.FetchHeightsBlock(terrain, areaWorld, Allocator.TempJob);
                    var pts = new NativeArray<float2>(candidates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    for (int iPt = 0; iPt < candidates.Count; iPt++)
                    {
                        var p2 = candidates[iPt];
                        pts[iPt] = new float2(worldPos.x + p2.x, worldPos.z + p2.y);
                    }
                    var outH = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                    var outN = new NativeArray<float3>(candidates.Count, Allocator.TempJob);
                    var outS = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                    var outA = new NativeArray<byte>(candidates.Count, Allocator.TempJob);

                    var hr = ov.HasValue ? ov.Value.heightRange : item.heightRange;
                    var sr = ov.HasValue ? ov.Value.slopeRange : item.slopeRange;
                    bool noiseEnabled = filter != null && filter.noise != null && filter.noise.enabled;
                    var job = new CandidateFilterJob
                    {
                        pointsWorld = pts,
                        heightsPatch = hb.heights,
                        xBase = hb.xBase,
                        zBase = hb.zBase,
                        width = hb.width,
                        height = hb.height,
                        hmMax = terrain.terrainData.heightmapResolution - 1,
                        dxWorld = hb.dxWorld,
                        dzWorld = hb.dzWorld,
                        terrainPos = new float3(worldPos.x, worldPos.y, worldPos.z),
                        sizeX = terrain.terrainData.size.x,
                        sizeZ = terrain.terrainData.size.z,
                        sizeY = terrain.terrainData.size.y,
                        heightRange = new float2(hr.x, hr.y),
                        slopeRange = new float2(sr.x, sr.y),
                        enableNoise = (byte)(noiseEnabled ? 1 : 0),
                        noiseScale = noiseEnabled ? Mathf.Max(filter.noise.scale, 0.0001f) : 1f,
                        noiseSeed = noiseEnabled ? filter.noise.seed : 0,
                        noiseOctaves = noiseEnabled ? Mathf.Max(filter.noise.octaves, 1) : 1,
                        noisePersistence = noiseEnabled ? filter.noise.persistence : 1f,
                        noiseLacunarity = noiseEnabled ? filter.noise.lacunarity : 2f,
                        noiseThreshold = noiseEnabled ? Mathf.Clamp01(filter.noise.threshold) : 0f,
                        noiseInvert = (byte)(noiseEnabled && filter.noise.invert ? 1 : 0),
                        outHeightLocal = outH,
                        outNormal = outN,
                        outSlope = outS,
                        outAccept = outA,
                    };
                    var handle = job.Schedule(candidates.Count, 64);
                    handle.Complete();

                    for (int s = 0; s < candidates.Count; s++)
                    {
                        if (outA[s] == 0) continue;
                        var p2 = candidates[s];
                        float fx = p2.x;
                        float fz = p2.y;
                        float heightLocal = outH[s];
                        var n = (Vector3)outN[s];
                        float slope = outS[s];
                        Vector3 sample = new Vector3(worldPos.x + fx, worldPos.y + heightLocal, worldPos.z + fz);
                        if (item.prefabType == Runtime.Profiles.PrefabType.Landscape)
                        {
                            if (slope < Mathf.Clamp(item.edgeSlopeThreshold, 0f, 90f)) continue;
                            float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                            var horiz = Vector3.ProjectOnPlane(-n.normalized, Vector3.up);
                            if (horiz.sqrMagnitude > 1e-6f)
                            {
                                horiz.Normalize();
                                var offset = horiz * depth;
                                sample = new Vector3(sample.x + offset.x, worldPos.y + heightLocal, sample.z + offset.z);
                                heightLocal = sample.y - worldPos.y;
                            }
                        }

                        var p2Local = new Vector2(fx, fz);
                        if (gridForItem.HasNearby(p2Local, spacing)) continue;
                        gridForItem.Add(p2Local);

                        var targetParent = ResolveTargetParent(terrain, item);
                        if (targetParent == null) { LogMissingMappingOnce(item.prefabType); continue; }

                        var placementContext = new PlacementContext
                        {
                            Item = item,
                            Position = sample,
                            Normal = n,
                            Terrain = terrain,
                            ItemIndex = it,
                            Parent = targetParent,
                            Random = rnd,
                            Overrides = ov
                        };
                        PlaceItem(placementContext);
                    }

                    hb.heights.Dispose();
                    pts.Dispose();
                    outH.Dispose();
                    outN.Dispose();
                    outS.Dispose();
                    outA.Dispose();
                    BrushEngine.ReleaseList(candidates);
                }
            }
        }


        private static float FractalNoise(Vector2 p, NoiseSettings ns)
        {
            // 归一化到0-1
            float amp = 1f;
            float freq = 1f / Mathf.Max(ns.scale, 0.0001f);
            float sum = 0f;
            float norm = 0f;
            var offset = new Vector2(123.456f * ns.seed, 789.012f * ns.seed);
            p += offset;
            for (int o = 0; o < Mathf.Max(ns.octaves, 1); o++)
            {
                float v = Mathf.PerlinNoise(p.x * freq, p.y * freq);
                sum += v * amp;
                norm += amp;
                amp *= ns.persistence;
                freq *= ns.lacunarity;
            }
            if (norm <= 0f) return 0f;
            return Mathf.Clamp01(sum / norm);
        }

        public static bool MatchTerrain(VegetationItem item, float heightLocal, float slope, PlacementOverrides? ov)
        {
            if (item == null) return false;
            var hr = ov.HasValue ? ov.Value.heightRange : item.heightRange;
            var sr = ov.HasValue ? ov.Value.slopeRange : item.slopeRange;
            if (heightLocal < hr.x || heightLocal > hr.y) return false;
            if (slope < sr.x || slope > sr.y) return false;
            return true;
        }

        // —— 父节点解析：仅使用设置页的 Object + PrefabType 映射（不回退地形容器） ——

        public static Transform ResolveTargetParent(Terrain terrain, VegetationItem item)
        {
            if (terrain == null || item == null) return null;
            var dict = BuildTypeToNodeMapping();
            return dict.TryGetValue(item.prefabType, out var tf) ? tf : null;
        }

        /// <summary>
        /// 放置单个植被实例（优化版：从8个参数简化为1个）
        /// </summary>
        public static void PlaceItem(PlacementContext context)
        {
            if (context.Item.prefab == null) return; // 提前返回
            // 优先复用对象池，避免大量实例化导致卡顿
            var go = VegetationPool.Get(context.Terrain, context.Item, context.ItemIndex, context.Parent, "Create Vegetation Instance");
            if (go == null) return; // 提前返回
            go.transform.position = context.Position;

            // 严格使用条目级范围，确保配置的缩放与旋转生效
            float scale;
            if (context.Overrides.HasValue)
            {
                var r = context.Overrides.Value.scaleRange;
                float t = (float)context.Random.NextDouble();
                scale = Mathf.Lerp(r.x, r.y, t);
            }
            else
            {
                scale = context.Item.CoreScale;
            }
            go.transform.localScale = Vector3.one * scale;

            float yRot = context.Item.prefabType == Runtime.Profiles.PrefabType.Landscape ? 0f : context.Item.SampleYRotation(context.Random);
            var rot = Quaternion.Euler(0f, yRot, 0f);
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            bool useNormal = cfg != null ? (cfg.normalDirection || context.Item.alignToTerrainNormal || context.Item.prefabType == Runtime.Profiles.PrefabType.Landscape) : (context.Item.alignToTerrainNormal || context.Item.prefabType == Runtime.Profiles.PrefabType.Landscape);
            if (context.Item.prefabType == Runtime.Profiles.PrefabType.Landscape)
            {
                var forward = context.Normal.normalized;
                var upOnPlane = Vector3.ProjectOnPlane(Vector3.up, forward);
                if (upOnPlane.sqrMagnitude < 1e-6f) upOnPlane = Vector3.Cross(forward, Vector3.right).normalized;
                var baseRot = Quaternion.LookRotation(forward, upOnPlane);
                rot = Quaternion.AngleAxis(yRot, forward) * baseRot;
            }
            else if (useNormal)
            {
                rot = Quaternion.LookRotation(Vector3.Cross(Vector3.right, context.Normal), context.Normal) * Quaternion.Euler(0f, yRot, 0f);
            }
            go.transform.rotation = rot;
            if (context.Item.prefabType == Runtime.Profiles.PrefabType.Landscape && context.Item.edgeAutoHeight)
            {
                var up = Vector3.up;
                var forward = Vector3.ProjectOnPlane(context.Normal, up);
                if (forward.sqrMagnitude > 1e-6f)
                {
                    forward.Normalize();
                    float hFoot = go.transform.position.y;
                    float heightMeters = 0f;
                    float step = Mathf.Max(context.Item.edgeLookAheadStep, 0.05f);
                    float maxD = Mathf.Max(context.Item.edgeMaxLookAhead, step);
                    for (float d = step; d <= maxD + 0.0001f; d += step)
                    {
                        var test = go.transform.position + (-forward) * d;
                        if (TerrainUtils.TryGetHeightAndNormal(context.Terrain, test, out float hTop, out Vector3 nTop))
                        {
                            float sTop = TerrainUtils.ComputeSlope(nTop);
                            if (sTop < Mathf.Clamp(context.Item.edgeSlopeThreshold, 0f, 90f)) { heightMeters = Mathf.Max(0f, hTop - hFoot); break; }
                        }
                    }
                    float baseScale = go.transform.localScale.x;
                    float yScale = baseScale;
                    if (heightMeters > 0f)
                    {
                        yScale = heightMeters / Mathf.Max(context.Item.edgeReferenceHeightMeters, 0.0001f);
                    }
                    go.transform.localScale = new Vector3(baseScale, yScale, baseScale);
                    var right = Vector3.Cross(up, forward).normalized;
                    var horizFwd = forward;
                    var offsConf = context.Item.CoreOffset;
                    var off = right * offsConf.x + up * offsConf.y + (-horizFwd) * offsConf.z;
                    go.transform.position += off;
                }
            }

            var vi = go.GetComponent<VegetationInstance>();
            if (vi == null) vi = go.AddComponent<VegetationInstance>();
            vi.sourceTerrain = context.Terrain;
            vi.profileItemIndex = context.ItemIndex;
            vi.instanceId = Guid.NewGuid().ToString();
            vi.sourcePrefabName = context.Item.prefab.name;
            VegetationPool.IndexRegister(context.Terrain, go);
        }
    }
}

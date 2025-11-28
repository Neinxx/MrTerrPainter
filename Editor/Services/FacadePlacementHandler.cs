using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MrTerrainPainter.Runtime.Profiles;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Editor.Utils;

namespace MrTerrainPainter.Editor.Services
{
    /// <summary>
    /// 立面（Facade）放置处理器，负责在悬崖/立面上放置物体
    /// </summary>
    public static class FacadePlacementHandler
    {
        public class Handler : IPlacementHandler
        {
            public bool CanHandle(VegetationItem item, BrushSettings bs)
            {
                return item != null && item.prefabType == PrefabType.Landscape && bs != null && bs.distribution == DistributionType.EdgeLine;
            }
            public void Paint(PaintContext context)
            {
                var profile = context.Profile;
                if (profile == null || profile.IsEmpty()) return;
                var items = profile.Items;
                var landItems = new System.Collections.Generic.List<VegetationItem>();
                for (int i = 0; i < items.Count; i++) { var it = items[i]; if (it != null && it.IsValid() && it.prefabType == PrefabType.Landscape) landItems.Add(it); }
                if (landItems.Count == 0) return;
                var typeToNode = VegetationGenerator.BuildTypeToNodeMapping();
                var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? Config.ConfigTools.GetCachedConfig();
                var req = new FacadeTraceRequest
                {
                    Terrain = context.Terrain,
                    Start = context.Center,
                    Length = context.BrushSettings.size * 2f,
                    ItemRef = landItems[0],
                    Config = cfg,
                    Brush = context.BrushSettings
                };
                var slices = FacadePathTracer.Trace(req);
                if (slices == null || slices.Count == 0) return;
                PlaceEdgeLineWithPipeline(context.Terrain, context.Center, context.BrushSettings.size, context.BrushSettings, landItems, typeToNode, slices, context.Random);
            }
        }
        /// <summary>
        /// 在立面切片上生成单个实例
        /// </summary>
        public static void SpawnFacadeInstance(Terrain terrain, VegetationItem item, int itemIndex, Transform parent, FacadeDetectionService.CliffSlice slice, System.Random rnd)
        {
            var go = VegetationPool.Get(terrain, item, itemIndex, parent, "Create Facade Instance");
            if (go == null) return;

            go.transform.position = slice.BottomPosition;
            go.transform.rotation = Quaternion.LookRotation(slice.Normal, slice.Direction);

            float rendererH = PrefabMetricsCache.GetPrefabHeightMeters(item.prefab);
            var cfgLocal = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? ConfigTools.GetCachedConfig();
            float minH = cfgLocal != null ? Mathf.Max(0.0001f, cfgLocal.minFacadeHeightMeters) : 0.0001f;
            float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), slice.Height / Mathf.Max(0.0001f, rendererH));

            var baseScale = new Vector3(uni, uni, uni);
            var finalScale = new Vector3(
                Mathf.Max(0.0001f, baseScale.x + item.facadeScaleOffset.x),
                Mathf.Max(0.0001f, baseScale.y + item.facadeScaleOffset.y),
                Mathf.Max(0.0001f, baseScale.z + item.facadeScaleOffset.z));

            go.transform.localScale = finalScale;
            float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
            var rightAxis = Vector3.Normalize(Vector3.Cross(slice.Direction, slice.Normal));
            var off = rightAxis * item.offsets.x + slice.Direction * item.offsets.y + (-slice.Normal.normalized) * (depth + Mathf.Max(0f, item.offsets.z));
            go.transform.position += off;

            var vi = go.GetComponent<VegetationInstance>() ?? go.AddComponent<VegetationInstance>();
            vi.sourceTerrain = terrain;
            vi.profileItemIndex = itemIndex;
            vi.sourcePrefabName = item.prefab.name;

            VegetationPool.IndexRegister(terrain, go);
        }

        /// <summary>
        /// 使用流水线在边缘线上放置物体
        /// </summary>
        public static void PlaceEdgeLineWithPipeline(
            Terrain terrain, Vector3 center, float radius, BrushSettings bs,
            List<VegetationItem> landItems, Dictionary<PrefabType, Transform> typeToNode,
            List<FacadeDetectionService.CliffSlice> slices, System.Random rnd)
        {
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? ConfigTools.GetCachedConfig();
            float mixMinSpacing = 0.01f;
            if (landItems != null && landItems.Count > 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < landItems.Count; i++)
                {
                    var li = landItems[i];
                    float s = Mathf.Max(Mathf.Max(li.CoreSpacing, li.CoreMinRadius), 0.01f);
                    if (s < best) best = s;
                }
                mixMinSpacing = best == float.MaxValue ? 0.01f : best;
            }

            var parent = typeToNode.TryGetValue(PrefabType.Landscape, out var tf) ? tf : terrain != null ? terrain.transform : null;
            if (parent == null) return;

            var cfgUi = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? ConfigTools.GetCachedConfig();
            var useArc = cfgUi != null && cfgUi.useSimplifiedFacadeUI;
            // 仅保留刷区内且法线朝离刷心方向的切片，避免“双面对面”
            var pruned = new List<FacadeDetectionService.CliffSlice>(slices.Count);
            float minSlopeDeg = 0f;
            for (int i = 0; i < landItems.Count; i++) minSlopeDeg = Mathf.Max(minSlopeDeg, Mathf.Clamp(landItems[i].edgeSlopeThreshold, 0f, 90f));
            for (int i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                if (!TerrainUtils.IsWithinTerrainBounds(terrain, s.BottomPosition)) continue;
                float dx = s.BottomPosition.x - center.x; float dz = s.BottomPosition.z - center.z;
                bool inside = (bs.shape == BrushShape.Circle) ? (dx * dx + dz * dz) <= radius * radius : (Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius);
                if (!inside) continue;
                var outward = new Vector3(dx, 0f, dz);
                if (outward.sqrMagnitude < 1e-6f) continue;
                outward.Normalize();
                if (Vector3.Dot(s.Normal, outward) <= 0f) continue; // 法线须朝离刷心方向
                var v = (s.TopPosition - s.BottomPosition).normalized;
                float slopeDeg = Vector3.Angle(Vector3.up, v);
                if (slopeDeg < minSlopeDeg) continue; // 切片必须足够陡
                pruned.Add(s);
            }

            var sampler = useArc
                ? (IPointSampler)new ArcLengthEdgeSampler(pruned, mixMinSpacing, center, bs.shape)
                : new EdgeLineSampler(pruned, mixMinSpacing, center, bs.shape);
            var candidates = sampler.Sample(center, radius);
            if (candidates == null || candidates.Count == 0) return;

            // [修复] 为每个候选点查找对应的CliffSlice，提取法线和高度信息
            var candidateNormals = new Unity.Collections.NativeArray<Unity.Mathematics.float3>(candidates.Count, Unity.Collections.Allocator.Temp);
            var candidateHeights = new Unity.Collections.NativeArray<float>(candidates.Count, Unity.Collections.Allocator.Temp);

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                // 查找最近的CliffSlice
                int nearestIdx = 0;
                float minDistSq = float.MaxValue;
                for (int si = 0; si < slices.Count; si++)
                {
                    float distSq = Vector3.SqrMagnitude(slices[si].BottomPosition - candidate);
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        nearestIdx = si;
                    }
                }
                var slice = slices[nearestIdx];
                candidateNormals[i] = new Unity.Mathematics.float3(slice.Normal.x, slice.Normal.y, slice.Normal.z);
                candidateHeights[i] = slice.BottomPosition.y - terrain.transform.position.y;
            }

            var filter = new FacadeConstraintFilter(cfg != null ? cfg.minFacadeHeightMeters : 0.0001f);
            var pooled = new FacadeAligningSpawner(slices);
            var globalGrid = new BrushSpatialGrid(mixMinSpacing);
            var spawner = new GlobalGridSpawner(globalGrid, mixMinSpacing, pooled);

            float sumW = 0f;
            for (int i = 0; i < landItems.Count; i++) sumW += Mathf.Max(0f, landItems[i].weight);
            sumW = Mathf.Max(0.0001f, sumW);
            int total = candidates.Count;
            var quota = new int[landItems.Count];
            int allocated = 0;
            for (int i = 0; i < landItems.Count; i++)
            {
                float w = Mathf.Max(0f, landItems[i].weight);
                quota[i] = Mathf.FloorToInt((w / sumW) * total);
                allocated += quota[i];
            }
            for (int i = 0; i < total - allocated; i++) quota[i % landItems.Count]++; // 补齐到总数

            var order = new List<int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; }

            for (int oi = 0; oi < order.Count; oi++)
            {
                int ci = order[oi];
                // 按剩余额度的权重随机选择
                var elig = new List<int>();
                float wsum = 0f;
                for (int li = 0; li < landItems.Count; li++)
                {
                    if (quota[li] <= 0) continue;
                    float w = Mathf.Max(0.0001f, landItems[li].weight);
                    elig.Add(li);
                    wsum += w;
                }
                if (elig.Count == 0) break;
                double rPick = rnd.NextDouble() * wsum;
                float acc = 0f; int chosen = elig[0];
                for (int e = 0; e < elig.Count; e++)
                {
                    float w = Mathf.Max(0.0001f, landItems[elig[e]].weight);
                    acc += w;
                    if (rPick <= acc) { chosen = elig[e]; break; }
                }
                quota[chosen]--;
                var item = landItems[chosen];

                var singleList = BrushEngine.AcquireList3(1);
                singleList.Add(candidates[ci]);

                // [修复] 为单个候选点创建对应的法线和高度数组
                var singleNormals = new Unity.Collections.NativeArray<Unity.Mathematics.float3>(1, Unity.Collections.Allocator.Temp);
                var singleHeights = new Unity.Collections.NativeArray<float>(1, Unity.Collections.Allocator.Temp);
                singleNormals[0] = candidateNormals[ci];
                singleHeights[0] = candidateHeights[ci];

                var pipelineContext = new PipelineContext
                {
                    Terrain = terrain,
                    Center = center,
                    Radius = radius,
                    Item = item,
                    ItemIndex = chosen,
                    Parent = parent
                };
                var pipelineData = new PipelineData
                {
                    Candidates = singleList,
                    Heights = singleHeights,
                    Slopes = default,
                    Normals = singleNormals  // [修复] 现在传递正确的立面法线
                };

                // [改进] 使用 FacadeLineMutator，基于实际立面高度完美适配
                var facadeMutator = new FacadeLineMutator(slices, candidates);
                VegetationPipeline.Shared
                    .Setup(new CandidateSamplerFromList(null, 0), filter, facadeMutator, spawner)
                    .Run(pipelineContext, pipelineData);

                BrushEngine.ReleaseList3(singleList);
                singleNormals.Dispose();
                singleHeights.Dispose();
            }

            // 清理临时数组
            candidateNormals.Dispose();
            candidateHeights.Dispose();
        }
    }
}

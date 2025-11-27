using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MrTerrainPainter.Runtime.Profiles;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Runtime.Core;

namespace MrTerrainPainter.Editor.Services
{
    /// <summary>
    /// 立面（Facade）放置处理器，负责在悬崖/立面上放置物体
    /// </summary>
    public static class FacadePlacementHandler
    {
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
        float mixMinSpacing = landItems.Count > 0 ? landItems.Min(li => Mathf.Max(Mathf.Max(li.CoreSpacing, li.CoreMinRadius), 0.01f)) : 0.01f;

        var parent = typeToNode.TryGetValue(PrefabType.Landscape, out var tf) ? tf : null;
        if (parent == null) return;

        var sampler = new EdgeLineSampler(slices, mixMinSpacing, center, bs.shape);
        var candidates = sampler.Sample(center, radius);
        if (candidates == null || candidates.Count == 0) return;

        var filter = new FacadeConstraintFilter(cfg != null ? cfg.minFacadeHeightMeters : 0.0001f);
        var pooled = new PooledSpawner();
        var globalGrid = new BrushSpatialGrid(mixMinSpacing);
        var spawner = new GlobalGridSpawner(globalGrid, mixMinSpacing, pooled);

        float sumW = Mathf.Max(0.0001f, landItems.Sum(i => Mathf.Max(0f, i.weight)));
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
                Heights = default,
                Slopes = default,
                Normals = default
            };

            VegetationPipeline.Shared
                .Setup(new CandidateSamplerFromList(null, 0), filter, new EdgeLineMutator(), spawner)
                .Run(pipelineContext, pipelineData);

            BrushEngine.ReleaseList3(singleList);
        }
    }
    }
}

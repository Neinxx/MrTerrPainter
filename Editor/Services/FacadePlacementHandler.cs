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
            float mixMinSpacing = landItems.Count > 0 ? landItems.Min(li => Mathf.Max(li.CoreSpacing, 0.01f)) : 0.01f;

            var parent = typeToNode.TryGetValue(PrefabType.Landscape, out var tf) ? tf : null;
            if (parent == null) return;

            var sampler = new EdgeLineSampler(slices, mixMinSpacing, center, bs.shape);
            var candidates = sampler.Sample(center, radius);
            if (candidates == null || candidates.Count == 0) return;

            var filter = new FacadeConstraintFilter(cfg != null ? cfg.minFacadeHeightMeters : 0.0001f);
            var spawner = new PooledSpawner();

            float sumW = landItems.Sum(i => i.weight);

            for (int i = 0; i < candidates.Count; i++)
            {
                float r = (float)rnd.NextDouble() * sumW;
                float acc = 0;
                int pick = 0;
                for (int k = 0; k < landItems.Count; k++) { acc += landItems[k].weight; if (r <= acc) { pick = k; break; } }
                var item = landItems[pick];

                var singleList = BrushEngine.AcquireList3(1);
                singleList.Add(candidates[i]);

                var pipelineContext = new PipelineContext
                {
                    Terrain = terrain,
                    Center = center,
                    Radius = radius,
                    Item = item,
                    ItemIndex = pick,
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

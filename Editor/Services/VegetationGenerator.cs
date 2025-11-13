using System;
using System.Collections.Generic;
using MrTerrainPainter.Editor.Utils;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class VegetationGenerator
    {
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
            public float threshold = 0.5f; // 0-1，门限
        }

        public class FilterSettings
        {
            public NoiseSettings noise = new NoiseSettings();
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
            var missingTypesLogged = new HashSet<Runtime.Profiles.PrefabType>();
            for (int it = 0; it < items.Count; it++)
            {
                var item = items[it];
                if (item == null || !item.IsValid()) continue;

                // 二次细分：在条目密度的基础上叠加条目权重，保持类型比例直觉一致
                // 注意：仍以条目baseDensity为主驱动，weight作为细化因子避免违背“按条目密度严格计数”的要求
                int count = Mathf.RoundToInt(item.baseDensity * (areaX * areaZ) * 0.001f * Mathf.Max(item.weight, 0f));
                count = Mathf.Clamp(count, 0, 50000);
                if (count <= 0) continue;

                float spacing = Mathf.Max(item.minSpacing, 0.01f);
                if (!grids.TryGetValue(it, out var gridForItem)) { gridForItem = new Grid(spacing); grids[it] = gridForItem; }

                for (int s = 0; s < count; s++)
                {
                    float fx, fz;
                    if (area.HasValue)
                    {
                        var a = area.Value;
                        float minX = a.center.x - a.extents.x;
                        float maxX = a.center.x + a.extents.x;
                        float minZ = a.center.z - a.extents.z;
                        float maxZ = a.center.z + a.extents.z;
                        fx = (float)rnd.NextDouble() * (maxX - minX) + (minX - worldPos.x);
                        fz = (float)rnd.NextDouble() * (maxZ - minZ) + (minZ - worldPos.z);
                    }
                    else
                    {
                        fx = (float)rnd.NextDouble() * size.x;
                        fz = (float)rnd.NextDouble() * size.z;
                    }

                    Vector3 sample = new Vector3(worldPos.x + fx, worldPos.y, worldPos.z + fz);

                    bool noiseEnabled = filter != null && filter.noise != null && filter.noise.enabled;
                    float noiseAcceptance = 1f;
                    if (noiseEnabled)
                    {
                        float nv = FractalNoise(new Vector2(sample.x, sample.z), filter.noise);
                        if (filter.noise.invert) nv = 1f - nv;
                        if (nv < Mathf.Clamp01(filter.noise.threshold)) continue;
                        noiseAcceptance = nv;
                        if (rnd.NextDouble() > noiseAcceptance) continue;
                    }

                    if (!TerrainUtils.TryGetHeightAndNormal(terrain, sample, out float h, out Vector3 n)) continue;
                    sample.y = h;
                    float slope = TerrainUtils.ComputeSlope(n);

                    float heightLocal = h - worldPos.y;
                    if (noiseEnabled)
                    {
                        var hr = ov.HasValue ? ov.Value.heightRange : item.heightRange;
                        if (heightLocal < hr.x || heightLocal > hr.y) continue;
                    }
                    else
                    {
                        if (!MatchTerrain(item, heightLocal, slope, ov)) continue;
                    }

                    var p2 = new Vector2(fx, fz);
                    if (gridForItem.HasNearby(p2, spacing)) continue;
                    gridForItem.Add(p2);

                    var targetParent = ResolveTargetParent(terrain, item);
                    if (targetParent == null)
                    {
                        if (!missingTypesLogged.Contains(item.prefabType))
                        {
                            missingTypesLogged.Add(item.prefabType);
                            Debug.LogError($"未找到类型 {item.prefabType} 的父节点映射，请在设置窗口绑定对应的 Object + PrefabType。");
                        }
                        continue; // 无父节点：按需报错并跳过实例创建
                    }
                    CreateInstance(item, sample, n, terrain, it, targetParent, rnd, ov);
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

        private static bool MatchTerrain(VegetationItem item, float heightLocal, float slope, PlacementOverrides? ov)
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
            if (terrain == null || item == null) return null; // 提前返回
            // 查找全局配置（优先选择存在映射数据的配置实例）
            var config = Resources
                .FindObjectsOfTypeAll<MrTerrainPainterConfig>()
               .FirstOrDefault(c => c.objectTypeList != null &&
                     c.objectList != null &&
                     c.objectTypeList.Length > 0 &&
                     c.objectList.Length > 0);
            if (config != null && config.mappingEntries != null && config.mappingEntries.Count > 0)
            {
                for (int i = config.mappingEntries.Count - 1; i >= 0; i--)
                {
                    var entry = config.mappingEntries[i];
                    if (entry == null) continue;
                    if (entry.type != item.prefabType) continue;
                    var go = entry.node;
                    if (go == null) continue;
                    var tf = go.transform;
                    if (tf != null) return tf;
                }
            }
            if (config != null && config.objectTypeList != null && config.objectList != null)
            {
                int maxCount = Mathf.Max(config.objectTypeList.Length, config.objectList.Length);
                for (int i = maxCount - 1; i >= 0; i--)
                {
                    var hasType = i < config.objectTypeList.Length;
                    var hasObj = i < config.objectList.Length;
                    if (!hasType || !hasObj) continue;
                    if (config.objectTypeList[i] != item.prefabType) continue;
                    var go = config.objectList[i];
                    if (go == null) continue;
                    var tf = go.transform;
                    if (tf != null) return tf;
                }
            }
            // 未找到映射：返回空以触发调用方的错误提示与跳过
            return null;
        }

        private static void CreateInstance(VegetationItem item, Vector3 pos, Vector3 normal, Terrain terrain, int itemIndex, Transform parent, System.Random rnd, PlacementOverrides? ov)
        {
            if (item.prefab == null) return; // 提前返回
            // 优先复用对象池，避免大量实例化导致卡顿
            var go = VegetationPool.Get(terrain, item, itemIndex, parent, "Create Vegetation Instance");
            if (go == null) return; // 提前返回
            go.transform.position = pos;

            // 严格使用条目级范围，确保配置的缩放与旋转生效
            float scale = item.SampleScale(rnd);
            go.transform.localScale = Vector3.one * scale;

            float yRot = item.SampleYRotation(rnd);
            var rot = Quaternion.Euler(0f, yRot, 0f);
            if (item.alignToTerrainNormal)
            {
                rot = Quaternion.LookRotation(Vector3.Cross(Vector3.right, normal), normal) * Quaternion.Euler(0f, yRot, 0f);
            }
            go.transform.rotation = rot;

            var vi = go.GetComponent<VegetationInstance>();
            if (vi == null) vi = go.AddComponent<VegetationInstance>();
            vi.sourceTerrain = terrain;
            vi.profileItemIndex = itemIndex;
            vi.instanceId = Guid.NewGuid().ToString();
        }
    }
}

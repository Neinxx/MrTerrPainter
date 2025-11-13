using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Utils;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public enum BrushShape { Circle, Square }

    public class BrushSettings
    {
        public BrushShape shape = BrushShape.Circle;
        public float size = 5f; // 半径或半边长
        public float strength = 1f; // 强度影响每次绘制数量
        public float densityScale = 1f; // 与配方密度叠乘
        public float hardness = 1f; // 0-1，影响边缘衰减
        public bool preview = true;
    }

    public static class BrushPainter
    {
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
                if (!cells.TryGetValue(k, out var list)) { list = new List<Vector2>(); cells[k] = list; }
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
        public static void DrawPreview(Vector3 center, BrushSettings bs)
        {
            if (bs == null || !bs.preview) return; // 提前返回
            Handles.color = new Color(0.2f, 0.7f, 1f, 0.6f);
            if (bs.shape == BrushShape.Circle)
            {
                Handles.DrawWireDisc(center, Vector3.up, bs.size);
            }
            else
            {
                Vector3 half = new Vector3(bs.size, 0f, bs.size);
                Handles.DrawWireCube(center, half * 2f);
            }
        }

        public static void Paint(Terrain terrain, VegetationProfile profile, Vector3 center, BrushSettings bs, System.Random rnd, VegetationGenerator.PlacementOverrides? ov = null)
        {
            if (terrain == null || profile == null || profile.IsEmpty()) return; // 提前返回
            var td = terrain.terrainData;
            if (td == null) return; // 提前返回

            float radius = bs.size;
            // 绘制模式与生成模式统一：严格使用设置页映射的父节点，不再回退至地形容器
            var missingTypesLogged = new HashSet<Runtime.Profiles.PrefabType>();

            var items = profile.Items;
            for (int it = 0; it < items.Count; it++)
            {
                var item = items[it];
                if (item == null || !item.IsValid()) continue;
                int count = Mathf.RoundToInt(item.baseDensity * bs.densityScale * bs.strength * 10f);
                count = Mathf.Clamp(count, 0, 500);
                if (count <= 0) continue;

                float spacing = Mathf.Max(item.minSpacing, 0.01f);
                var grid = new Grid(spacing);

                for (int i = 0; i < count; i++)
                {
                    Vector3 p = RandomPointInBrush(center, radius, bs.shape, rnd);
                    if (!TerrainUtils.IsWithinTerrainBounds(terrain, p)) continue;
                    if (!TerrainUtils.TryGetHeightAndNormal(terrain, p, out float h, out Vector3 n)) continue;
                    p.y = h;
                    float slope = TerrainUtils.ComputeSlope(n);

                    // 边缘硬度衰减
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(center.x, center.z));
                    float t = Mathf.Clamp01(d / radius);
                    float edgeFalloff = 1f - t; // 中心为1，边缘为0
                    float acceptance = Mathf.Lerp(1f, edgeFalloff, Mathf.Clamp01(bs.hardness));
                    if (rnd.NextDouble() > acceptance) continue;

                    if (!MatchTerrain(item, h - terrain.transform.position.y, slope, ov)) continue;

                    // 条目级最小间距约束（XZ平面）
                    var p2 = new Vector2(p.x - terrain.transform.position.x, p.z - terrain.transform.position.z);
                    if (grid.HasNearby(p2, spacing)) continue;
                    grid.Add(p2);

                    var targetParent = VegetationGenerator.ResolveTargetParent(terrain, item);
                    if (targetParent == null)
                    {
                        if (!missingTypesLogged.Contains(item.prefabType))
                        {
                            missingTypesLogged.Add(item.prefabType);
                            Debug.LogError($"未找到类型 {item.prefabType} 的父节点映射，请在设置窗口绑定对应的 Object + PrefabType。");
                        }
                        continue; // 无父节点：报错并跳过实例创建
                    }
                    CreateInstance(item, p, n, terrain, it, targetParent, rnd, ov);
                }
            }
        }

        public static void Erase(Vector3 center, BrushSettings bs, bool eraseAll, IReadOnlyList<GameObject> onlyTypes = null)
        {
            float radius = bs.size;
            var hits = Physics.OverlapSphere(center, radius);
            for (int i = 0; i < hits.Length; i++)
            {
                var go = hits[i].gameObject;
                var vi = go.GetComponent<VegetationInstance>();
                if (vi == null) continue;
                if (!eraseAll && onlyTypes != null && onlyTypes.Count > 0)
                {
                    bool match = false;
                    for (int t = 0; t < onlyTypes.Count; t++)
                    {
                        if (go.name.StartsWith(onlyTypes[t].name)) { match = true; break; }
                    }
                    if (!match) continue;
                }
                VegetationPool.Recycle(vi.sourceTerrain, go, "Erase Vegetation Instance");
            }
        }

        // 基于容器子物体的擦除（无需碰撞体）
        public static void Erase(Terrain terrain, Vector3 center, BrushSettings bs, bool eraseAll, IReadOnlyList<GameObject> onlyTypes = null)
        {
            if (terrain == null) return; // 提前返回
            float radius = bs.size;

            // 1) 组合所有潜在的根容器：默认地形容器 + 设置映射的父节点
            var roots = new List<Transform>();
            var defaultContainer = terrain.transform.Find($"Vegetation_{terrain.name}");
            if (defaultContainer != null) roots.Add(defaultContainer);

            // 聚合所有可用配置实例，避免拿到空数组实例导致擦除失败
            var configs = Resources.FindObjectsOfTypeAll<MrTerrainPainter.Editor.Config.MrTerrainPainterConfig>();
            if (configs != null && configs.Length > 0)
            {
                var set = new HashSet<Transform>();
                for (int ci = 0; ci < configs.Length; ci++)
                {
                    var c = configs[ci];
                    var list = c != null ? c.objectList : null;
                    if (list == null || list.Length == 0) continue;
                    for (int i = 0; i < list.Length; i++)
                    {
                        var go = list[i];
                        if (go == null) continue;
                        var tf = go.transform;
                        if (tf == null) continue;
                        if (set.Add(tf)) roots.Add(tf);
                    }
                }
            }

            if (roots.Count == 0)
            {
                // 2) 无根容器时，回退到物理擦除（需要碰撞体）
                Erase(center, bs, eraseAll, onlyTypes);
                return;
            }

            // 收集待回收对象（遍历所有根的所有子孙）
            var toRecycle = new List<GameObject>();
            for (int r = 0; r < roots.Count; r++)
            {
                var root = roots[r];
                if (root == null) continue;
                CollectInRadius(root, center, radius, eraseAll, onlyTypes, toRecycle);
            }

            for (int i = 0; i < toRecycle.Count; i++)
            {
                var go = toRecycle[i];
                if (go == null) continue;
                var vi = go.GetComponent<VegetationInstance>();
                var srcTerrain = vi != null ? vi.sourceTerrain : terrain;
                VegetationPool.Recycle(srcTerrain, go, "Erase Vegetation Instance");
            }
        }

        private static void CollectInRadius(Transform root, Vector3 center, float radius, bool eraseAll, IReadOnlyList<GameObject> onlyTypes, List<GameObject> outList)
        {
            if (root == null) return; // 提前返回
            // DFS 遍历所有子孙，查找 VegetationInstance
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null) continue;
                var go = t.gameObject;
                var vi = go.GetComponent<VegetationInstance>();
                if (vi != null)
                {
                    var p = t.position;
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(center.x, center.z));
                    if (d <= radius)
                    {
                        if (!eraseAll && onlyTypes != null && onlyTypes.Count > 0)
                        {
                            bool match = false;
                            for (int i = 0; i < onlyTypes.Count; i++)
                            {
                                if (go.name.StartsWith(onlyTypes[i].name)) { match = true; break; }
                            }
                            if (!match)
                            {
                                // 不匹配类型则跳过
                                goto SkipAdd;
                            }
                        }
                        outList.Add(go);
                    }
                }
            SkipAdd:
                for (int i = 0; i < t.childCount; i++)
                {
                    stack.Push(t.GetChild(i));
                }
            }
        }

        private static Vector3 RandomPointInBrush(Vector3 center, float size, BrushShape shape, System.Random rnd)
        {
            if (shape == BrushShape.Circle)
            {
                float r = Mathf.Sqrt((float)rnd.NextDouble()) * size; // 均匀分布
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                return center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            float x = (float)rnd.NextDouble() * 2f - 1f;
            float z = (float)rnd.NextDouble() * 2f - 1f;
            return center + new Vector3(x * size, 0f, z * size);
        }

        private static List<VegetationItem> BuildWeightedList(IReadOnlyList<VegetationItem> items)
        {
            var list = new List<VegetationItem>();
            if (items == null || items.Count == 0) return list; // 提前返回
            foreach (var it in items)
            {
                if (it == null || !it.IsValid()) continue;
                int count = Mathf.Clamp(Mathf.RoundToInt(it.weight * 10f), 1, 100);
                for (int i = 0; i < count; i++) list.Add(it);
            }
            return list;
        }

        private static bool MatchTerrain(VegetationItem item, float heightLocal, float slope, VegetationGenerator.PlacementOverrides? ov)
        {
            if (item == null) return false;
            var hr = ov.HasValue ? ov.Value.heightRange : item.heightRange;
            var sr = ov.HasValue ? ov.Value.slopeRange : item.slopeRange;
            if (heightLocal < hr.x || heightLocal > hr.y) return false;
            if (slope < sr.x || slope > sr.y) return false;
            return true;
        }

        private static Transform GetOrCreateContainer(Terrain terrain)
        {
            var name = $"Vegetation_{terrain.name}";
            var t = terrain.transform.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(terrain.transform, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        private static float SampleRange(Vector2 range, System.Random rnd)
        {
            return Mathf.Lerp(range.x, range.y, (float)rnd.NextDouble());
        }

        private static void CreateInstance(VegetationItem item, Vector3 pos, Vector3 normal, Terrain terrain, int itemIndex, Transform parent, System.Random rnd, VegetationGenerator.PlacementOverrides? ov)
        {
            if (item.prefab == null) return; // 提前返回
            // 优先复用对象池，减少实例化与GC
            var go = VegetationPool.Get(terrain, item, itemIndex, parent, "Paint Vegetation Instance");
            if (go == null) return; // 提前返回
            go.transform.position = pos;
            // 强制使用条目级缩放范围，确保Profile设置生效
            float scale = item.SampleScale(rnd);
            go.transform.localScale = Vector3.one * scale;
            // 强制使用条目级Y旋转范围
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
        }
    }
}
using System.Collections.Generic;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using MrTerrainPainter.Editor.Config;

// 编辑器对象池：避免擦除时大量销毁，支持复用与撤销
public static class VegetationPool
{
    public static bool ShowInHierarchy = true;
    private static readonly Dictionary<string, ObjectPool<GameObject>> pools = new();
    private static readonly Dictionary<int, Dictionary<(int,int), List<GameObject>>> spatial = new();
    private const float SpatialCellSize = 2f;
    private static (int,int) Key(Terrain t, Vector3 worldPos)
    {
        var lp = worldPos - t.transform.position;
        return (Mathf.FloorToInt(lp.x / SpatialCellSize), Mathf.FloorToInt(lp.z / SpatialCellSize));
    }
    public static void ClearTerrainIndex(Terrain terrain)
    {
        if (terrain == null) return;
        var tid = terrain.GetInstanceID();
        spatial.Remove(tid);
    }
    public static void ClearAllIndexes()
    {
        spatial.Clear();
    }
    public static void IndexRegister(Terrain terrain, GameObject go)
    {
        if (terrain == null || go == null) return;
        var tid = terrain.GetInstanceID();
        if (!spatial.TryGetValue(tid, out var grid)) { grid = new Dictionary<(int,int), List<GameObject>>(); spatial[tid] = grid; }
        var k = Key(terrain, go.transform.position);
        if (!grid.TryGetValue(k, out var list)) { list = new List<GameObject>(); grid[k] = list; }
        if (!list.Contains(go)) list.Add(go);
    }
    public static void IndexUnregister(Terrain terrain, GameObject go)
    {
        if (terrain == null || go == null) return;
        var tid = terrain.GetInstanceID();
        if (!spatial.TryGetValue(tid, out var grid)) return;
        var k = Key(terrain, go.transform.position);
        if (grid.TryGetValue(k, out var list)) { list.Remove(go); }
    }
    public static void QueryInRadius(Terrain terrain, Vector3 center, float radius, List<GameObject> outList)
    {
        if (terrain == null || outList == null) return;
        var tid = terrain.GetInstanceID();
        if (!spatial.TryGetValue(tid, out var grid)) return;
        var lp = center - terrain.transform.position;
        int rx = Mathf.CeilToInt(radius / SpatialCellSize);
        var kc = (Mathf.FloorToInt(lp.x / SpatialCellSize), Mathf.FloorToInt(lp.z / SpatialCellSize));
        for (int dx = -rx; dx <= rx; dx++)
        for (int dz = -rx; dz <= rx; dz++)
        {
            var k = (kc.Item1 + dx, kc.Item2 + dz);
            if (!grid.TryGetValue(k, out var list)) continue;
            for (int i = 0; i < list.Count; i++)
            {
                var go = list[i];
                if (go == null) continue;
                var p = go.transform.position;
                var v = new Vector3(p.x - center.x, 0f, p.z - center.z);
                if (v.sqrMagnitude <= radius * radius) outList.Add(go);
            }
        }
    }
    // 获取或创建：复用池中对象；若无则实例化
    public static GameObject Get(Terrain terrain, VegetationItem item, int itemIndex, Transform targetParent, string undoLabel)
    {
        if (terrain == null || item == null || item.prefab == null) return null; // 提前返回
        var nameHint = item.prefab.name;
        var bin = GetOrCreateBin(terrain, itemIndex, nameHint);
        var key = BuildKey(terrain, itemIndex, nameHint);
        if (!pools.TryGetValue(key, out var pool))
        {
            pool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab);
                    if (go == null) return null;
                    Undo.RegisterCreatedObjectUndo(go, undoLabel);
                    var vi = go.GetComponent<VegetationInstance>() ?? go.AddComponent<VegetationInstance>();
                    vi.sourceTerrain = terrain;
                    vi.profileItemIndex = itemIndex;
                    Undo.SetTransformParent(go.transform, bin, undoLabel);
                    go.SetActive(false);
                    return go;
                },
                actionOnGet: null,
                actionOnRelease: go =>
                {
                    if (go == null) return;
                    Undo.SetTransformParent(go.transform, bin, undoLabel);
                    Undo.RecordObject(go, undoLabel);
                    go.SetActive(false);
                },
                actionOnDestroy: go => { if (go != null) Object.DestroyImmediate(go); },
                defaultCapacity: 10,
                maxSize: 100
            );
            pools[key] = pool;
        }
        var reused = pool.Get();
        if (reused == null) return null;
        Undo.SetTransformParent(reused.transform, targetParent, undoLabel);
        Undo.RecordObject(reused, undoLabel);
        reused.SetActive(true);
        var vi2 = reused.GetComponent<VegetationInstance>() ?? reused.AddComponent<VegetationInstance>();
        vi2.sourceTerrain = terrain;
        vi2.profileItemIndex = itemIndex;
        vi2.sourcePrefabName = item.prefab.name;
        return reused;
    }

    // 回收到对象池：移动到池并禁用
    public static void Recycle(Terrain terrain, GameObject go, string undoLabel)
    {
        if (terrain == null || go == null) return; // 提前返回
        IndexUnregister(terrain, go);
        var vi = go.GetComponent<VegetationInstance>();
        var itemIndex = vi != null ? vi.profileItemIndex : -1;
        var nameHint = vi != null && !string.IsNullOrEmpty(vi.sourcePrefabName) ? vi.sourcePrefabName : go.name;
        var key = BuildKey(terrain, itemIndex, nameHint);
        if (!pools.TryGetValue(key, out var pool))
        {
            var bin = GetOrCreateBin(terrain, itemIndex, nameHint);
            Undo.SetTransformParent(go.transform, bin, undoLabel);
            Undo.RecordObject(go, undoLabel);
            go.SetActive(false);
            return;
        }
        pool.Release(go);
    }

    private static Transform GetOrCreatePoolRoot(Terrain terrain)
    {
        var name = $"VegetationPool_{terrain.name}";
        var t = terrain.transform.Find(name);
        if (t != null)
        {
            t.gameObject.hideFlags = ShowInHierarchy ? HideFlags.None : HideFlags.HideInHierarchy;
            return t;
        }
        var go = new GameObject(name);
        go.transform.SetParent(terrain.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.hideFlags = ShowInHierarchy ? HideFlags.None : HideFlags.HideInHierarchy;
        return go.transform;
    }

    private static Transform GetOrCreateBin(Terrain terrain, int itemIndex, string nameHint)
    {
        var root = GetOrCreatePoolRoot(terrain);
        var safeName = string.IsNullOrEmpty(nameHint) ? "Item" : nameHint;
        var binName = itemIndex >= 0 ? $"Item_{itemIndex}_{safeName}" : $"Item_{safeName}";
        var t = root.Find(binName);
        if (t != null) return t;
        var go = new GameObject(binName);
        go.transform.SetParent(root, false);
        go.transform.localPosition = Vector3.zero;
        return go.transform;
    }

    private static string BuildKey(Terrain terrain, int itemIndex, string nameHint)
    {
        var safeName = string.IsNullOrEmpty(nameHint) ? "Item" : nameHint;
        return $"{terrain.GetInstanceID()}_{itemIndex}_{safeName}";
    }

    // 批量回收：将地形的生成容器中所有实例迁移到对象池（可选删除空容器）
    public static void RecycleAllInstances(Terrain terrain, bool removeEmptyContainer = false, string undoLabel = "Clear Vegetation Instances")
    {
        if (terrain == null) return; // 提前返回

        // 1) 默认容器（旧逻辑支持）：Terrain 下的 Vegetation_{terrain.name}
        var defaultContainer = terrain.transform.Find($"Vegetation_{terrain.name}");

        var mappedParents = new List<Transform>();
        var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
        var entries = cfg != null ? cfg.mappingEntries : null;
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var tf = entries[i]?.node;
                if (tf != null && !mappedParents.Contains(tf)) mappedParents.Add(tf);
            }
        }

        // 使用撤销分组，避免生成大量独立Undo记录造成卡顿
        int group = Undo.GetCurrentGroup();
        Undo.IncrementCurrentGroup();

        // 收集并回收：默认容器 + 映射父节点下的实例（仅回收与该 Terrain 关联的）
        void CollectAndRecycleUnder(Transform parent)
        {
            if (parent == null) return;
            var toRecycle = new List<GameObject>();
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null) continue;
                toRecycle.Add(child.gameObject);
            }
            for (int i = 0; i < toRecycle.Count; i++)
            {
                var go = toRecycle[i];
                var vi = go.GetComponent<VegetationInstance>();
                if (vi == null) continue; // 仅回收本工具生成的实例
                // 仅回收来源地形匹配者，避免影响用户自有物体
                if (vi.sourceTerrain != null && vi.sourceTerrain != terrain) continue;
                var srcTerrain = vi.sourceTerrain != null ? vi.sourceTerrain : terrain;
                Recycle(srcTerrain, go, undoLabel);
            }
        }

        // 默认容器优先处理
        CollectAndRecycleUnder(defaultContainer);
        // 映射父节点处理（不删除这些父节点）
        for (int i = 0; i < mappedParents.Count; i++) CollectAndRecycleUnder(mappedParents[i]);

        // 默认容器按需删除（仅当存在时）
        if (removeEmptyContainer && defaultContainer != null)
        {
            Undo.DestroyObjectImmediate(defaultContainer.gameObject);
        }

        // 合并撤销操作，减少记录数量
        Undo.CollapseUndoOperations(group);
    }

    public static void ApplyShowInHierarchyAll()
    {
        var terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0) return;
        for (int i = 0; i < terrains.Length; i++)
        {
            var t = terrains[i];
            if (t == null) continue;
            var root = t.transform.Find($"VegetationPool_{t.name}");
            if (root == null) continue;
            var stack = new System.Collections.Generic.Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                cur.gameObject.hideFlags = ShowInHierarchy ? HideFlags.None : HideFlags.HideInHierarchy;
                for (int ci = 0; ci < cur.childCount; ci++) stack.Push(cur.GetChild(ci));
            }
            EditorUtility.SetDirty(root.gameObject);
        }
    }
}

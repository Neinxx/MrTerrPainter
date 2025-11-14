using System.Collections.Generic;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;

// 编辑器对象池：避免擦除时大量销毁，支持复用与撤销
public static class VegetationPool
{
    public static bool ShowInHierarchy = true;
    // 获取或创建：复用池中对象；若无则实例化
    public static GameObject Get(Terrain terrain, VegetationItem item, int itemIndex, Transform targetParent, string undoLabel)
    {
        if (terrain == null || item == null || item.prefab == null) return null; // 提前返回
        var bin = GetOrCreateBin(terrain, itemIndex, item.prefab.name);

        // 查找一个未激活的子物体作为复用对象
        GameObject reused = null;
        for (int i = 0; i < bin.childCount; i++)
        {
            var t = bin.GetChild(i);
            if (t == null) continue;
            var go = t.gameObject;
            if (!go.activeSelf)
            {
                reused = go;
                break;
            }
        }

        if (reused == null)
        {
            // 没有可复用对象，实例化新的
            var go = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab);
            if (go == null) return null;
            Undo.RegisterCreatedObjectUndo(go, undoLabel);
            var vi = go.GetComponent<VegetationInstance>();
            if (vi == null) vi = go.AddComponent<VegetationInstance>();
            vi.sourceTerrain = terrain;
            vi.profileItemIndex = itemIndex;
            // 先放入bin，便于撤销与管理
            Undo.SetTransformParent(go.transform, bin, undoLabel);
            go.SetActive(false);
            reused = go;
        }

        // 迁移到目标父级并激活
        Undo.SetTransformParent(reused.transform, targetParent, undoLabel);
        Undo.RecordObject(reused, undoLabel);
        reused.SetActive(true);

        var vi2 = reused.GetComponent<VegetationInstance>();
        if (vi2 == null) vi2 = reused.AddComponent<VegetationInstance>();
        vi2.sourceTerrain = terrain;
        vi2.profileItemIndex = itemIndex;
        return reused;
    }

    // 回收到对象池：移动到池并禁用
    public static void Recycle(Terrain terrain, GameObject go, string undoLabel)
    {
        if (terrain == null || go == null) return; // 提前返回
        var vi = go.GetComponent<VegetationInstance>();
        var itemIndex = vi != null ? vi.profileItemIndex : -1;
        var nameHint = go.name;
        var bin = GetOrCreateBin(terrain, itemIndex, nameHint);
        // 迁移层级并禁用，均记录Undo，撤销时会回到原父级与激活状态
        Undo.SetTransformParent(go.transform, bin, undoLabel);
        Undo.RecordObject(go, undoLabel);
        go.SetActive(false);
    }

    private static Transform GetOrCreatePoolRoot(Terrain terrain)
    {
        var name = $"VegetationPool_{terrain.name}";
        var t = terrain.transform.Find(name);
        if (t != null)
        {
            // 动态应用显示设置
            t.gameObject.hideFlags = ShowInHierarchy ? HideFlags.None : HideFlags.HideInHierarchy;
            return t;
        }
        var go = new GameObject(name);
        go.transform.SetParent(terrain.transform, false);
        go.transform.localPosition = Vector3.zero;
        // 根据设置决定是否在层级显示
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

    // 批量回收：将地形的生成容器中所有实例迁移到对象池（可选删除空容器）
    public static void RecycleAllInstances(Terrain terrain, bool removeEmptyContainer = false, string undoLabel = "Clear Vegetation Instances")
    {
        if (terrain == null) return; // 提前返回

        // 1) 默认容器（旧逻辑支持）：Terrain 下的 Vegetation_{terrain.name}
        var defaultContainer = terrain.transform.Find($"Vegetation_{terrain.name}");

        // 2) 设置映射的父节点：按设置页中的 ObjectList 聚合（不删除这些容器）
        var mappedParents = new List<Transform>();
        foreach (var cfg in Resources.FindObjectsOfTypeAll<MrTerrainPainter.Editor.Config.MrTerrainPainterConfig>())
        {
            var entries = cfg != null ? cfg.mappingEntries : null;
            if (entries == null || entries.Count == 0) continue;
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
}

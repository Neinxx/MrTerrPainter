using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using MrTerrainPainter.Editor.Utils;

namespace MrTerrainPainter.Editor.Controllers
{
    // 负责场景地形的扫描、列表清理与新增，保持纯业务逻辑
    public class TerrainController
    {
        private readonly List<Terrain> _cacheSelectedTerrains = new();
        // 扫描场景中的激活地形，填充 UI 展示数据与名称集合
        public void ScanSceneTerrains(List<Terrain> terrainListUIData, List<string> scannedTerrainNames)
        {
            if (terrainListUIData == null || scannedTerrainNames == null) return; // 提前返回
            scannedTerrainNames.Clear();
            terrainListUIData.Clear();
            var terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0) return; // 提前返回
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t == null) continue;
                scannedTerrainNames.Add(t.name);
                terrainListUIData.Add(t);
            }
        }

        // 仅清空 UI 列表（保留其他状态）
        public void ClearTerrainUIList(List<Terrain> terrainListUIData)
        {
            if (terrainListUIData == null) return; // 提前返回
            terrainListUIData.Clear();
        }

        // 清空选中列表、UI列表与名称集合
        public void ClearTerrainLists(List<Terrain> selectedTerrains, List<Terrain> terrainListUIData, List<string> scannedTerrainNames)
        {
            if (selectedTerrains == null || terrainListUIData == null || scannedTerrainNames == null) return; // 提前返回
            selectedTerrains.Clear();
            terrainListUIData.Clear();
            scannedTerrainNames.Clear();
        }

        // 将单个 Terrain 加入到选中与 UI 列表，并记录名称（去重保护）
        public void AddTerrainToLists(Terrain terrain,
            List<Terrain> selectedTerrains,
            List<Terrain> terrainListUIData,
            List<string> scannedTerrainNames)
        {
            if (terrain == null || selectedTerrains == null || terrainListUIData == null || scannedTerrainNames == null) return; // 提前返回
            // 去重：避免重复添加
            if (!selectedTerrains.Contains(terrain)) selectedTerrains.Add(terrain);
            if (!terrainListUIData.Contains(terrain)) terrainListUIData.Add(terrain);
            if (!scannedTerrainNames.Contains(terrain.name)) scannedTerrainNames.Add(terrain.name);
        }

        public bool TryGetTerrainHit(Ray ray, out Terrain terrain, out Vector3 pos, out Vector3 normal)
        {
            terrain = null;
            pos = Vector3.zero;
            normal = Vector3.up;
            float bestT = float.MaxValue;
            var terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0) return false;
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t == null) continue;
                var col = t.GetComponent<TerrainCollider>();
                if (col != null)
                {
                    if (col.Raycast(ray, out var hit, 10000f))
                    {
                        if (hit.distance < bestT)
                        {
                            bestT = hit.distance;
                            terrain = t;
                            pos = hit.point;
                            if (TerrainUtils.TryGetHeightAndNormal(terrain, pos, out var h, out var n))
                            {
                                pos.y = h;
                                normal = n;
                            }
                        }
                    }
                    continue;
                }
                float dy = ray.direction.y;
                if (Mathf.Abs(dy) < 1e-5f) continue;
                float planeY = t.transform.position.y;
                float tt = (planeY - ray.origin.y) / dy;
                if (tt <= 0f || tt >= bestT) continue;
                var p = ray.origin + ray.direction * tt;
                var size = t.terrainData.size;
                var tp = t.transform.position;
                if (p.x < tp.x || p.x > tp.x + size.x || p.z < tp.z || p.z > tp.z + size.z) continue;
                if (TerrainUtils.TryGetHeightAndNormal(t, p, out var hh, out var nn))
                {
                    bestT = tt;
                    terrain = t;
                    pos = new Vector3(p.x, hh, p.z);
                    normal = nn;
                }
            }
            return terrain != null;
        }

        public Terrain NearestTerrain(Vector3 pos, List<Terrain> selectedTerrains)
        {
            Terrain best = null;
            float bestDist = float.MaxValue;
            if (selectedTerrains == null) return best;
            for (int i = 0; i < selectedTerrains.Count; i++)
            {
                var t = selectedTerrains[i];
                if (t == null) continue;
                float d = (pos - t.transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best;
        }

        public IReadOnlyList<Terrain> GetSelectedTerrains()
        {
            _cacheSelectedTerrains.Clear();
            var ctx = MrTerrainPainter.Editor.Tools.MTPBrushContext.SelectedTerrains;
            if (ctx != null)
            {
                for (int i = 0; i < ctx.Count; i++)
                {
                    var t = ctx[i];
                    if (t != null && !_cacheSelectedTerrains.Contains(t)) _cacheSelectedTerrains.Add(t);
                }
            }
            var objs = Selection.transforms;
            if (objs != null)
            {
                for (int i = 0; i < objs.Length; i++)
                {
                    var tf = objs[i];
                    if (tf == null) continue;
                    var t = tf.GetComponent<Terrain>();
                    if (t != null && !_cacheSelectedTerrains.Contains(t)) _cacheSelectedTerrains.Add(t);
                }
            }
            return _cacheSelectedTerrains;
        }

        public bool TryFindNearestTerrain(Vector3 worldPos, out Terrain nearest)
        {
            nearest = null;
            var list = GetSelectedTerrains();
            if (list == null || list.Count == 0)
            {
                var actives = Terrain.activeTerrains;
                if (actives == null || actives.Length == 0) return false;
                float best = float.MaxValue;
                for (int i = 0; i < actives.Length; i++)
                {
                    var t = actives[i];
                    if (t == null) continue;
                    float d = (worldPos - t.transform.position).sqrMagnitude;
                    if (d < best) { best = d; nearest = t; }
                }
                return nearest != null;
            }
            float bestSel = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null) continue;
                float d = (worldPos - t.transform.position).sqrMagnitude;
                if (d < bestSel) { bestSel = d; nearest = t; }
            }
            return nearest != null;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Editor.Controllers
{
    // 负责场景地形的扫描、列表清理与新增，保持纯业务逻辑
    public class TerrainController
    {
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
    }
}
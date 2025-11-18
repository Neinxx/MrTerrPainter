using UnityEngine;

namespace MrTerrainPainter.Runtime.Core
{
    // 标记由工具创建的植被实例，便于擦除与统计
    public class VegetationInstance : MonoBehaviour
    {
        [Tooltip("来源地形对象，用于归类管理")]
        public Terrain sourceTerrain;

        [Tooltip("在配方中的索引（可选）")]
        public int profileItemIndex = -1;

        [Tooltip("实例唯一标识（可选）")]
        public string instanceId;

        public string sourcePrefabName;
    }
}

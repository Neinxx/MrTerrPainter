using UnityEngine;

namespace MrTerrainPainter.Editor.Utils
{
    public static class TerrainUtils
    {
        // 提前返回：无地形或坐标非法，直接返回默认值
        public static bool TryGetHeightAndNormal(Terrain terrain, Vector3 worldPos, out float height, out Vector3 normal)
        {
            height = 0f;
            normal = Vector3.up;
            if (terrain == null) return false;

            var td = terrain.terrainData;
            if (td == null) return false;

            Vector3 local = worldPos - terrain.transform.position;
            if (local.x < 0 || local.z < 0 || local.x > td.size.x || local.z > td.size.z) return false;

            height = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
            normal = td.GetInterpolatedNormal(local.x / td.size.x, local.z / td.size.z);
            return true;
        }

        public static float ComputeSlope(Vector3 normal)
        {
            // 以法线与世界上方向的夹角作为坡度（度）
            return Vector3.Angle(normal, Vector3.up);
        }

        public static bool IsWithinTerrainBounds(Terrain terrain, Vector3 worldPos)
        {
            if (terrain == null) return false;
            var td = terrain.terrainData;
            if (td == null) return false;
            Vector3 local = worldPos - terrain.transform.position;
            return local.x >= 0 && local.z >= 0 && local.x <= td.size.x && local.z <= td.size.z;
        }
    }
}
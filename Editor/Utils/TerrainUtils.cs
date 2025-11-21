using UnityEngine;
using Unity.Collections;

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

        public struct HeightBlock
        {
            public NativeArray<float> heights;
            public int xBase;
            public int zBase;
            public int width;
            public int height;
            public float dxWorld;
            public float dzWorld;
        }

        public static HeightBlock FetchHeightsBlock(Terrain terrain, Bounds worldArea, Allocator allocator)
        {
            var td = terrain.terrainData;
            int hmRes = td.heightmapResolution;
            int hmMax = hmRes - 1;
            Vector3 pos = terrain.transform.position;
            Vector3 size = td.size;

            float minX = Mathf.Clamp(worldArea.min.x - pos.x, 0f, size.x);
            float maxX = Mathf.Clamp(worldArea.max.x - pos.x, 0f, size.x);
            float minZ = Mathf.Clamp(worldArea.min.z - pos.z, 0f, size.z);
            float maxZ = Mathf.Clamp(worldArea.max.z - pos.z, 0f, size.z);

            float u0 = (minX / size.x) * hmMax;
            float v0 = (minZ / size.z) * hmMax;
            float u1 = (maxX / size.x) * hmMax;
            float v1 = (maxZ / size.z) * hmMax;

            int xBase = Mathf.Max(0, Mathf.FloorToInt(u0) - 1);
            int zBase = Mathf.Max(0, Mathf.FloorToInt(v0) - 1);
            int xEnd = Mathf.Min(hmMax, Mathf.CeilToInt(u1) + 1);
            int zEnd = Mathf.Min(hmMax, Mathf.CeilToInt(v1) + 1);
            int w = Mathf.Max(1, xEnd - xBase + 1);
            int h = Mathf.Max(1, zEnd - zBase + 1);

            var block = td.GetHeights(xBase, zBase, w, h);
            var arr = new NativeArray<float>(w * h, allocator, NativeArrayOptions.UninitializedMemory);
            int k = 0;
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    arr[k++] = block[j, i];
                }
            }

            return new HeightBlock
            {
                heights = arr,
                xBase = xBase,
                zBase = zBase,
                width = w,
                height = h,
                dxWorld = size.x / hmMax,
                dzWorld = size.z / hmMax,
            };
        }
    }
}

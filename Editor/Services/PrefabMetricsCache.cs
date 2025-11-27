using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    /// <summary>
    /// Prefab尺寸缓存，用于快速获取prefab的高度和水平范围
    /// 避免重复计算Renderer bounds
    /// </summary>
    public static class PrefabMetricsCache
    {
        private static readonly Dictionary<int, float> s_heightCache = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> s_extentCache = new Dictionary<int, float>();

        /// <summary>
        /// 获取Prefab的高度（米）
        /// </summary>
        public static float GetPrefabHeightMeters(GameObject go)
        {
            if (go == null) return 1f;

            int id = go.GetInstanceID();
            if (s_heightCache.TryGetValue(id, out float cached))
                return cached;

            float h = 1f;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                h = bounds.size.y;
            }

            s_heightCache[id] = h;
            return h;
        }

        /// <summary>
        /// 获取Prefab的水平范围（米，XZ平面最大半径）
        /// </summary>
        public static float GetPrefabHorizontalExtentMeters(GameObject go)
        {
            if (go == null) return 0.5f;

            int id = go.GetInstanceID();
            if (s_extentCache.TryGetValue(id, out float cached))
                return cached;

            float ext = 0.5f;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                float dx = bounds.size.x;
                float dz = bounds.size.z;
                ext = Mathf.Max(dx, dz) * 0.5f;
            }

            s_extentCache[id] = ext;
            return ext;
        }

        /// <summary>
        /// 清空缓存
        /// </summary>
        public static void ClearCache()
        {
            s_heightCache.Clear();
            s_extentCache.Clear();
        }
    }
}

using System;
using UnityEngine;

namespace MrTerrainPainter.Runtime.Profiles
{
    [Serializable]
    public class VegetationItem
    {
        [Header("基础配置")]
        public GameObject prefab;
        [Range(0f, 1f)] public float weight = 1f;

        [Header("类型")]
        public PrefabType prefabType = PrefabType.Prop;

        [Header("缩放范围")]
        public Vector2 uniformScaleRange = new Vector2(1f, 1f);

        [Header("旋转范围 (Y 轴)")]
        public Vector2 yRotationRange = new Vector2(0f, 360f);

        [Header("地形条件")]
        public Vector2 heightRange = new Vector2(0f, 1000f);
        public Vector2 slopeRange = new Vector2(0f, 90f);

        [Header("密度与间距（条目级）")]
        [Tooltip("该预制体的单位面积基础密度，仅用于笔刷绘制时的数量估算。")]
        [Range(0f, 10f)] public float baseDensity = 1f;
        [Tooltip("该预制体的最小间距约束（米），随机生成与绘制均生效。")]
        [Range(0f, 10f)] public float minSpacing = 1.5f;

        [Header("对齐设置")]
        public bool alignToTerrainNormal = true;

        public int Index { get; set; }


        public bool IsValid()
        {
            if (prefab == null) return false;
            if (weight <= 0f) return false;
            if (uniformScaleRange.x <= 0f || uniformScaleRange.y <= 0f) return false;
            if (uniformScaleRange.y < uniformScaleRange.x) return false;
            return true;
        }

        public float SampleScale(System.Random rnd)
        {
            if (uniformScaleRange.x == uniformScaleRange.y) return uniformScaleRange.x;
            var t = (float)rnd.NextDouble();
            return Mathf.Lerp(uniformScaleRange.x, uniformScaleRange.y, t);
        }

        public float SampleYRotation(System.Random rnd)
        {
            var t = (float)rnd.NextDouble();
            return Mathf.Lerp(yRotationRange.x, yRotationRange.y, t);
        }
    }
}
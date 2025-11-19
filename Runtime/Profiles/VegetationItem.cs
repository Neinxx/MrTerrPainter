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

        [Header("Landscape 封边石设置")]
        [Tooltip("最小坡度阈值（度）。当地形坡度小于该值时不放置封边石")]
        [Range(0f, 90f)] public float edgeSlopeThreshold = 75f;
        [Tooltip("沿地形法线方向的插入深度范围（米），用于将封边石部分嵌入地形")]
        public Vector2 embedDepthRange = new Vector2(0.1f, 0.3f);
        [Tooltip("封边石资产的参考世界宽度（米），用于 EdgeLine 按笔刷直径归一缩放")]
        public float edgeReferenceWidthMeters = 1f;
        [Tooltip("EdgeLine 自动高度：是否按立面高度自动缩放本地Y尺寸")]
        public bool edgeAutoHeight = true;
        [Tooltip("资产参考高度（米），用于将立面高度换算为本地Y缩放系数")]
        public float edgeReferenceHeightMeters = 1f;
        [Tooltip("在立面坐标系中的偏移：X沿条带right，Y沿世界up，Z沿水平-Forward（贴墙方向）")]
        public Vector3 edgeOffsets = Vector3.zero;
        [Tooltip("沿水平-Forward的探测步长（米）用于查找崖顶")]
        public float edgeLookAheadStep = 0.5f;
        [Tooltip("沿水平-Forward的最大探测距离（米）")]
        public float edgeMaxLookAhead = 5f;

        [Header("FacadeStone 专用设置")]
        [Tooltip("进入陡坡阈值（度），检测立面时的起始陡度阈值")] 
        [Range(0f, 90f)] public float edgeSlopeEnter = 30f;
        [Tooltip("退出至平缓阈值（度），检测立面结束的陡度阈值")] 
        [Range(0f, 90f)] public float edgeSlopeExit = 25f;
        [Tooltip("探测步长（米），沿水平±Forward扫描的步长范围")] 
        [Range(0.05f, 1f)] public float probeStep = 0.3f;
        [Tooltip("最大探测距离（米），沿水平±Forward的扫描上限")] 
        [Range(0.5f, 20f)] public float probeMaxDist = 6f;
        [Tooltip("Facade参考高度（米），用于按立面高度自动设置本地Y缩放")] 
        public float referenceHeightMeters = 1f;
        [Tooltip("Facade偏移：X沿条带right，Y沿世界up，Z沿水平-Forward（贴墙方向）")] 
        public Vector3 offsets = Vector3.zero;

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

        public float SampleEmbedDepth(System.Random rnd)
        {
            var t = (float)rnd.NextDouble();
            return Mathf.Lerp(embedDepthRange.x, embedDepthRange.y, t);
        }
    }
}

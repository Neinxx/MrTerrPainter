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
        [HideInInspector] public Vector2 heightRange = new Vector2(0f, 1000f);
        [HideInInspector] public Vector2 slopeRange = new Vector2(0f, 90f);

        [Header("密度与间距（条目级）")]
        [Tooltip("该预制体的单位面积基础密度，仅用于笔刷绘制时的数量估算。")]
        [Range(0f, 10f)] public float baseDensity = 1f;
        [Tooltip("该预制体的最小间距约束（米），随机生成与绘制均生效。")]
        [Range(0f, 10f)] public float minSpacing = 1.5f;

        [Header("对齐设置")]
        [HideInInspector] public bool alignToTerrainNormal = true;

        [Header("Landscape 封边石设置")]
        [HideInInspector]
        [Tooltip("最小坡度阈值（度）。当地形坡度小于该值时不放置封边石")]
        [Range(0f, 90f)] public float edgeSlopeThreshold = 75f;
        [Tooltip("沿地形法线方向的插入深度范围（米），用于将封边石部分嵌入地形")]
        [HideInInspector] public Vector2 embedDepthRange = new Vector2(0.1f, 0.3f);
        [Tooltip("封边石资产的参考世界宽度（米），用于 EdgeLine 按笔刷直径归一缩放")]
        [HideInInspector] public float edgeReferenceWidthMeters = 1f;
        [Tooltip("EdgeLine 自动高度：是否按立面高度自动缩放本地Y尺寸")]
        [HideInInspector] public bool edgeAutoHeight = true;
        [Tooltip("资产参考高度（米），用于将立面高度换算为本地Y缩放系数")]
        [HideInInspector] public float edgeReferenceHeightMeters = 1f;
        [Tooltip("在立面坐标系中的偏移：X沿条带right，Y沿世界up，Z沿水平-Forward（贴墙方向）")]
        [HideInInspector] public Vector3 edgeOffsets = Vector3.zero;
        [Tooltip("沿水平-Forward的探测步长（米）用于查找崖顶")]
        [HideInInspector] public float edgeLookAheadStep = 0.5f;
        [Tooltip("沿水平-Forward的最大探测距离（米）")]
        [HideInInspector] public float edgeMaxLookAhead = 5f;

        [Header("FacadeStone 专用设置")]
        [HideInInspector]
        [Tooltip("进入陡坡阈值（度），检测立面时的起始陡度阈值")]
        [Range(0f, 90f)] public float edgeSlopeEnter = 30f;
        [Tooltip("退出至平缓阈值（度），检测立面结束的陡度阈值")]
        [Range(0f, 90f)] public float edgeSlopeExit = 25f;
        [Tooltip("探测步长（米），沿水平±Forward扫描的步长范围")]
        [HideInInspector][Range(0.1f, 5f)] public float probeStep = 0.5f;
        [Tooltip("最大探测距离（米），沿水平±Forward的扫描上限")]
        [HideInInspector][Range(0.5f, 20f)] public float probeMaxDist = 6f;
        [Tooltip("Facade参考高度（米），用于按立面高度自动设置本地Y缩放")]
        [HideInInspector] public float referenceHeightMeters = 1f;
        [Tooltip("Facade偏移：X沿条带right，Y沿世界up，Z沿水平-Forward（贴墙方向）")]
        [HideInInspector] public Vector3 offsets = Vector3.zero;

        [Header("堆叠模式 (Stacking)")]
        [HideInInspector]
        [Tooltip("是否启用垂直堆叠模式。若关闭则使用单体拉伸。")]
        public bool edgeStacking = false;
        [Tooltip("堆叠层的沿竖直方向的水平偏移（米），用于多层间错位对齐")]
        [HideInInspector] public float edgeStackingOffsetMeters = 0f;
        [Tooltip("顶部层的额外Y缩放偏置（乘法），用于补偿顶部间隙")]
        [HideInInspector] public float edgeTopScaleBias = 1f;

        [Tooltip("Facade自适应缩放后的额外缩放偏移（XYZ加法）用于微调最终缩放")]
        [HideInInspector] public Vector3 facadeScaleOffset = Vector3.zero;

        [Header("虚拟立面平滑 (Smoothing)")]
        [HideInInspector] public FacadeSmoothingMode facadeSmoothingMode = FacadeSmoothingMode.Mean;
        [Tooltip("平滑窗口大小（奇数>=3），用于均值或高斯平滑")]
        [HideInInspector] public int facadeSmoothingWindow = 3;
        [Tooltip("高斯平滑的Sigma（标准差）")]
        [HideInInspector] public float facadeSmoothingSigma = 1f;

        [Header("放置策略 (SerializeReference)")]
        [SerializeReference] public IPlacementSettings placementSettings;

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

        public Vector3 CoreOffset
        {
            get
            {
                var v = edgeOffsets != Vector3.zero ? edgeOffsets : offsets;
                return new Vector3(v.x, v.y, v.z);
            }
        }

        public float CoreScale
        {
            get
            {
                float lo = Mathf.Max(0.0001f, uniformScaleRange.x);
                float hi = Mathf.Max(lo, uniformScaleRange.y);
                if (Mathf.Approximately(lo, hi)) return lo;
                return (lo + hi) * 0.5f;
            }
        }

        public float CoreSpacing
        {
            get
            {
                return Mathf.Max(minSpacing, 0.01f);
            }
        }

        public void ValidateCore()
        {
            uniformScaleRange.x = Mathf.Max(0.0001f, uniformScaleRange.x);
            uniformScaleRange.y = Mathf.Max(uniformScaleRange.x, uniformScaleRange.y);
            minSpacing = Mathf.Max(0.01f, minSpacing);
        }

        private void OnEnable()
        {
            EnsurePlacementSettings();
            SyncFromPlacementSettings();
        }

        private void OnValidate()
        {
            EnsurePlacementSettings();
            SyncFromPlacementSettings();
        }

        private void EnsurePlacementSettings()
        {
            if (placementSettings == null)
            {
                placementSettings = (prefabType == PrefabType.Landscape)
                    ? (IPlacementSettings)new LandscapePlacementSettings()
                    : new StandardPlacementSettings();
            }
            else
            {
                // 类型切换时自动匹配
                if (prefabType == PrefabType.Landscape && placementSettings is not LandscapePlacementSettings)
                    placementSettings = new LandscapePlacementSettings();
                else if (prefabType != PrefabType.Landscape && placementSettings is not StandardPlacementSettings)
                    placementSettings = new StandardPlacementSettings();
            }
        }

        private void SyncFromPlacementSettings()
        {
            if (placementSettings is StandardPlacementSettings s)
            {
                heightRange = s.heightRange;
                slopeRange = s.slopeRange;
                alignToTerrainNormal = s.alignToTerrainNormal;
            }
            else if (placementSettings is LandscapePlacementSettings l)
            {
                edgeSlopeThreshold = l.edgeSlopeThreshold;
                embedDepthRange = l.embedDepthRange;
                edgeReferenceWidthMeters = l.edgeReferenceWidthMeters;
                edgeAutoHeight = l.edgeAutoHeight;
                edgeReferenceHeightMeters = l.edgeReferenceHeightMeters;
                edgeOffsets = l.edgeOffsets;
                edgeStacking = l.edgeStacking;
                edgeStackingOffsetMeters = l.edgeStackingOffsetMeters;
                edgeTopScaleBias = l.edgeTopScaleBias;
                facadeScaleOffset = l.facadeScaleOffset;

                if (l.facade != null)
                {
                    edgeSlopeEnter = l.facade.edgeSlopeEnter;
                    edgeSlopeExit = l.facade.edgeSlopeExit;
                    probeStep = l.facade.probeStep;
                    probeMaxDist = l.facade.probeMaxDist;
                    referenceHeightMeters = l.facade.referenceHeightMeters;
                    offsets = l.facade.offsets;
                    facadeSmoothingMode = l.facade.facadeSmoothingMode;
                    facadeSmoothingWindow = l.facade.facadeSmoothingWindow;
                    facadeSmoothingSigma = l.facade.facadeSmoothingSigma;
                }
            }
        }
    }
}


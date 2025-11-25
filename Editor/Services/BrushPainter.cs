using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Utils;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using MrTerrainPainter.Editor.Services;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

namespace MrTerrainPainter.Editor.Services
{
    public enum BrushShape { Circle, Square, Strip }
    public enum BrushSettingKey { Shape, Size, Strength, DensityScale, Hardness, Preview, FalloffCurve, MinSpacingJitter, Distribution, StrokeSeed, MaxPoints, Cluster, MixItemsWeighted, LimitPerItem, GlobalSpacingFactor, MixExtraProfiles, UseBurstPoisson, PreviewStyle, StrokeSpacingFactor, StrokeSpacingAbsolute, UseAbsoluteStrokeSpacing }

    public struct BrushPreviewStyle
    {
        public Color fillColor;
        public Color ringColor;
        public Color innerColor;
        public float ringWidth;
        public float innerWidth;
        public bool showLabel;
        public Color labelColor;
        public Vector2 labelOffset;
        public static BrushPreviewStyle Default => new BrushPreviewStyle
        {
            fillColor = new Color(0.24f, 0.65f, 1f, 0.15f),
            ringColor = new Color(1f, 1f, 1f, 0.9f),
            innerColor = new Color(0.24f, 0.65f, 1f, 0.35f),
            ringWidth = 4f,
            innerWidth = 4f,
            showLabel = true,
            labelColor = Color.white,
            labelOffset = new Vector2(0f, 0f)
        };
    }

    public class BrushSettings
    {
        public event System.Action<string> Changed;
        public event System.Action<BrushSettingKey> ChangedKey;

        private static readonly System.Collections.Generic.Dictionary<BrushSettingKey, string> s_nameMap = new System.Collections.Generic.Dictionary<BrushSettingKey, string>
        {
            { BrushSettingKey.Shape, nameof(shape) },
            { BrushSettingKey.Size, nameof(size) },
            { BrushSettingKey.Strength, nameof(strength) },
            { BrushSettingKey.DensityScale, nameof(densityScale) },
            { BrushSettingKey.Hardness, nameof(hardness) },
            { BrushSettingKey.Preview, nameof(preview) },
            { BrushSettingKey.FalloffCurve, nameof(falloffCurve) },
            { BrushSettingKey.MinSpacingJitter, nameof(minSpacingJitter) },
            { BrushSettingKey.Distribution, nameof(distribution) },
            { BrushSettingKey.StrokeSeed, nameof(strokeSeed) },
            { BrushSettingKey.MaxPoints, nameof(maxPoints) },
            { BrushSettingKey.Cluster, nameof(cluster) },
            { BrushSettingKey.MixItemsWeighted, nameof(mixItemsWeighted) },
            { BrushSettingKey.LimitPerItem, nameof(limitPerItem) },
            { BrushSettingKey.GlobalSpacingFactor, nameof(globalSpacingFactor) },
            { BrushSettingKey.MixExtraProfiles, nameof(mixExtraProfiles) },
            { BrushSettingKey.UseBurstPoisson, nameof(useBurstPoisson) },
            { BrushSettingKey.PreviewStyle, nameof(previewStyle) },
            { BrushSettingKey.StrokeSpacingFactor, nameof(strokeSpacingFactor) },
            { BrushSettingKey.StrokeSpacingAbsolute, nameof(strokeSpacingAbsolute) },
            { BrushSettingKey.UseAbsoluteStrokeSpacing, nameof(useAbsoluteStrokeSpacing) },
            { (BrushSettingKey)1001, nameof(adaptiveMinFactor) },
            { (BrushSettingKey)1002, nameof(adaptiveMaxFactor) },
            { (BrushSettingKey)1003, nameof(adaptiveNoiseWeight) },
        };

        private bool Notify(BrushSettingKey key)
        {
            ChangedKey?.Invoke(key);
            if (s_nameMap.TryGetValue(key, out var n)) Changed?.Invoke(n);
            return true;
        }


        private bool SetFloat(ref float field, float value, BrushSettingKey key, float? min = null, float? max = null, bool approximately = true, bool clamp01 = false)
        {
            var v = clamp01 ? Mathf.Clamp01(value) : value;
            if (min.HasValue) v = Mathf.Max(v, min.Value);
            if (max.HasValue) v = Mathf.Min(v, max.Value);
            if ((approximately && Mathf.Approximately(field, v)) || (!approximately && field == v)) return false;
            field = v;
            return Notify(key);
        }

        private bool SetInt(ref int field, int value, BrushSettingKey key, int min = int.MinValue)
        {
            var v = min != int.MinValue ? Mathf.Max(value, min) : value;
            if (field == v) return false;
            field = v;
            return Notify(key);
        }

        private bool SetBool(ref bool field, bool value, BrushSettingKey key)
        {
            if (field == value) return false;
            field = value;
            return Notify(key);
        }

        private bool SetValue<T>(ref T field, T value, BrushSettingKey key) where T : struct
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            return Notify(key);
        }

        private bool SetRef<T>(ref T field, T value, BrushSettingKey key) where T : class
        {
            if (ReferenceEquals(field, value)) return false;
            field = value;
            return Notify(key);
        }

        private BrushShape _shape = BrushShape.Circle;
        public BrushShape shape { get => _shape; set { SetValue(ref _shape, value, BrushSettingKey.Shape); } }

        private float _size = 5f;
        public float size { get => _size; set { SetFloat(ref _size, value, BrushSettingKey.Size, min: 0.01f); } }

        private float _strength = 1f;
        public float strength { get => _strength; set { SetFloat(ref _strength, value, BrushSettingKey.Strength, min: 0f); } }

        private float _densityScale = 1f;
        public float densityScale { get => _densityScale; set { SetFloat(ref _densityScale, value, BrushSettingKey.DensityScale, min: 0f); } }

        private float _hardness = 1f;
        public float hardness { get => _hardness; set { SetFloat(ref _hardness, value, BrushSettingKey.Hardness, clamp01: true); } }

        private bool _preview = true;
        public bool preview { get => _preview; set { SetBool(ref _preview, value, BrushSettingKey.Preview); } }

        private AnimationCurve _falloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        public AnimationCurve falloffCurve { get => _falloffCurve; set { if (value == null) return; SetRef(ref _falloffCurve, value, BrushSettingKey.FalloffCurve); } }

        private float _minSpacingJitter = 0f;
        public float minSpacingJitter { get => _minSpacingJitter; set { SetFloat(ref _minSpacingJitter, value, BrushSettingKey.MinSpacingJitter, min: 0f); } }

        private DistributionType _distribution = DistributionType.Uniform;
        public DistributionType distribution { get => _distribution; set { SetValue(ref _distribution, value, BrushSettingKey.Distribution); } }

        private int _strokeSeed = 0;
        public int strokeSeed { get => _strokeSeed; set { SetInt(ref _strokeSeed, value, BrushSettingKey.StrokeSeed); } }

        private int _maxPoints = 1000;
        public int maxPoints { get => _maxPoints; set { SetInt(ref _maxPoints, value, BrushSettingKey.MaxPoints, min: 1); } }

        private ClusterSettings _cluster = new ClusterSettings { clusterCount = 10, childPerCluster = 5, clusterRadius = 2f, childJitter = 0.2f };
        public ClusterSettings cluster { get => _cluster; set { SetValue(ref _cluster, value, BrushSettingKey.Cluster); } }

        private bool _mixItemsWeighted = true;
        public bool mixItemsWeighted { get => _mixItemsWeighted; set { SetBool(ref _mixItemsWeighted, value, BrushSettingKey.MixItemsWeighted); } }

        private bool _limitPerItem = true;
        public bool limitPerItem { get => _limitPerItem; set { SetBool(ref _limitPerItem, value, BrushSettingKey.LimitPerItem); } }

        private float _globalSpacingFactor = 0f;
        public float globalSpacingFactor { get => _globalSpacingFactor; set { SetFloat(ref _globalSpacingFactor, value, BrushSettingKey.GlobalSpacingFactor, min: 0f); } }

        private bool _mixExtraProfiles = false;
        public bool mixExtraProfiles { get => _mixExtraProfiles; set { SetBool(ref _mixExtraProfiles, value, BrushSettingKey.MixExtraProfiles); } }

        private bool _useBurstPoisson = true;
        public bool useBurstPoisson { get => _useBurstPoisson; set { SetBool(ref _useBurstPoisson, value, BrushSettingKey.UseBurstPoisson); } }

        private BrushPreviewStyle _previewStyle = BrushPreviewStyle.Default;
        public BrushPreviewStyle previewStyle { get => _previewStyle; set { SetValue(ref _previewStyle, value, BrushSettingKey.PreviewStyle); } }

        private float _strokeSpacingFactor = 0.25f;
        public float strokeSpacingFactor { get => _strokeSpacingFactor; set { SetFloat(ref _strokeSpacingFactor, value, BrushSettingKey.StrokeSpacingFactor, min: 0f, max: 2f); } }

        private float _strokeSpacingAbsolute = 0f;
        public float strokeSpacingAbsolute { get => _strokeSpacingAbsolute; set { SetFloat(ref _strokeSpacingAbsolute, value, BrushSettingKey.StrokeSpacingAbsolute, min: 0f); } }

        private bool _useAbsoluteStrokeSpacing = false;
        public bool useAbsoluteStrokeSpacing { get => _useAbsoluteStrokeSpacing; set { SetBool(ref _useAbsoluteStrokeSpacing, value, BrushSettingKey.UseAbsoluteStrokeSpacing); } }

        public float adaptiveMinFactor { get => _adaptiveMinFactor; set { SetFloat(ref _adaptiveMinFactor, value, (BrushSettingKey)1001, min: 0.1f); } }
        public float adaptiveMaxFactor { get => _adaptiveMaxFactor; set { SetFloat(ref _adaptiveMaxFactor, value, (BrushSettingKey)1002, min: 0.1f); } }
        public float adaptiveNoiseWeight { get => _adaptiveNoiseWeight; set { SetFloat(ref _adaptiveNoiseWeight, value, (BrushSettingKey)1003, min: 0.0001f); } }

        private float _adaptiveMinFactor = 0.7f;
        private float _adaptiveMaxFactor = 1.8f;
        private float _adaptiveNoiseWeight = 1f;
    }

    [BurstCompile]
    internal struct TerrainSampleJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> pointsWorld;
        [ReadOnly] public NativeArray<float> heightsPatch;
        public int xBase;
        public int zBase;
        public int width;
        public int height;
        public int hmMax;
        public float dxWorld;
        public float dzWorld;
        public float3 terrainPos;
        public float sizeX;
        public float sizeZ;
        public float sizeY;

        public NativeArray<float> outHeightLocal;
        public NativeArray<float3> outNormal;
        public NativeArray<float> outSlope;

        private float SampleHeight01(float2 uv)
        {
            float u = math.clamp(uv.x, 0f, hmMax);
            float v = math.clamp(uv.y, 0f, hmMax);
            int xi = (int)math.floor(u);
            int zi = (int)math.floor(v);
            int xi1 = math.min(xi + 1, xBase + width - 1);
            int zi1 = math.min(zi + 1, zBase + height - 1);
            xi = math.max(xi, xBase);
            zi = math.max(zi, zBase);
            float fu = u - xi;
            float fv = v - zi;
            int lx = xi - xBase;
            int lz = zi - zBase;
            int lx1 = xi1 - xBase;
            int lz1 = zi1 - zBase;
            float h00 = heightsPatch[lz * width + lx];
            float h10 = heightsPatch[lz * width + lx1];
            float h01 = heightsPatch[lz1 * width + lx];
            float h11 = heightsPatch[lz1 * width + lx1];
            float h0 = math.lerp(h00, h10, fu);
            float h1 = math.lerp(h01, h11, fu);
            return math.lerp(h0, h1, fv);
        }

        private float3 ComputeNormal(float2 uv)
        {
            float u = math.clamp(uv.x, 1f, hmMax - 1f);
            float v = math.clamp(uv.y, 1f, hmMax - 1f);
            int xi = (int)math.floor(u);
            int zi = (int)math.floor(v);
            int lxC = math.clamp(xi - xBase, 0, width - 1);
            int lzC = math.clamp(zi - zBase, 0, height - 1);
            int lxL = math.max(lxC - 1, 0);
            int lxR = math.min(lxC + 1, width - 1);
            int lzU = math.max(lzC - 1, 0);
            int lzD = math.min(lzC + 1, height - 1);
            float hL = heightsPatch[lzC * width + lxL];
            float hR = heightsPatch[lzC * width + lxR];
            float hU = heightsPatch[lzU * width + lxC];
            float hD = heightsPatch[lzD * width + lxC];
            float dhdx = ((hR - hL) * sizeY) / (2f * dxWorld);
            float dhdz = ((hD - hU) * sizeY) / (2f * dzWorld);
            float3 n = math.normalizesafe(new float3(-dhdx, 1f, -dhdz));
            return n;
        }

        private float SampleSlope(float3 n)
        {
            float cosTheta = math.clamp(n.y, -1f, 1f);
            float theta = math.acos(cosTheta);
            return theta * 57.2957795f;
        }

        public void Execute(int index)
        {
            float2 pw = pointsWorld[index];
            float2 pl = new float2(pw.x - terrainPos.x, pw.y - terrainPos.z);
            float2 uv = new float2((pl.x / sizeX) * hmMax, (pl.y / sizeZ) * hmMax);
            float h01 = SampleHeight01(uv);
            float hLocal = h01 * sizeY;
            float3 n = ComputeNormal(uv);
            float slope = SampleSlope(n);
            outHeightLocal[index] = hLocal;
            outNormal[index] = n;
            outSlope[index] = slope;
        }
    }

    public static class BrushPainter
    {
        private static System.Collections.Generic.Dictionary<int, float> s_prefabHeightCache = new System.Collections.Generic.Dictionary<int, float>();
        private static System.Collections.Generic.Dictionary<int, float> s_prefabHorizExtentCache = new System.Collections.Generic.Dictionary<int, float>();
        public static float GetPrefabHeightMeters(GameObject prefab)
        {
            if (prefab == null) return 1f;
            int id = prefab.GetInstanceID();
            if (s_prefabHeightCache.TryGetValue(id, out var h) && h > 0f) return h;
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            float height = 1f;
            if (temp != null)
            {
                try
                {
                    temp.transform.localScale = Vector3.one;
                    var rends = temp.GetComponentsInChildren<Renderer>();
                    if (rends != null && rends.Length > 0)
                    {
                        var b = new Bounds(temp.transform.position, Vector3.zero);
                        for (int i = 0; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        height = Mathf.Max(0.0001f, b.size.y);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(temp);
                }
            }
            s_prefabHeightCache[id] = height;
            return height;
        }
        public static float GetPrefabHorizontalExtentMeters(GameObject prefab)
        {
            if (prefab == null) return 1f;
            int id = prefab.GetInstanceID();
            if (s_prefabHorizExtentCache.TryGetValue(id, out var w) && w > 0f) return w;
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            float extent = 1f;
            if (temp != null)
            {
                try
                {
                    temp.transform.localScale = Vector3.one;
                    var rends = temp.GetComponentsInChildren<Renderer>();
                    if (rends != null && rends.Length > 0)
                    {
                        var b = new Bounds(temp.transform.position, Vector3.zero);
                        for (int i = 0; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        extent = Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.z));
                    }
                }
                finally
                {
                    Object.DestroyImmediate(temp);
                }
            }
            s_prefabHorizExtentCache[id] = extent;
            return extent;
        }
        private static bool s_configCompleteCached;
        static BrushPainter()
        {
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            s_configCompleteCached = cfg != null && MrTerrainPainter.Editor.Config.ConfigTools.IsComplete(cfg, out _);
            MrTerrainPainter.Editor.Config.ConfigTools.CompletenessChanged += v => { s_configCompleteCached = v; };
            MrTerrainPainter.Editor.Config.ConfigTools.ConfigUpdated += () =>
            {
                var c = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                s_configCompleteCached = c != null && MrTerrainPainter.Editor.Config.ConfigTools.IsComplete(c, out _);
            };
        }
        public class Grid
        {
            private float cellSize;
            private readonly Dictionary<(int, int), List<Vector2>> cells = new();
            public Grid(float spacing)
            {
                cellSize = Mathf.Max(spacing, 0.01f);
            }
            public void Reset(float spacing)
            {
                cellSize = Mathf.Max(spacing, 0.01f);
                cells.Clear();
            }
            public void Clear()
            {
                cells.Clear();
            }
            private (int, int) Key(Vector2 p)
            {
                return (Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize));
            }
            public void Add(Vector2 p)
            {
                var k = Key(p);
                if (!cells.TryGetValue(k, out var list)) { list = new List<Vector2>(); cells[k] = list; }
                list.Add(p);
            }
            public bool HasNearby(Vector2 p, float minDist)
            {
                var k = Key(p);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var nk = (k.Item1 + dx, k.Item2 + dy);
                        if (!cells.TryGetValue(nk, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (Vector2.SqrMagnitude(list[i] - p) < minDist * minDist) return true;
                        }
                    }
                return false;
            }
        }
        private static Grid s_sharedGrid;
        private static System.Collections.Generic.Dictionary<int, Grid> s_itemGridCache = new();
        public static void ClearCache()
        {
            s_prefabHeightCache.Clear();
            s_prefabHorizExtentCache.Clear();
            // Grid 在每次绘制周期会 Reset，如需彻底释放也可：
            foreach (var kv in s_itemGridCache) kv.Value.Clear();
            s_itemGridCache.Clear();
            s_sharedGrid = null;
        }
        private static Grid GetSharedGrid(float spacing)
        {
            if (s_sharedGrid == null) s_sharedGrid = new Grid(spacing);
            else s_sharedGrid.Reset(spacing);
            return s_sharedGrid;
        }
        private static Grid GetItemGrid(int idx, float spacing)
        {
            if (!s_itemGridCache.TryGetValue(idx, out var g)) { g = new Grid(spacing); s_itemGridCache[idx] = g; }
            else g.Reset(spacing);
            return g;
        }
        public static void DrawPreview(SceneInteractionService.PreviewData data, BrushSettings bs)
        {
            if (bs == null || !bs.preview) return;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var st = bs.previewStyle;
            var fill = st.fillColor;
            var ring = st.ringColor;
            var inner = st.innerColor;
            if (!s_configCompleteCached)
            {
                fill = new Color(1f, 0f, 0f, 0.15f);
                ring = new Color(1f, 0f, 0f, 0.9f);
                inner = new Color(1f, 0.4f, 0.4f, 0.35f);
            }
            var center = data.hasData ? data.center : Vector3.zero;
            if (bs.shape == BrushShape.Circle)
            {
                Handles.color = fill;
                Handles.DrawSolidDisc(center, Vector3.up, bs.size);
                Handles.color = ring;
                Handles.DrawWireDisc(center, Vector3.up, bs.size);
                float innerR = Mathf.Clamp(bs.size * Mathf.Clamp01(1f - bs.hardness), 0f, bs.size);
                if (innerR > 0f)
                {
                    Handles.color = inner;
                    Handles.DrawWireDisc(center, Vector3.up, innerR);
                }
                if (st.showLabel)
                {
                    var sp = HandleUtility.WorldToGUIPoint(center + new Vector3(0f, 0.02f, bs.size + 0.1f));
                    Handles.BeginGUI();
                    var c = GUI.color;
                    GUI.color = st.labelColor;
                    GUI.Label(new Rect(sp.x + st.labelOffset.x, sp.y + st.labelOffset.y, 100, 20), $"Size {bs.size:F1}");
                    GUI.color = c;
                    Handles.EndGUI();
                }
            }
            else
            {
                Vector3 half = new Vector3(bs.size, 0f, bs.size);
                Handles.color = fill;
                Handles.DrawSolidRectangleWithOutline(new[]
                {
                    center + new Vector3(-half.x, 0f, -half.z),
                    center + new Vector3(-half.x, 0f, half.z),
                    center + new Vector3(half.x, 0f, half.z),
                    center + new Vector3(half.x, 0f, -half.z)
                }, fill, ring);
            }
            if (bs.distribution == DistributionType.EdgeLine && data.slices != null && data.slices.Count > 1)
            {
                var cfgC = MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                var bottom = data.bottomLine?.ToArray();
                var top = data.topLine?.ToArray();
                if (bottom != null && bottom.Length > 1 && top != null && top.Length > 1)
                {
                    Handles.color = cfgC != null ? cfgC.facadePreviewBottomColor : new Color(0f, 1f, 0f, 0.8f);
                    Handles.DrawAAPolyLine(st.ringWidth, bottom);
                    Handles.color = cfgC != null ? cfgC.facadePreviewTopColor : new Color(1f, 0.2f, 0.2f, 0.8f);
                    Handles.DrawAAPolyLine(st.ringWidth, top);
                }
                Handles.Label(center + Vector3.up * 0.25f, $"Render {data.prefabW:F2}m x {data.prefabH:F2}m");
            }
        }

        public static void DrawPreview(Vector3 center, Vector3 normal, BrushSettings bs)
        {
            if (bs == null || !bs.preview) return;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var st = bs.previewStyle;
            var fill = st.fillColor;
            var ring = st.ringColor;
            var inner = st.innerColor;
            var cfg1 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            bool useNormalDir = cfg1 != null && cfg1.normalDirection;
            if (!s_configCompleteCached)
            {
                fill = new Color(1f, 0f, 0f, 0.15f);
                ring = new Color(1f, 0f, 0f, 0.9f);
                inner = new Color(1f, 0.4f, 0.4f, 0.35f);
            }
            var planeN = useNormalDir ? normal.normalized : Vector3.up;
            var raisedCenter = center + planeN * 0.02f;
            if (bs.shape == BrushShape.Circle)
            {
                Handles.color = fill;
                Handles.DrawSolidDisc(raisedCenter, planeN, bs.size);
                Handles.color = ring;
                const int segments = 64;
                var pts = new Vector3[segments + 1];
                var tangent = Vector3.Normalize(Vector3.Cross(planeN, Vector3.right));
                if (tangent == Vector3.zero) tangent = Vector3.Normalize(Vector3.Cross(planeN, Vector3.forward));
                var bitangent = Vector3.Normalize(Vector3.Cross(planeN, tangent));
                for (int i = 0; i <= segments; i++)
                {
                    float a = (i / (float)segments) * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    pts[i] = raisedCenter + (tangent * dir.x + bitangent * dir.z) * bs.size;
                }
                Handles.DrawAAPolyLine(st.ringWidth, pts);
                float innerR = Mathf.Clamp(bs.size * Mathf.Clamp01(1f - bs.hardness), 0f, bs.size);
                if (innerR > 0f)
                {
                    Handles.color = inner;
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = (i / (float)segments) * Mathf.PI * 2f;
                        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        pts[i] = raisedCenter + (tangent * dir.x + bitangent * dir.z) * innerR;
                    }
                    Handles.DrawAAPolyLine(st.innerWidth, pts);
                }
                if (st.showLabel)
                {
                    var sp = HandleUtility.WorldToGUIPoint(raisedCenter + (bitangent * (bs.size + 0.1f)));
                    Handles.BeginGUI();
                    var c = GUI.color;
                    GUI.color = st.labelColor;
                    GUI.Label(new Rect(sp.x + st.labelOffset.x, sp.y + st.labelOffset.y, 100, 20), $"Size {bs.size:F1}");
                    GUI.color = c;
                    Handles.EndGUI();
                }
            }
            else
            {
                Vector3 half = new Vector3(bs.size, 0f, bs.size);
                Handles.color = fill;
                Handles.DrawSolidRectangleWithOutline(new[]
                {
                    raisedCenter + new Vector3(-half.x, 0f, -half.z),
                    raisedCenter + new Vector3(-half.x, 0f, half.z),
                    raisedCenter + new Vector3(half.x, 0f, half.z),
                    raisedCenter + new Vector3(half.x, 0f, -half.z)
                }, fill, ring);
            }
            if (useNormalDir)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.9f);
                var tip = raisedCenter + planeN * (bs.size * 0.6f);
                Handles.DrawAAPolyLine(6f, new Vector3[] { raisedCenter, tip });
            }
            // 当不使用法线方向时，不绘制法线/上方向辅助线
            if (bs.distribution == DistributionType.EdgeLine)
            {
                Terrain t = null;
                var terrains = Terrain.activeTerrains;
                for (int i = 0; i < terrains.Length; i++)
                {
                    if (TerrainUtils.IsWithinTerrainBounds(terrains[i], center)) { t = terrains[i]; break; }
                }
                var profile = MrTerrainPainter.Editor.Tools.MTPBrushContext.CurrentProfile;
                var itemRef = profile != null ? profile.Items.FirstOrDefault(it => it != null && it.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape) : null;
                if (t != null && itemRef != null)
                {
                    var cfgPrev2 = MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                    var req2 = new FacadeDetectionService.FacadeTraceBuilder()
                        .Terrain(t)
                        .Start(center)
                        .Length(bs.size * 2f)
                        .FromItem(itemRef)
                        .FromConfig(cfgPrev2)
                        .Build();
                    var slices = FacadeDetectionService.TraceVirtualFacade(req2);
                    if (slices != null && slices.Count > 0)
                    {
                        var grid = new Grid(Mathf.Max(itemRef.minSpacing, 0.01f));
                        var filtered = new System.Collections.Generic.List<FacadeDetectionService.CliffSlice>(slices.Count);
                        for (int i = 0; i < slices.Count; i++)
                        {
                            var s = slices[i];
                            if (!IsWithinBrush(s.BottomPosition, center, bs.size, bs.shape)) continue;
                            var p2 = new Vector2(s.BottomPosition.x - t.transform.position.x, s.BottomPosition.z - t.transform.position.z);
                            if (grid.HasNearby(p2, Mathf.Max(itemRef.minSpacing, 0.01f))) continue;
                            grid.Add(p2);
                            filtered.Add(s);
                        }
                        if (filtered.Count > 4)
                        {
                            filtered = MrTerrainPainter.Editor.Utils.SplineUtils.ResampleSlicesSmoothly(filtered, Mathf.Max(itemRef.CoreSpacing, 0.01f));
                        }
                        if (filtered.Count > 1) DrawFacadeRailsAndTicks(filtered, st, ring, itemRef);
                        DrawFacadeSlicesPreview(filtered, bs);
                    }
                    else Handles.Label(center, "未检测到立面，尝试提高 edgeSlopeEnter 或增大笔刷半径");
                    var nCenter2 = Vector3.up;
                    if (TerrainUtils.TryGetHeightAndNormal(t, center, out var hC2, out var nC2)) nCenter2 = nC2;
                    var forward2 = Vector3.ProjectOnPlane(nCenter2, Vector3.up);
                    if (forward2.sqrMagnitude > 1e-6f)
                    {
                        forward2.Normalize();
                        var right3 = Vector3.Cross(Vector3.up, forward2).normalized;
                        float rail2 = Mathf.Max(itemRef.edgeReferenceWidthMeters, 0.01f) * 0.5f;
                        int seg2 = 32;
                        var left2 = new Vector3[seg2 + 1];
                        var rightPts2 = new Vector3[seg2 + 1];
                        for (int i = 0; i <= seg2; i++)
                        {
                            float u = Mathf.Lerp(-bs.size, bs.size, i / (float)seg2);
                            left2[i] = center + right3 * (u - rail2);
                            rightPts2[i] = center + right3 * (u + rail2);
                        }
                        Handles.color = ring;
                        Handles.DrawAAPolyLine(st.ringWidth, left2);
                        Handles.DrawAAPolyLine(st.ringWidth, rightPts2);
                    }
                    float rw2 = GetPrefabHorizontalExtentMeters(itemRef.prefab);
                    float rh2 = GetPrefabHeightMeters(itemRef.prefab);
                    Handles.Label(center + planeN * 0.25f, $"Render {rw2:F2}m x {rh2:F2}m");
                }
            }
        }

        private static void DrawFacadeRailsAndTicks(System.Collections.Generic.List<FacadeDetectionService.CliffSlice> filtered, BrushPreviewStyle st, Color ring, MrTerrainPainter.Runtime.Profiles.VegetationItem itemRef)
        {
            var bottomLine = new Vector3[filtered.Count];
            var topLine = new Vector3[filtered.Count];
            for (int i = 0; i < filtered.Count; i++)
            {
                bottomLine[i] = filtered[i].BottomPosition;
                topLine[i] = filtered[i].TopPosition;
            }
            var cfgC = MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            Handles.color = cfgC != null ? cfgC.facadePreviewBottomColor : new Color(0f, 1f, 0f, 0.8f);
            Handles.DrawAAPolyLine(st.ringWidth, bottomLine);
            Handles.color = cfgC != null ? cfgC.facadePreviewTopColor : new Color(1f, 0.2f, 0.2f, 0.8f);
            Handles.DrawAAPolyLine(st.ringWidth, topLine);
            Handles.color = new Color(1f, 1f, 1f, 0.3f);
            float acc = 0f;
            for (int i = 0; i < filtered.Count - 1; i++)
            {
                acc += Vector3.Distance(bottomLine[i], bottomLine[i + 1]);
                if (acc >= Mathf.Max(1f, itemRef.CoreSpacing) || i == 0 || i == filtered.Count - 2)
                {
                    Handles.DrawLine(bottomLine[i], topLine[i]);
                    acc = 0f;
                }
            }
            float rail = Mathf.Max(itemRef.edgeReferenceWidthMeters, 0.01f) * 0.5f;
            var leftRail = new Vector3[filtered.Count];
            var rightRail = new Vector3[filtered.Count];
            for (int i = 0; i < filtered.Count; i++)
            {
                var ss = filtered[i];
                leftRail[i] = ss.BottomPosition - ss.Normal * rail;
                rightRail[i] = ss.BottomPosition + ss.Normal * rail;
            }
            Handles.color = ring;
            Handles.DrawAAPolyLine(st.ringWidth, leftRail);
            Handles.DrawAAPolyLine(st.ringWidth, rightRail);
            float tickLen = rail * 0.3f;
            float spacing = Mathf.Max(itemRef.CoreSpacing, 0.01f);
            for (int i = 0; i < filtered.Count; i++)
            {
                var ss = filtered[i];
                var a = ss.BottomPosition - ss.Normal * tickLen;
                var b = ss.BottomPosition + ss.Normal * tickLen;
                Handles.DrawAAPolyLine(st.innerWidth, new Vector3[] { a, b });
            }
        }

        public static void Paint(Terrain terrain, VegetationProfile profile, Vector3 center, BrushSettings bs, System.Random rnd, VegetationGenerator.PlacementOverrides? ov = null)
        {
            if (terrain == null || profile == null || profile.IsEmpty()) return; // 提前返回
            var td = terrain.terrainData;
            if (td == null) return; // 提前返回

            float radius = bs.size;
            var areaWorldShared = new Bounds(new Vector3(center.x, terrain.transform.position.y, center.z), new Vector3(radius * 2f, 1f, radius * 2f));
            var hbShared = TerrainUtils.FetchHeightsBlock(terrain, areaWorldShared, Allocator.TempJob);
            var typeToNode = VegetationGenerator.BuildTypeToNodeMapping();

            if (bs.mixItemsWeighted)
            {
                var allItems = profile.Items.Where(it => it != null && it.IsValid()).ToList();
                if (allItems.Count == 0) { if (hbShared.heights.IsCreated) hbShared.heights.Dispose(); return; }
                int seed = bs.strokeSeed != 0 ? bs.strokeSeed : profile.randomSeed;
                var centerXZ = new Vector2(center.x, center.z);
                int totalDesired = 0;
                var perItemLimit = new Dictionary<int, int>();
                for (int i = 0; i < allItems.Count; i++)
                {
                    var it = allItems[i];
                    int c = Mathf.Clamp(Mathf.RoundToInt(it.baseDensity * bs.densityScale * bs.strength * 10f), 0, 500);
                    perItemLimit[i] = c;
                    totalDesired += c;
                }
                float minSpacingForAll = 0.5f;
                if (allItems.Count > 0)
                {
                    float best = bs.size;
                    for (int i = 0; i < allItems.Count; i++)
                    {
                        var s = Mathf.Max(allItems[i].CoreSpacing, 0.01f);
                        if (s < best) best = s;
                    }
                    minSpacingForAll = best;
                }
                int candidateCount = VegetationGenerator.ComputeDesiredCandidateCount(bs.shape, radius, minSpacingForAll, Mathf.Max(1, totalDesired), bs.maxPoints);
                List<Vector2> candidates = null;
                bool useFacade = bs.distribution == DistributionType.EdgeLine && allItems.Any(it => it.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape);
                if (useFacade)
                {
                    var landItems = allItems.Where(it => it.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape).ToList();
                    var itemRef = landItems.FirstOrDefault();
                    if (itemRef == null) { if (hbShared.heights.IsCreated) hbShared.heights.Dispose(); return; }
                    var cfg2 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                    var req3 = new FacadeDetectionService.FacadeTraceBuilder()
                        .Terrain(terrain)
                        .Start(center)
                        .Length(radius * 2f)
                        .Slopes(itemRef.edgeSlopeEnter, itemRef.edgeSlopeExit)
                        .Step(itemRef.probeStep)
                        .Smoothing(cfg2 != null ? cfg2.facadeSmoothMode : MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode.Gaussian,
                                   cfg2 != null ? Mathf.Max(3, cfg2.facadeSmoothWindow) : 5,
                                   cfg2 != null ? Mathf.Max(0.1f, cfg2.facadeSmoothSigma) : 1f)
                        .FromConfig(cfg2)
                        .Build();
                    var slices = FacadeDetectionService.TraceVirtualFacade(req3);
                    if (slices == null || slices.Count == 0)
                    {
                        Handles.Label(center, "未检测到立面，尝试提高 edgeSlopeEnter 或增大笔刷半径");
                        if (hbShared.heights.IsCreated) hbShared.heights.Dispose();
                        return;
                    }
                    PlaceEdgeLineWithPipeline(terrain, center, radius, bs, landItems, typeToNode, slices, rnd);
                    if (hbShared.heights.IsCreated) hbShared.heights.Dispose();
                    return;
                    float mixMinSpacing = landItems.Count > 0 ? landItems.Min(li => Mathf.Max(li.CoreSpacing, 0.01f)) : 0.01f;
                    var mixGridLocal = new Grid(mixMinSpacing);
                    var parent = typeToNode.TryGetValue(MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape, out var tf) ? tf : null;
                    if (parent == null) { VegetationGenerator.LogMissingMappingOnce(MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape); if (hbShared.heights.IsCreated) hbShared.heights.Dispose(); return; }
                    var weights = landItems.Select(li => Mathf.Clamp(li.weight, 0.0001f, 100f)).ToArray();
                    float sumW = weights.Sum();
                    for (int si = 0; si < slices.Count; si++)
                    {
                        var s = slices[si];
                        if (!IsWithinBrush(s.BottomPosition, center, radius, bs.shape)) continue;
                        var prev = slices[Mathf.Max(0, si - 1)].BottomPosition;
                        var next = slices[Mathf.Min(slices.Count - 1, si + 1)].BottomPosition;
                        var tp = s.BottomPosition - prev; tp.y = 0f;
                        var tn = next - s.BottomPosition; tn.y = 0f;
                        float angRad = 0f;
                        float ds = 0.0001f;
                        if (tp.sqrMagnitude > 1e-6f && tn.sqrMagnitude > 1e-6f)
                        {
                            angRad = Mathf.Deg2Rad * Vector3.Angle(tp.normalized, tn.normalized);
                            ds = Mathf.Max(0.0001f, (tp.magnitude + tn.magnitude) * 0.5f);
                        }
                        float kappa = angRad / ds; // 曲率近似
                        float minW = landItems.Count > 0 ? landItems.Min(li => GetPrefabHorizontalExtentMeters(li.prefab)) : 0.01f;
                        float maxW = landItems.Count > 0 ? landItems.Max(li => GetPrefabHorizontalExtentMeters(li.prefab)) : minW;
                        float alphaW = 1.0f;
                        float desiredW = Mathf.Clamp(alphaW / Mathf.Max(kappa, 0.0001f), minW, maxW);
                        float Llocal = ds;
                        // 构建近似集合：使用包裹度评分（宽度拟合 + 覆盖度）
                        float tolScoreBase = Mathf.Max(desiredW * 0.15f, 0.2f);
                        var near = new System.Collections.Generic.List<(int idx, float score)>(landItems.Count);
                        for (int i = 0; i < landItems.Count; i++)
                        {
                            float wv = GetPrefabHorizontalExtentMeters(landItems[i].prefab);
                            float rendererH1 = GetPrefabHeightMeters(landItems[i].prefab);
                            var cfgLocal1 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                            float minH1 = cfgLocal1 != null ? Mathf.Max(0.0001f, cfgLocal1.minFacadeHeightMeters) : 0.0001f;
                            float uniLocal = Mathf.Max(minH1 / Mathf.Max(0.0001f, rendererH1), s.Height / Mathf.Max(0.0001f, rendererH1));
                            float coverage = Mathf.Clamp01(Llocal / Mathf.Max(0.0001f, wv * uniLocal));
                            float fitW = Mathf.Abs(wv - desiredW);
                            float fitC = 1f - coverage;
                            float score = 0.7f * fitW + 0.3f * fitC;
                            if (score <= tolScoreBase) near.Add((i, score));
                        }
                        int pick;
                        if (near.Count == 0)
                        {
                            float bestScore = float.MaxValue; pick = 0;
                            for (int i = 0; i < landItems.Count; i++)
                            {
                                float wv = GetPrefabHorizontalExtentMeters(landItems[i].prefab);
                                float rendererH2 = GetPrefabHeightMeters(landItems[i].prefab);
                                var cfgLocal2 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                                float minH2 = cfgLocal2 != null ? Mathf.Max(0.0001f, cfgLocal2.minFacadeHeightMeters) : 0.0001f;
                                float uniLocal = Mathf.Max(minH2 / Mathf.Max(0.0001f, rendererH2), s.Height / Mathf.Max(0.0001f, rendererH2));
                                float coverage = Mathf.Clamp01(Llocal / Mathf.Max(0.0001f, wv * uniLocal));
                                float score = 0.7f * Mathf.Abs(wv - desiredW) + 0.3f * (1f - coverage);
                                if (score < bestScore) { bestScore = score; pick = i; }
                            }
                        }
                        else if (near.Count == 1)
                        {
                            pick = near[0].idx;
                        }
                        else
                        {
                            float sumLocal = 0f;
                            for (int k = 0; k < near.Count; k++) sumLocal += Mathf.Clamp(landItems[near[k].idx].weight, 0.0001f, 100f);
                            float rPick = (float)rnd.NextDouble() * Mathf.Max(0.0001f, sumLocal);
                            float accW = 0f; pick = near[0].idx;
                            for (int k = 0; k < near.Count; k++)
                            {
                                accW += Mathf.Clamp(landItems[near[k].idx].weight, 0.0001f, 100f);
                                if (rPick <= accW) { pick = near[k].idx; break; }
                            }
                        }
                        var item = landItems[pick];
                        var p2 = new Vector2(s.BottomPosition.x - terrain.transform.position.x, s.BottomPosition.z - terrain.transform.position.z);
                        float rendererW = GetPrefabHorizontalExtentMeters(item.prefab);
                        float rendererH = GetPrefabHeightMeters(item.prefab);
                        var cfgLocal = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                        float minH = cfgLocal != null ? Mathf.Max(0.0001f, cfgLocal.minFacadeHeightMeters) : 0.0001f;
                        float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), s.Height / Mathf.Max(0.0001f, rendererH));
                        float spacingThresh = Mathf.Max(item.CoreSpacing, rendererW * uni);
                        if (mixGridLocal.HasNearby(p2, spacingThresh)) continue;
                        var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
                        var go = VegetationPool.Get(terrain, item, pick, parent, "Create Vegetation Instance");
                        if (go == null) continue;
                        go.transform.position = s.BottomPosition;
                        go.transform.rotation = Quaternion.LookRotation(s.Normal, s.Direction);
                        var baseScale = new Vector3(uni, uni, uni);
                        var finalScale = new Vector3(
                            Mathf.Max(0.0001f, baseScale.x + item.facadeScaleOffset.x),
                            Mathf.Max(0.0001f, baseScale.y + item.facadeScaleOffset.y),
                            Mathf.Max(0.0001f, baseScale.z + item.facadeScaleOffset.z));
                        go.transform.localScale = finalScale;
                        float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                        var offsConf = item.offsets;
                        var off = rightAxis * offsConf.x + s.Direction * offsConf.y + (-s.Normal.normalized) * (depth + Mathf.Max(0f, offsConf.z));
                        go.transform.position += off;
                        var vi = go.GetComponent<VegetationInstance>() ?? go.AddComponent<VegetationInstance>();
                        vi.sourceTerrain = terrain;
                        vi.profileItemIndex = pick;
                        vi.sourcePrefabName = item.prefab.name;
                        VegetationPool.IndexRegister(terrain, go);
                        mixGridLocal.Add(p2);
                    }
                    if (hbShared.heights.IsCreated) hbShared.heights.Dispose();
                    return;
                }
                else
                {
                    var candReq = new VegetationGenerator.CandidateBuilder()
                        .Center(centerXZ)
                        .Radius(radius)
                        .Shape(bs.shape)
                        .Desired(candidateCount)
                        .MinSpacing(minSpacingForAll)
                        .Jitter(bs.minSpacingJitter)
                        .Seed(seed)
                        .FromBrush(bs)
                        .Random(rnd)
                        .Build();
                    candidates = VegetationGenerator.BuildCandidates(candReq);
                }
                foreach (var kv in s_itemGridCache) kv.Value.Clear();
                Grid globalGrid = null;
                float globalFactor = Mathf.Max(0f, bs.globalSpacingFactor);
                if (globalFactor > 0f) globalGrid = new Grid(globalFactor);
                var weightCounts = new List<int>(allItems.Count);
                int totalWeight = 0;
                for (int i = 0; i < allItems.Count; i++)
                {
                    var it = allItems[i];
                    int w = Mathf.Clamp(Mathf.RoundToInt(it.weight * 10f), 1, 100);
                    weightCounts.Add(w);
                    totalWeight += w;
                }
                int nWeights = weightCounts.Count;
                var prob = new float[nWeights];
                var alias = new int[nWeights];
                if (nWeights > 0)
                {
                    var small = new System.Collections.Generic.Queue<int>();
                    var large = new System.Collections.Generic.Queue<int>();
                    float sum = Mathf.Max(1, totalWeight);
                    for (int i = 0; i < nWeights; i++) prob[i] = (weightCounts[i] / sum) * nWeights;
                    for (int i = 0; i < nWeights; i++) { if (prob[i] < 1f) small.Enqueue(i); else large.Enqueue(i); }
                    while (small.Count > 0 && large.Count > 0)
                    {
                        int s = small.Dequeue();
                        int l = large.Dequeue();
                        alias[s] = l;
                        prob[l] = (prob[l] + prob[s]) - 1f;
                        if (prob[l] < 1f) small.Enqueue(l); else large.Enqueue(l);
                    }
                    while (large.Count > 0) { prob[large.Dequeue()] = 1f; }
                    while (small.Count > 0) { prob[small.Dequeue()] = 1f; }
                }
                NativeArray<float> outH2 = default;
                NativeArray<float3> outN2 = default;
                NativeArray<float> outS2 = default;
                if (candidates.Count > 0)
                {
                    var pts2 = new NativeArray<float2>(candidates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    for (int iPt = 0; iPt < candidates.Count; iPt++) { var c2 = candidates[iPt]; pts2[iPt] = new float2(c2.x, c2.y); }
                    outH2 = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                    outN2 = new NativeArray<float3>(candidates.Count, Allocator.TempJob);
                    outS2 = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                    var job2 = new TerrainSampleJob
                    {
                        pointsWorld = pts2,
                        heightsPatch = hbShared.heights,
                        xBase = hbShared.xBase,
                        zBase = hbShared.zBase,
                        width = hbShared.width,
                        height = hbShared.height,
                        hmMax = td.heightmapResolution - 1,
                        dxWorld = hbShared.dxWorld,
                        dzWorld = hbShared.dzWorld,
                        terrainPos = new float3(terrain.transform.position.x, terrain.transform.position.y, terrain.transform.position.z),
                        sizeX = td.size.x,
                        sizeZ = td.size.z,
                        sizeY = td.size.y,
                        outHeightLocal = outH2,
                        outNormal = outN2,
                        outSlope = outS2,
                    };
                    var handle2 = job2.Schedule(candidates.Count, 64);
                    handle2.Complete();
                    pts2.Dispose();
                }
                var candidatesWorld = new List<Vector3>(candidates.Count);
                for (int i = 0; i < candidates.Count; i++) candidatesWorld.Add(new Vector3(candidates[i].x, center.y, candidates[i].y));
                var heightsArr = outH2.IsCreated ? outH2.ToArray() : null;
                var slopesArr = outS2.IsCreated ? outS2.ToArray() : null;
                var normalsArr = outN2.IsCreated ? outN2.Select(v => (Vector3)v).ToArray() : null;
                for (int iItem = 0; iItem < allItems.Count; iItem++)
                {
                    var item = allItems[iItem];
                    var targetParent = typeToNode.TryGetValue(item.prefabType, out var tf) ? tf : null;
                    if (targetParent == null) { VegetationGenerator.LogMissingMappingOnce(item.prefabType); continue; }
                    var sampler = new CandidateSamplerFromList(candidates, center.y);
                    var filter = new HeightSlopeFilter(item);
                    var mutator = new StandardMutator(item);
                    var spawner = new PooledSpawner();
                    var pipeline = new VegetationPipeline(sampler, filter, mutator, spawner);
                    pipeline.Run(terrain, center, radius, item, iItem, targetParent, candidatesWorld, heightsArr, slopesArr, normalsArr);
                }
                BrushEngine.ReleaseList(candidates);
                if (outH2.IsCreated) outH2.Dispose();
                if (outN2.IsCreated) outN2.Dispose();
                if (outS2.IsCreated) outS2.Dispose();
                if (hbShared.heights.IsCreated) hbShared.heights.Dispose();
                return;
            }

            var items = profile.Items;
            for (int it = 0; it < items.Count; it++)
            {
                var item = items[it];
                if (item == null || !item.IsValid()) continue;
                int count = Mathf.RoundToInt(item.baseDensity * bs.densityScale * bs.strength * 10f);
                count = Mathf.Clamp(count, 0, 500);
                if (count <= 0) continue;

                float spacing = Mathf.Max(item.CoreSpacing, 0.01f);
                float jitter = Mathf.Max(bs.minSpacingJitter, 0f);
                int seed = bs.strokeSeed != 0 ? bs.strokeSeed : profile.randomSeed;
                var centerXZ = new Vector2(center.x, center.z);
                int desired = VegetationGenerator.ComputeDesiredCandidateCount(bs.shape, radius, spacing, Mathf.Min(count, bs.maxPoints), bs.maxPoints);
                List<Vector2> candidates = null;
                FacadeDetectionService.FacadeInfo facadeInfo = default;
                bool useFacade = (bs.distribution == DistributionType.EdgeLine && item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape);
                if (useFacade)
                {
                    var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                    var targetParent = typeToNode.TryGetValue(item.prefabType, out var tf) ? tf : null;
                    if (targetParent == null) { VegetationGenerator.LogMissingMappingOnce(item.prefabType); continue; }
                    FacadeDetectionService.ProcessFacadeAndPlace(terrain, center, radius, item, bs.shape, s =>
                    {
                        var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
                        float h = s.Height;
                        float refH = Mathf.Max(item.edgeReferenceHeightMeters, 0.0001f);
                        bool stacking = item.edgeStacking && h > refH * 1.5f;
                        if (!stacking)
                        {
                            var go = VegetationPool.Get(terrain, item, it, targetParent, "Create Vegetation Instance");
                            if (go == null) return;
                            go.transform.position = s.BottomPosition;
                            go.transform.rotation = Quaternion.LookRotation(s.Normal, s.Direction);
                            float rendererH = GetPrefabHeightMeters(item.prefab);
                            var cfgLocal = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                            float minH = cfgLocal != null ? Mathf.Max(0.0001f, cfgLocal.minFacadeHeightMeters) : 0.0001f;
                            float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), h / Mathf.Max(0.0001f, rendererH));
                            var baseScale = new Vector3(uni, uni, uni);
                            var finalScale = new Vector3(
                                Mathf.Max(0.0001f, baseScale.x + item.facadeScaleOffset.x),
                                Mathf.Max(0.0001f, baseScale.y + item.facadeScaleOffset.y),
                                Mathf.Max(0.0001f, baseScale.z + item.facadeScaleOffset.z));
                            go.transform.localScale = finalScale;
                            float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                            var offsConf = item.offsets;
                            var off = rightAxis * offsConf.x + s.Direction * offsConf.y + (-s.Normal.normalized) * (depth + Mathf.Max(0f, offsConf.z));
                            go.transform.position += off;
                            var vi = go.GetComponent<VegetationInstance>() ?? go.AddComponent<VegetationInstance>();
                            vi.sourceTerrain = terrain;
                            vi.profileItemIndex = it;
                            vi.sourcePrefabName = item.prefab.name;
                            VegetationPool.IndexRegister(terrain, go);
                        }
                        else
                        {
                            int layers = Mathf.Max(1, Mathf.CeilToInt(h / refH));
                            float per = h / layers;
                            float used = 0f;
                            for (int L = 0; L < layers; L++)
                            {
                                float currH = L == layers - 1 ? (h - used) : per;
                                used += currH;
                                var go = VegetationPool.Get(terrain, item, it, targetParent, "Create Vegetation Instance");
                                if (go == null) continue;
                                var basePos = s.BottomPosition + s.Direction * (per * L + Mathf.Max(0f, item.edgeStackingOffsetMeters));
                                go.transform.position = basePos;
                                go.transform.rotation = Quaternion.LookRotation(s.Normal, s.Direction);
                                float rendererH2 = GetPrefabHeightMeters(item.prefab);
                                var cfgLocal2 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                                float minH2 = cfgLocal2 != null ? Mathf.Max(0.0001f, cfgLocal2.minFacadeHeightMeters) : 0.0001f;
                                float uni2 = Mathf.Max(minH2 / Mathf.Max(0.0001f, rendererH2), currH / Mathf.Max(0.0001f, rendererH2));
                                var baseScale2 = new Vector3(uni2, uni2, uni2);
                                var finalScale2 = new Vector3(
                                    Mathf.Max(0.0001f, baseScale2.x + item.facadeScaleOffset.x),
                                    Mathf.Max(0.0001f, baseScale2.y + item.facadeScaleOffset.y),
                                    Mathf.Max(0.0001f, baseScale2.z + item.facadeScaleOffset.z));
                                go.transform.localScale = finalScale2;
                                float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                                var offsConf2 = item.offsets;
                                var off = rightAxis * offsConf2.x + s.Direction * offsConf2.y + (-s.Normal.normalized) * (depth + Mathf.Max(0f, offsConf2.z));
                                go.transform.position += off;
                                var vi = go.GetComponent<VegetationInstance>() ?? go.AddComponent<VegetationInstance>();
                                vi.sourceTerrain = terrain;
                                vi.profileItemIndex = it;
                                vi.sourcePrefabName = item.prefab.name;
                                VegetationPool.IndexRegister(terrain, go);
                            }
                        }
                    });
                    continue;
                }
                else
                {
                    candidates = (bs.distribution == DistributionType.EdgeLine && item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape)
                        ? BuildFacadeStripCandidates(terrain, center, radius, bs, item)
                        : VegetationGenerator.BuildCandidates(
                        centerXZ,
                        radius,
                        bs.shape,
                        desired,
                        spacing,
                        jitter,
                        seed + it,
                        bs.distribution,
                        bs.useBurstPoisson,
                        bs.cluster,
                        bs.adaptiveMinFactor,
                        bs.adaptiveMaxFactor,
                        bs.adaptiveNoiseWeight,
                        rnd);
                }
                var grid = GetSharedGrid(spacing);
                int placed = 0;
                NativeArray<float> outH = default;
                NativeArray<float3> outN = default;
                NativeArray<float> outS = default;
                if (!useFacade && candidates.Count > 0)
                {
                    var pts = new NativeArray<float2>(candidates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    for (int iPt = 0; iPt < candidates.Count; iPt++)
                    {
                        var c2 = candidates[iPt];
                        pts[iPt] = new float2(c2.x, c2.y);
                    }
                    outH = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                    outN = new NativeArray<float3>(candidates.Count, Allocator.TempJob);
                    outS = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                    var job = new TerrainSampleJob
                    {
                        pointsWorld = pts,
                        heightsPatch = hbShared.heights,
                        xBase = hbShared.xBase,
                        zBase = hbShared.zBase,
                        width = hbShared.width,
                        height = hbShared.height,
                        hmMax = td.heightmapResolution - 1,
                        dxWorld = hbShared.dxWorld,
                        dzWorld = hbShared.dzWorld,
                        terrainPos = new float3(terrain.transform.position.x, terrain.transform.position.y, terrain.transform.position.z),
                        sizeX = td.size.x,
                        sizeZ = td.size.z,
                        sizeY = td.size.y,
                        outHeightLocal = outH,
                        outNormal = outN,
                        outSlope = outS,
                    };
                    var handle = job.Schedule(candidates.Count, 64);
                    handle.Complete();
                    pts.Dispose();
                }
                for (int ci = 0; ci < candidates.Count && placed < count; ci++)
                {
                    var c = candidates[ci];
                    Vector3 p = new Vector3(c.x, center.y, c.y);
                    if (!TerrainUtils.IsWithinTerrainBounds(terrain, p)) continue;
                    float h = p.y;
                    Vector3 n = Vector3.up;
                    float slope = 0f;
                    if (useFacade)
                    {
                        p.y = facadeInfo.bottomPos.y;
                        n = facadeInfo.forward.normalized;
                        slope = 90f;
                    }
                    else
                    {
                        h = outH[ci] + terrain.transform.position.y;
                        n = (Vector3)outN[ci];
                        slope = outS[ci];
                        p.y = h;
                    }
                    float dx0 = p.x - center.x;
                    float dz0 = p.z - center.z;
                    float t = Mathf.Clamp01(Mathf.Sqrt(dx0 * dx0 + dz0 * dz0) / radius);
                    float edge = 1f - t;
                    float acceptance = bs.falloffCurve != null ? bs.falloffCurve.Evaluate(1f - t) : Mathf.Lerp(1f, edge, Mathf.Clamp01(bs.hardness));
                    if (rnd.NextDouble() > acceptance) continue;
                    if (!VegetationGenerator.MatchTerrain(item, h - terrain.transform.position.y, slope, ov)) continue;
                    if (item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape && !useFacade)
                    {
                        if (slope < Mathf.Clamp(item.edgeSlopeThreshold, 0f, 90f)) continue;
                        var fwd = n.normalized;
                        var upProj = Vector3.ProjectOnPlane(Vector3.up, fwd);
                        if (upProj.sqrMagnitude < 1e-6f) upProj = Vector3.Cross(fwd, Vector3.right).normalized;
                        var right = Vector3.Normalize(Vector3.Cross(upProj, fwd));
                        float step = Mathf.Max(item.CoreSpacing, 0.01f);
                        float u = Vector3.Dot(p - center, right);
                        float w = Vector3.Dot(p - center, fwd);
                        float snappedU = Mathf.Round(u / step) * step;
                        var pDesired = center + right * snappedU + fwd * w;
                        if (TerrainUtils.TryGetHeightAndNormal(terrain, pDesired, out float h2, out Vector3 n2)) { p = new Vector3(pDesired.x, h2, pDesired.z); n = n2; }
                        float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                        var horiz = Vector3.ProjectOnPlane(-n.normalized, Vector3.up);
                        if (horiz.sqrMagnitude > 1e-6f)
                        {
                            horiz.Normalize();
                            var offset = horiz * depth;
                            p = new Vector3(p.x + offset.x, h, p.z + offset.z);
                        }
                    }
                    var p2 = new Vector2(p.x - terrain.transform.position.x, p.z - terrain.transform.position.z);
                    if (bs.globalSpacingFactor > 0f)
                    {
                        float gspace = spacing * bs.globalSpacingFactor;
                        if (gspace > 0f && grid.HasNearby(p2, gspace)) continue;
                    }
                    if (grid.HasNearby(p2, spacing)) continue;
                    grid.Add(p2);
                    var targetParent = typeToNode.TryGetValue(item.prefabType, out var tf) ? tf : null;
                    if (targetParent == null) { VegetationGenerator.LogMissingMappingOnce(item.prefabType); continue; }
                    var ovScaled = ov;
                    if (useFacade)
                    {
                        // 已在上方通过 slices 直接放置
                    }
                    else
                    {
                        if (bs.distribution == DistributionType.EdgeLine && item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape)
                        {
                            float refW = Mathf.Max(item.edgeReferenceWidthMeters, 0.0001f);
                            float scale = ((radius * 2f) / refW) * Mathf.Max(0.0001f, item.CoreScale);
                            var o = ovScaled.HasValue ? ovScaled.Value : new VegetationGenerator.PlacementOverrides();
                            o.scaleRange = new Vector2(scale, scale);
                            ovScaled = o;
                        }
                        VegetationGenerator.PlaceItem(item, p, n, terrain, it, targetParent, rnd, ovScaled);
                    }
                    placed++;
                }
                BrushEngine.ReleaseList(candidates);
                if (outH.IsCreated) outH.Dispose();
                if (outN.IsCreated) outN.Dispose();
                if (outS.IsCreated) outS.Dispose();
            }
            if (hbShared.heights.IsCreated) hbShared.heights.Dispose();
        }

        private static void PlaceEdgeLineWithPipeline(
            Terrain terrain,
            Vector3 center,
            float radius,
            BrushSettings bs,
            System.Collections.Generic.List<MrTerrainPainter.Runtime.Profiles.VegetationItem> landItems,
            System.Collections.Generic.Dictionary<MrTerrainPainter.Runtime.Profiles.PrefabType, Transform> typeToNode,
            System.Collections.Generic.List<FacadeDetectionService.CliffSlice> slices,
            System.Random rnd)
        {
            var cfg2 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            float mixMinSpacing = landItems.Count > 0 ? landItems.Min(li => Mathf.Max(li.CoreSpacing, 0.01f)) : 0.01f;
            var parent = typeToNode.TryGetValue(MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape, out var tf) ? tf : null;
            if (parent == null) { VegetationGenerator.LogMissingMappingOnce(MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape); return; }
            var sampler = new EdgeLineSampler(slices, mixMinSpacing, center, bs.shape);
            var candidatesWorld = sampler.Sample(center, radius);
            if (candidatesWorld == null || candidatesWorld.Count == 0) return;
            var heightsArr = new float[candidatesWorld.Count];
            var slopesArr = new float[candidatesWorld.Count];
            var normalsArr = new Vector3[candidatesWorld.Count];
            for (int i = 0; i < candidatesWorld.Count; i++)
            {
                var nearest = slices[Mathf.Clamp(i, 0, slices.Count - 1)];
                heightsArr[i] = nearest.Height;
                var n = nearest.Normal;
                normalsArr[i] = n;
                slopesArr[i] = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f)) * 57.29578f;
            }
            var weights = landItems.Select(li => Mathf.Clamp(li.weight, 0.0001f, 100f)).ToArray();
            float sumW = Mathf.Max(0.0001f, weights.Sum());
            var filter = new FacadeConstraintFilter(cfg2 != null ? cfg2.minFacadeHeightMeters : 0.0001f);
            var spawner = new PooledSpawner();
            for (int i = 0; i < candidatesWorld.Count; i++)
            {
                float rPick = (float)rnd.NextDouble() * sumW;
                float acc = 0f; int pick = 0;
                for (int k = 0; k < landItems.Count; k++) { acc += weights[k]; if (rPick <= acc) { pick = k; break; } }
                var item = landItems[pick];
                var mutator = new EdgeLineMutator();
                var pipeline = new VegetationPipeline(new CandidateSamplerFromList(new System.Collections.Generic.List<Vector2> { new Vector2(candidatesWorld[i].x, candidatesWorld[i].z) }, candidatesWorld[i].y), filter, mutator, spawner);
                pipeline.Run(terrain, center, radius, item, pick, parent, new System.Collections.Generic.List<Vector3> { candidatesWorld[i] }, new float[] { heightsArr[i] }, new float[] { slopesArr[i] }, new Vector3[] { normalsArr[i] });
            }
        }

        public static void PaintMixed(Terrain terrain, IReadOnlyList<VegetationProfile> profiles, Vector3 center, BrushSettings bs, System.Random rnd, VegetationGenerator.PlacementOverrides? ov = null)
        {
            if (terrain == null || profiles == null || profiles.Count == 0) return;
            var td = terrain.terrainData;
            if (td == null) return;
            var allItems = new List<VegetationItem>();
            var typeToNode = VegetationGenerator.BuildTypeToNodeMapping();
            for (int pi = 0; pi < profiles.Count; pi++)
            {
                var p = profiles[pi];
                if (p == null || p.IsEmpty()) continue;
                var items = p.Items;
                for (int ii = 0; ii < items.Count; ii++)
                {
                    var it = items[ii];
                    if (it == null || !it.IsValid()) continue;
                    allItems.Add(it);
                }
            }
            if (allItems.Count == 0) return;
            int seed = bs.strokeSeed != 0 ? bs.strokeSeed : profiles[0].randomSeed;
            float radius = bs.size;
            var centerXZ = new Vector2(center.x, center.z);
            int totalDesired = 0;
            var perItemLimit = new Dictionary<int, int>();
            for (int i = 0; i < allItems.Count; i++)
            {
                var it = allItems[i];
                int c = Mathf.Clamp(Mathf.RoundToInt(it.baseDensity * bs.densityScale * bs.strength * 10f), 0, 500);
                perItemLimit[i] = c;
                totalDesired += c;
            }
            float minSpacingForAll = 0.5f;
            if (allItems.Count > 0)
            {
                float best = bs.size;
                for (int i = 0; i < allItems.Count; i++)
                {
                    var s = Mathf.Max(allItems[i].minSpacing, 0.01f);
                    if (s < best) best = s;
                }
                minSpacingForAll = best;
            }
            int candidateCount = VegetationGenerator.ComputeDesiredCandidateCount(bs.shape, radius, minSpacingForAll, Mathf.Max(1, totalDesired), bs.maxPoints);
            List<Vector2> candidates = null;

            bool useFacade = bs.distribution == DistributionType.EdgeLine && allItems.Any(it => it.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape);
            if (useFacade)
            {
                var landItems = allItems.Where(it => it.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape).ToList();
                var itemRef = landItems.First();
                var cfg2 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                var slices = FacadeDetectionService.TraceVirtualFacade(
                    terrain,
                    center,
                    radius * 2f,
                    itemRef.edgeSlopeEnter,
                    itemRef.edgeSlopeExit,
                    itemRef.probeStep,
                    cfg2 != null ? cfg2.facadeSmoothMode : MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode.Gaussian,
                    cfg2 != null ? Mathf.Max(3, cfg2.facadeSmoothWindow) : 5,
                    cfg2 != null ? Mathf.Max(0.1f, cfg2.facadeSmoothSigma) : 1f);
                slices = FacadeDetectionService.ApplyGlobalConstraints(slices, cfg2 != null ? cfg2.minFacadeHeightMeters : 0.3f, true, cfg2 != null ? cfg2.curveOffsetRightMeters : 0f, cfg2 != null ? cfg2.curveOffsetOutMeters : 0f);
                if (slices != null && slices.Count > 0)
                {
                    float maxRendererW = landItems.Count > 0 ? landItems.Max(li => GetPrefabHorizontalExtentMeters(li.prefab)) : 0.01f;
                    float minLenSeg2 = Mathf.Max(maxRendererW, itemRef.edgeReferenceWidthMeters);
                    slices = FacadeDetectionService.FilterByMinimumWidth(slices, minLenSeg2, Mathf.Max(itemRef.CoreSpacing, 0.01f), 30f);
                }
                if (slices == null || slices.Count == 0)
                {
                    Handles.Label(center, "未检测到立面，尝试提高 edgeSlopeEnter 或增大笔刷半径");
                    return;
                }
                float mixMinSpacing = landItems.Count > 0 ? landItems.Min(li => Mathf.Max(li.minSpacing, 0.01f)) : 0.01f;
                var mixGridLocal = new Grid(mixMinSpacing);
                var parent = typeToNode.TryGetValue(MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape, out var tf) ? tf : null;
                if (parent == null) { VegetationGenerator.LogMissingMappingOnce(MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape); return; }
                var weights = landItems.Select(li => Mathf.Clamp(li.weight, 0.0001f, 100f)).ToArray();
                float sumW = weights.Sum();
                for (int si = 0; si < slices.Count; si++)
                {
                    var s = slices[si];
                    if (!IsWithinBrush(s.BottomPosition, center, radius, bs.shape)) continue;
                    int pick = 0;
                    float r = (float)rnd.NextDouble() * sumW;
                    float acc = 0f;
                    for (int i = 0; i < weights.Length; i++) { acc += weights[i]; if (r <= acc) { pick = i; break; } }
                    var item = landItems[pick];
                    var p2 = new Vector2(s.BottomPosition.x - terrain.transform.position.x, s.BottomPosition.z - terrain.transform.position.z);
                    float rendererW = GetPrefabHorizontalExtentMeters(item.prefab);
                    float rendererHeight2 = GetPrefabHeightMeters(item.prefab);
                    var cfgM = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                    float minHF = cfgM != null ? Mathf.Max(0.0001f, cfgM.minFacadeHeightMeters) : 0.0001f;
                    float hLoc = s.Height;
                    float rendererH = GetPrefabHeightMeters(item.prefab);
                    float uniS = Mathf.Max(minHF / Mathf.Max(0.0001f, rendererH), hLoc / Mathf.Max(0.0001f, rendererH));
                    float spacingThresh = Mathf.Max(item.minSpacing, rendererW * uniS);
                    if (mixGridLocal.HasNearby(p2, spacingThresh)) continue;
                    var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
                    float h = s.Height;
                    var go = VegetationPool.Get(terrain, item, pick, parent, "Create Vegetation Instance");
                    if (go == null) continue;
                    go.transform.position = s.BottomPosition;
                    go.transform.rotation = Quaternion.LookRotation(s.Normal, s.Direction);
                    var cfgLocal = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                    float minH = cfgLocal != null ? Mathf.Max(0.0001f, cfgLocal.minFacadeHeightMeters) : 0.0001f;
                    float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererHeight2), h / Mathf.Max(0.0001f, rendererHeight2));
                    var baseScale = new Vector3(uni, uni, uni);
                    var finalScale = new Vector3(
                        Mathf.Max(0.0001f, baseScale.x + item.facadeScaleOffset.x),
                        Mathf.Max(0.0001f, baseScale.y + item.facadeScaleOffset.y),
                        Mathf.Max(0.0001f, baseScale.z + item.facadeScaleOffset.z));
                    go.transform.localScale = finalScale;
                    float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                    var offsConf = item.offsets;
                    var off = rightAxis * offsConf.x + s.Direction * offsConf.y + (-s.Normal.normalized) * (depth + Mathf.Max(0f, offsConf.z));
                    go.transform.position += off;
                    var vi = go.GetComponent<VegetationInstance>() ?? go.AddComponent<VegetationInstance>();
                    vi.sourceTerrain = terrain;
                    vi.profileItemIndex = pick;
                    vi.sourcePrefabName = item.prefab.name;
                    VegetationPool.IndexRegister(terrain, go);
                    mixGridLocal.Add(p2);
                }
                return;
            }
            else
            {
                candidates = bs.distribution == DistributionType.EdgeLine
                    ? BuildFacadeStripCandidates(terrain, center, radius, bs, null)
                    : VegetationGenerator.BuildCandidates(
                    centerXZ,
                    radius,
                    bs.shape,
                    candidateCount,
                    minSpacingForAll,
                    bs.minSpacingJitter,
                    seed,
                    bs.distribution,
                    bs.useBurstPoisson,
                    bs.cluster,
                    bs.adaptiveMinFactor,
                    bs.adaptiveMaxFactor,
                    bs.adaptiveNoiseWeight,
                    rnd);
            }
            foreach (var kv in s_itemGridCache) kv.Value.Clear();
            Grid globalGrid = null;
            float globalFactor = Mathf.Max(0f, bs.globalSpacingFactor);
            if (globalFactor > 0f) globalGrid = new Grid(globalFactor);
            var weightCounts = new List<int>(allItems.Count);
            int totalWeight = 0;
            for (int i = 0; i < allItems.Count; i++)
            {
                var it = allItems[i];
                int w = Mathf.Clamp(Mathf.RoundToInt(it.weight * 10f), 1, 100);
                weightCounts.Add(w);
                totalWeight += w;
            }
            int nWeights = weightCounts.Count;
            var prob = new float[nWeights];
            var alias = new int[nWeights];
            if (nWeights > 0)
            {
                var small = new System.Collections.Generic.Queue<int>();
                var large = new System.Collections.Generic.Queue<int>();
                float sum = Mathf.Max(1, totalWeight);
                for (int i = 0; i < nWeights; i++)
                {
                    prob[i] = (weightCounts[i] / sum) * nWeights;
                }
                for (int i = 0; i < nWeights; i++)
                {
                    if (prob[i] < 1f) small.Enqueue(i); else large.Enqueue(i);
                }
                while (small.Count > 0 && large.Count > 0)
                {
                    int s = small.Dequeue();
                    int l = large.Dequeue();
                    alias[s] = l;
                    prob[l] = (prob[l] + prob[s]) - 1f;
                    if (prob[l] < 1f) small.Enqueue(l); else large.Enqueue(l);
                }
                while (large.Count > 0) { prob[large.Dequeue()] = 1f; }
                while (small.Count > 0) { prob[small.Dequeue()] = 1f; }
            }
            NativeArray<float> outH2 = default;
            NativeArray<float3> outN2 = default;
            NativeArray<float> outS2 = default;
            if (!useFacade && candidates.Count > 0)
            {
                var areaWorld2 = new Bounds(new Vector3(center.x, terrain.transform.position.y, center.z), new Vector3(radius * 2f, 1f, radius * 2f));
                var hb2 = TerrainUtils.FetchHeightsBlock(terrain, areaWorld2, Allocator.TempJob);
                var pts2 = new NativeArray<float2>(candidates.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                for (int iPt = 0; iPt < candidates.Count; iPt++)
                {
                    var c2 = candidates[iPt];
                    pts2[iPt] = new float2(c2.x, c2.y);
                }
                outH2 = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                outN2 = new NativeArray<float3>(candidates.Count, Allocator.TempJob);
                outS2 = new NativeArray<float>(candidates.Count, Allocator.TempJob);
                var job2 = new TerrainSampleJob
                {
                    pointsWorld = pts2,
                    heightsPatch = hb2.heights,
                    xBase = hb2.xBase,
                    zBase = hb2.zBase,
                    width = hb2.width,
                    height = hb2.height,
                    hmMax = terrain.terrainData.heightmapResolution - 1,
                    dxWorld = hb2.dxWorld,
                    dzWorld = hb2.dzWorld,
                    terrainPos = new float3(terrain.transform.position.x, terrain.transform.position.y, terrain.transform.position.z),
                    sizeX = terrain.terrainData.size.x,
                    sizeZ = terrain.terrainData.size.z,
                    sizeY = terrain.terrainData.size.y,
                    outHeightLocal = outH2,
                    outNormal = outN2,
                    outSlope = outS2,
                };
                var handle2 = job2.Schedule(candidates.Count, 64);
                handle2.Complete();
                hb2.heights.Dispose();
                pts2.Dispose();
            }
            for (int ci = 0; ci < candidates.Count; ci++)
            {
                var c = candidates[ci];
                Vector3 p = new Vector3(c.x, center.y, c.y);
                if (!TerrainUtils.IsWithinTerrainBounds(terrain, p)) continue;
                float h = p.y;
                Vector3 n = Vector3.up;
                float slope = 0f;
                if (useFacade)
                {
                    // FacadeStone路径已上方处理并返回
                }
                else
                {
                    h = outH2[ci] + terrain.transform.position.y;
                    n = (Vector3)outN2[ci];
                    slope = outS2[ci];
                    p.y = h;
                }
                float dx0 = p.x - center.x;
                float dz0 = p.z - center.z;
                float t = Mathf.Clamp01(Mathf.Sqrt(dx0 * dx0 + dz0 * dz0) / radius);
                float acceptance = bs.falloffCurve != null ? bs.falloffCurve.Evaluate(1f - t) : 1f;
                if (rnd.NextDouble() > acceptance) continue;
                int tries = 3;
                while (tries-- > 0)
                {
                    if (totalWeight <= 0 || allItems.Count == 0) break;
                    if (nWeights <= 0) break;
                    int col = rnd.Next(0, nWeights);
                    float frac = (float)rnd.NextDouble();
                    int idx = frac < prob[col] ? col : alias[col];
                    if (idx < 0) break;
                    if (bs.limitPerItem && perItemLimit.TryGetValue(idx, out var remain) && remain <= 0)
                    {
                        continue;
                    }
                    var p2 = new Vector2(p.x - terrain.transform.position.x, p.z - terrain.transform.position.z);
                    var item = allItems[idx];
                    if (globalGrid != null && globalFactor > 0f)
                    {
                        float gspace = Mathf.Max(item.minSpacing, 0.01f) * globalFactor;
                        if (gspace > 0f && globalGrid.HasNearby(p2, gspace)) { continue; }
                    }
                    var grid = GetItemGrid(idx, Mathf.Max(item.minSpacing, 0.01f));
                    if (grid.HasNearby(p2, Mathf.Max(item.minSpacing, 0.01f))) { continue; }
                    if (!VegetationGenerator.MatchTerrain(item, h - terrain.transform.position.y, slope, ov)) { continue; }
                    if (item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape && !useFacade)
                    {
                        if (slope < Mathf.Clamp(item.edgeSlopeThreshold, 0f, 90f)) { continue; }
                        var fwd = n.normalized;
                        var upProj = Vector3.ProjectOnPlane(Vector3.up, fwd);
                        if (upProj.sqrMagnitude < 1e-6f) upProj = Vector3.Cross(fwd, Vector3.right).normalized;
                        var right = Vector3.Normalize(Vector3.Cross(upProj, fwd));
                        float step = Mathf.Max(item.minSpacing, 0.01f);
                        float u = Vector3.Dot(p - center, right);
                        float w = Vector3.Dot(p - center, fwd);
                        float snappedU = Mathf.Round(u / step) * step;
                        var pDesired = center + right * snappedU + fwd * w;
                        if (TerrainUtils.TryGetHeightAndNormal(terrain, pDesired, out float h2, out Vector3 n2)) { p = new Vector3(pDesired.x, h2, pDesired.z); n = n2; }
                        float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                        var horiz = Vector3.ProjectOnPlane(-n.normalized, Vector3.up);
                        if (horiz.sqrMagnitude > 1e-6f)
                        {
                            horiz.Normalize();
                            var offset = horiz * depth;
                            p = new Vector3(p.x + offset.x, h2, p.z + offset.z);
                        }
                    }
                    var targetParent = typeToNode.TryGetValue(item.prefabType, out var tf) ? tf : null;
                    if (targetParent == null) { VegetationGenerator.LogMissingMappingOnce(item.prefabType); continue; }
                    var ovScaled = ov;
                    if (item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape && useFacade)
                    {
                        // FacadeStone路径已上方处理并返回
                    }
                    else
                    {
                        if (bs.distribution == DistributionType.EdgeLine && item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape)
                        {
                            float refW = Mathf.Max(item.edgeReferenceWidthMeters, 0.0001f);
                            float scale = (radius * 2f) / refW;
                            var o = ovScaled.HasValue ? ovScaled.Value : new VegetationGenerator.PlacementOverrides();
                            o.scaleRange = new Vector2(scale, scale);
                            ovScaled = o;
                        }
                        VegetationGenerator.PlaceItem(item, p, n, terrain, idx, targetParent, rnd, ovScaled);
                    }
                    grid.Add(p2);
                    if (globalGrid != null && globalFactor > 0f)
                    {
                        float gspace = Mathf.Max(item.minSpacing, 0.01f) * globalFactor;
                        if (gspace > 0f) globalGrid.Add(p2);
                    }
                    if (bs.limitPerItem && perItemLimit.ContainsKey(idx))
                    {
                        perItemLimit[idx] = Mathf.Max(0, perItemLimit[idx] - 1);
                        if (perItemLimit[idx] == 0)
                        {
                            totalWeight -= weightCounts[idx];
                            weightCounts[idx] = 0;
                        }
                    }
                    break;
                }
            }
            BrushEngine.ReleaseList(candidates);
            if (outH2.IsCreated) outH2.Dispose();
            if (outN2.IsCreated) outN2.Dispose();
            if (outS2.IsCreated) outS2.Dispose();
        }

        public static void Erase(Vector3 center, BrushSettings bs, bool eraseAll, IReadOnlyList<GameObject> onlyTypes = null)
        {
            float radius = bs.size;
            var candidates = new System.Collections.Generic.List<GameObject>();
            var terrain = Terrain.activeTerrains.Length > 0 ? Terrain.activeTerrains[0] : null;
            if (terrain != null)
            {
                VegetationPool.QueryInRadius(terrain, center, radius, candidates);
            }
            if (candidates.Count == 0)
            {
                var hits = Physics.OverlapSphere(center, radius);
                for (int i = 0; i < hits.Length; i++) candidates.Add(hits[i].gameObject);
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                var go = candidates[i];
                var vi = go.GetComponent<VegetationInstance>();
                if (vi == null) continue;
                if (!eraseAll && onlyTypes != null && onlyTypes.Count > 0)
                {
                    bool match = false;
                    for (int t = 0; t < onlyTypes.Count; t++)
                    {
                        if (go.name.StartsWith(onlyTypes[t].name)) { match = true; break; }
                    }
                    if (!match) continue;
                }
                VegetationPool.Recycle(vi.sourceTerrain, go, "Erase Vegetation Instance");
            }
        }

        // 基于容器子物体的擦除（无需碰撞体）
        public static void Erase(Terrain terrain, Vector3 center, BrushSettings bs, bool eraseAll, IReadOnlyList<GameObject> onlyTypes = null)
        {
            if (terrain == null) return; // 提前返回
            float radius = bs.size;

            // 1) 组合所有潜在的根容器：默认地形容器 + 设置映射的父节点
            var roots = new List<Transform>();
            var defaultContainer = terrain.transform.Find($"Vegetation_{terrain.name}");
            if (defaultContainer != null) roots.Add(defaultContainer);

            // 聚合所有可用配置实例，避免拿到空数组实例导致擦除失败
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            var configs = cfg != null ? new MrTerrainPainter.Editor.Config.MrTerrainPainterConfig[] { cfg } : MrTerrainPainter.Editor.Config.ConfigTools.GetAllConfigsCached();
            if (configs != null && configs.Length > 0)
            {
                var set = new HashSet<Transform>();
                for (int ci = 0; ci < configs.Length; ci++)
                {
                    var c = configs[ci];
                    var entries = c != null ? c.mappingEntries : null;
                    if (entries == null || entries.Count == 0) continue;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var tf = entries[i]?.node;
                        if (tf == null) continue;
                        if (set.Add(tf)) roots.Add(tf);
                    }
                }
            }

            if (roots.Count == 0)
            {
                // 2) 无根容器时，回退到物理擦除（需要碰撞体）
                Erase(center, bs, eraseAll, onlyTypes);
                return;
            }

            // 收集待回收对象（遍历所有根的所有子孙）
            var toRecycle = new List<GameObject>();
            for (int r = 0; r < roots.Count; r++)
            {
                var root = roots[r];
                if (root == null) continue;
                CollectInRadius(root, center, radius, eraseAll, onlyTypes, toRecycle);
            }

            for (int i = 0; i < toRecycle.Count; i++)
            {
                var go = toRecycle[i];
                if (go == null) continue;
                var vi = go.GetComponent<VegetationInstance>();
                var srcTerrain = vi != null ? vi.sourceTerrain : terrain;
                VegetationPool.Recycle(srcTerrain, go, "Erase Vegetation Instance");
            }
        }

        private static List<Vector2> BuildFacadeStripCandidates(Terrain terrain, Vector3 center, float radius, BrushSettings bs, VegetationItem itemOrNull)
        {
            var list = new List<Vector2>();
            if (!TerrainUtils.TryGetHeightAndNormal(terrain, center, out float h, out Vector3 nCenter)) return list;
            float slopeCenter = TerrainUtils.ComputeSlope(nCenter);
            float thr = itemOrNull != null ? Mathf.Clamp(itemOrNull.edgeSlopeThreshold, 0f, 90f) : 75f;
            if (slopeCenter < thr) return list;
            var up = Vector3.up;
            var forward = Vector3.ProjectOnPlane(nCenter, up);
            if (forward.sqrMagnitude < 1e-6f) return list;
            forward.Normalize();
            var right = Vector3.Cross(up, forward).normalized;
            float length = bs.size * 2f;
            float step = itemOrNull != null ? Mathf.Max(itemOrNull.minSpacing, 0.01f) : Mathf.Max(0.5f, 0.01f);
            for (float u = -length * 0.5f; u <= length * 0.5f + 0.0001f; u += step)
            {
                var p = center + right * u;
                list.Add(new Vector2(p.x, p.z));
            }
            return list;
        }

        private static List<Vector2> BuildFacadeStripCandidatesFromInfo(FacadeDetectionService.FacadeInfo info, BrushSettings bs, VegetationItem item)
        {
            var list = new List<Vector2>();
            float length = bs.size * 2f;
            float step = Mathf.Max(item.minSpacing, 0.01f);
            for (float u = -length * 0.5f; u <= length * 0.5f + 0.0001f; u += step)
            {
                var p = info.bottomPos + info.right * u;
                list.Add(new Vector2(p.x, p.z));
            }
            return list;
        }

        private static void CollectInRadius(Transform root, Vector3 center, float radius, bool eraseAll, IReadOnlyList<GameObject> onlyTypes, List<GameObject> outList)
        {
            if (root == null) return; // 提前返回
            // DFS 遍历所有子孙，查找 VegetationInstance
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null) continue;
                var go = t.gameObject;
                var vi = go.GetComponent<VegetationInstance>();
                if (vi != null)
                {
                    var p = t.position;
                    float dx = p.x - center.x;
                    float dz = p.z - center.z;
                    if (dx * dx + dz * dz <= radius * radius)
                    {
                        if (!eraseAll && onlyTypes != null && onlyTypes.Count > 0)
                        {
                            bool match = false;
                            for (int i = 0; i < onlyTypes.Count; i++)
                            {
                                if (go.name.StartsWith(onlyTypes[i].name)) { match = true; break; }
                            }
                            if (match)
                            {
                                outList.Add(go);
                            }
                        }
                        else
                        {
                            outList.Add(go);
                        }
                    }
                }
                for (int i = 0; i < t.childCount; i++)
                {
                    stack.Push(t.GetChild(i));
                }
            }
        }





        private static float SampleRange(Vector2 range, System.Random rnd)
        {
            return Mathf.Lerp(range.x, range.y, (float)rnd.NextDouble());
        }


        private static void CreateFacadeInstance(VegetationItem item, Vector3 pos, Vector3 forward, Terrain terrain, int itemIndex, Transform parent, System.Random rnd, FacadeDetectionService.FacadeInfo info, VegetationGenerator.PlacementOverrides? ov)
        {
            if (item.prefab == null) return;
            var go = VegetationPool.Get(terrain, item, itemIndex, parent, "Paint Vegetation Instance");
            if (go == null) return;
            go.transform.position = pos;

            float baseScale = item.SampleScale(rnd);
            go.transform.localScale = Vector3.one * baseScale;

            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            bool useNormal = cfg != null && cfg.normalDirection;

            Quaternion rot = useNormal
                ? Quaternion.LookRotation(forward, (Vector3.ProjectOnPlane(Vector3.up, forward).sqrMagnitude < 1e-6f ? Vector3.Cross(forward, Vector3.right).normalized : Vector3.ProjectOnPlane(Vector3.up, forward)))
                : Quaternion.identity;
            go.transform.rotation = rot;

            if (useNormal && item.edgeAutoHeight)
            {
                float yScale = info.heightMeters > 0f ? (info.heightMeters / Mathf.Max(item.edgeReferenceHeightMeters, 0.0001f)) : baseScale;
                go.transform.localScale = new Vector3(baseScale, yScale, baseScale);
                var up = Vector3.up;
                var right = info.right;
                var horizFwd = info.forward;
                float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
                var off = right * item.offsets.x + up * item.offsets.y + (-horizFwd) * (depth + item.offsets.z);
                go.transform.position += off;
            }

            var vi = go.GetComponent<VegetationInstance>();
            if (vi == null) vi = go.AddComponent<VegetationInstance>();
            vi.sourceTerrain = terrain;
            vi.profileItemIndex = itemIndex;
            vi.sourcePrefabName = item.prefab.name;
            VegetationPool.IndexRegister(terrain, go);
        }


        private static void DrawFacadeSlicesPreview(System.Collections.Generic.List<FacadeDetectionService.CliffSlice> slices, BrushSettings bs)
        {
            if (bs == null || !bs.preview || slices == null || slices.Count == 0) return;
            Handles.color = Color.green;
            for (int i = 0; i < slices.Count - 1; i++) Handles.DrawLine(slices[i].BottomPosition, slices[i + 1].BottomPosition);
            Handles.color = Color.red;
            for (int i = 0; i < slices.Count - 1; i++) Handles.DrawLine(slices[i].TopPosition, slices[i + 1].TopPosition);
            Handles.color = Color.white;
            for (int i = 0; i < slices.Count; i++) Handles.DrawLine(slices[i].BottomPosition, slices[i].TopPosition);
            Handles.color = Color.blue;
            for (int i = 0; i < slices.Count; i++) Handles.ArrowHandleCap(0, slices[i].BottomPosition, Quaternion.LookRotation(slices[i].Normal, Vector3.up), 1.0f, EventType.Repaint);
        }

        private static bool IsWithinBrush(Vector3 pos, Vector3 center, float radius, BrushShape shape)
        {
            float dx = pos.x - center.x; float dz = pos.z - center.z;
            if (shape == BrushShape.Circle) return (dx * dx + dz * dz) <= radius * radius;
            return Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius;
        }
    }
}

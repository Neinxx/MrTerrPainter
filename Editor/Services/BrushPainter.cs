using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Utils;
using MrTerrainPainter.Runtime.Core;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using MrTerrainPainter.Editor.Services;

namespace MrTerrainPainter.Editor.Services
{
    public enum BrushShape { Circle, Square }
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

    public static class BrushPainter
    {
        private static bool s_configCompleteCached;
        static BrushPainter()
        {
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            s_configCompleteCached = cfg != null && MrTerrainPainter.Editor.Config.ConfigTools.IsComplete(cfg, out _);
            MrTerrainPainter.Editor.Config.ConfigTools.CompletenessChanged += v => { s_configCompleteCached = v; };
        }
        private class Grid
        {
            private readonly float cellSize;
            private readonly Dictionary<(int, int), List<Vector2>> cells = new();
            public Grid(float spacing)
            {
                cellSize = Mathf.Max(spacing, 0.01f);
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
        public static void DrawPreview(Vector3 center, BrushSettings bs)
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
        }

        public static void DrawPreview(Vector3 center, Vector3 normal, BrushSettings bs)
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
            var raisedCenter = center + normal.normalized * 0.02f;
            if (bs.shape == BrushShape.Circle)
            {
                Handles.color = fill;
                Handles.DrawSolidDisc(raisedCenter, normal, bs.size);
                Handles.color = ring;
                const int segments = 64;
                var pts = new Vector3[segments + 1];
                var tangent = Vector3.Normalize(Vector3.Cross(normal, Vector3.right));
                if (tangent == Vector3.zero) tangent = Vector3.Normalize(Vector3.Cross(normal, Vector3.forward));
                var bitangent = Vector3.Normalize(Vector3.Cross(normal, tangent));
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
            var cfg1 = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            if (cfg1 != null && cfg1.normalDirection)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.9f);
                var tip = raisedCenter + normal.normalized * (bs.size * 0.6f);
                Handles.DrawAAPolyLine(6f, new Vector3[] { raisedCenter, tip });
            }
            var fwd = normal.normalized;
            var upProj = Vector3.ProjectOnPlane(Vector3.up, fwd);
            if (upProj.sqrMagnitude > 1e-6f)
            {
                var upTip = raisedCenter + upProj.normalized * (bs.size * 0.4f);
                Handles.color = new Color(0.6f, 1f, 0.6f, 0.9f);
                Handles.DrawAAPolyLine(4f, new Vector3[] { raisedCenter, upTip });
            }
        }

        public static void Paint(Terrain terrain, VegetationProfile profile, Vector3 center, BrushSettings bs, System.Random rnd, VegetationGenerator.PlacementOverrides? ov = null)
        {
            if (terrain == null || profile == null || profile.IsEmpty()) return; // 提前返回
            var td = terrain.terrainData;
            if (td == null) return; // 提前返回

            float radius = bs.size;
            var typeToNode = VegetationGenerator.BuildTypeToNodeMapping();

            var items = profile.Items;
            for (int it = 0; it < items.Count; it++)
            {
                var item = items[it];
                if (item == null || !item.IsValid()) continue;
                int count = Mathf.RoundToInt(item.baseDensity * bs.densityScale * bs.strength * 10f);
                count = Mathf.Clamp(count, 0, 500);
                if (count <= 0) continue;

                float spacing = Mathf.Max(item.minSpacing, 0.01f);
                float jitter = Mathf.Max(bs.minSpacingJitter, 0f);
                int seed = bs.strokeSeed != 0 ? bs.strokeSeed : profile.randomSeed;
                var centerXZ = new Vector2(center.x, center.z);
                int desired = VegetationGenerator.ComputeDesiredCandidateCount(bs.shape, radius, spacing, Mathf.Min(count, bs.maxPoints), bs.maxPoints);
                List<Vector2> candidates = null;
                FacadeDetectionService.FacadeInfo facadeInfo = default;
                bool useFacade = (bs.distribution == DistributionType.EdgeLine && item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape);
                if (useFacade)
                {
                    if (!FacadeDetectionService.TryDetectFacade(terrain, center, item.edgeSlopeEnter, item.edgeSlopeExit, item.probeStep, item.probeMaxDist, out facadeInfo))
                    {
                        continue;
                    }
                    candidates = BuildFacadeStripCandidatesFromInfo(facadeInfo, bs, item);
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
                var grid = new Grid(spacing);
                int placed = 0;
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
                        if (!TerrainUtils.TryGetHeightAndNormal(terrain, p, out h, out n)) continue;
                        p.y = h;
                        slope = TerrainUtils.ComputeSlope(n);
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
                        CreateFacadeInstance(item, p, n, terrain, it, targetParent, rnd, facadeInfo, ovScaled);
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
                        CreateInstance(item, p, n, terrain, it, targetParent, rnd, ovScaled);
                    }
                    placed++;
                }
                BrushEngine.ReleaseList(candidates);
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
            FacadeDetectionService.FacadeInfo facadeInfo = default;
            bool useFacade = bs.distribution == DistributionType.EdgeLine && allItems.Any(it => it.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape);
            if (useFacade)
            {
                var itemRef = allItems.First(it => it.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape);
                if (!FacadeDetectionService.TryDetectFacade(terrain, center, itemRef.edgeSlopeEnter, itemRef.edgeSlopeExit, itemRef.probeStep, itemRef.probeMaxDist, out facadeInfo))
                {
                    return;
                }
                candidates = BuildFacadeStripCandidatesFromInfo(facadeInfo, bs, itemRef);
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
            var itemGrids = new Dictionary<int, Grid>();
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
                    p.y = facadeInfo.bottomPos.y;
                    n = facadeInfo.forward.normalized;
                    slope = 90f;
                }
                else
                {
                    if (!TerrainUtils.TryGetHeightAndNormal(terrain, p, out h, out n)) continue;
                    p.y = h;
                    slope = TerrainUtils.ComputeSlope(n);
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
                    if (!itemGrids.TryGetValue(idx, out var grid))
                    {
                        grid = new Grid(Mathf.Max(item.minSpacing, 0.01f));
                        itemGrids[idx] = grid;
                    }
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
                        CreateFacadeInstance(item, p, n, terrain, idx, targetParent, rnd, facadeInfo, ovScaled);
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
                        CreateInstance(item, p, n, terrain, idx, targetParent, rnd, ovScaled);
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

        private static void CreateInstance(VegetationItem item, Vector3 pos, Vector3 normal, Terrain terrain, int itemIndex, Transform parent, System.Random rnd, VegetationGenerator.PlacementOverrides? ov)
        {
            if (item.prefab == null) return; // 提前返回
            // 优先复用对象池，减少实例化与GC
            var go = VegetationPool.Get(terrain, item, itemIndex, parent, "Paint Vegetation Instance");
            if (go == null) return; // 提前返回
            go.transform.position = pos;
            // 强制使用条目级缩放范围，确保Profile设置生效
            float scale = item.SampleScale(rnd);
            go.transform.localScale = Vector3.one * scale;
            // 强制使用条目级Y旋转范围
            float yRot = item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape ? 0f : item.SampleYRotation(rnd);
            var rot = Quaternion.Euler(0f, yRot, 0f);
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
            bool useNormal = cfg != null ? (cfg.normalDirection || item.alignToTerrainNormal || item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape) : (item.alignToTerrainNormal || item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape);
            if (item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape)
            {
                var forward = normal.normalized;
                var upOnPlane = Vector3.ProjectOnPlane(Vector3.up, forward);
                if (upOnPlane.sqrMagnitude < 1e-6f) upOnPlane = Vector3.Cross(forward, Vector3.right).normalized;
                var baseRot = Quaternion.LookRotation(forward, upOnPlane);
                rot = Quaternion.AngleAxis(yRot, forward) * baseRot;
            }
            else if (useNormal)
            {
                rot = Quaternion.LookRotation(Vector3.Cross(Vector3.right, normal), normal) * Quaternion.Euler(0f, yRot, 0f);
            }
            go.transform.rotation = rot;
            if (item.prefabType == MrTerrainPainter.Runtime.Profiles.PrefabType.Landscape && item.edgeAutoHeight)
            {
                var up = Vector3.up;
                var forward = Vector3.ProjectOnPlane(normal, up);
                if (forward.sqrMagnitude > 1e-6f)
                {
                    forward.Normalize();
                    float hFoot = go.transform.position.y;
                    float heightMeters = 0f;
                    float step = Mathf.Max(item.edgeLookAheadStep, 0.05f);
                    float maxD = Mathf.Max(item.edgeMaxLookAhead, step);
                    for (float d = step; d <= maxD + 0.0001f; d += step)
                    {
                        var test = go.transform.position + (-forward) * d;
                        if (TerrainUtils.TryGetHeightAndNormal(terrain, test, out float hTop, out Vector3 nTop))
                        {
                            float sTop = TerrainUtils.ComputeSlope(nTop);
                            if (sTop < Mathf.Clamp(item.edgeSlopeThreshold, 0f, 90f)) { heightMeters = Mathf.Max(0f, hTop - hFoot); break; }
                        }
                    }
                    float baseScale = go.transform.localScale.x;
                    float yScale = baseScale;
                    if (heightMeters > 0f)
                    {
                        yScale = heightMeters / Mathf.Max(item.edgeReferenceHeightMeters, 0.0001f);
                    }
                    go.transform.localScale = new Vector3(baseScale, yScale, baseScale);
                    var right = Vector3.Cross(up, forward).normalized;
                    var horizFwd = forward; // 已在水平面
                    var off = right * item.edgeOffsets.x + up * item.edgeOffsets.y + (-horizFwd) * item.edgeOffsets.z;
                    go.transform.position += off;
                }
            }
            var vi = go.GetComponent<VegetationInstance>();
            if (vi == null) vi = go.AddComponent<VegetationInstance>();
            vi.sourceTerrain = terrain;
            vi.profileItemIndex = itemIndex;
            vi.sourcePrefabName = item.prefab.name;
            VegetationPool.IndexRegister(terrain, go);
        }

        private static void CreateFacadeInstance(VegetationItem item, Vector3 pos, Vector3 forward, Terrain terrain, int itemIndex, Transform parent, System.Random rnd, FacadeDetectionService.FacadeInfo info, VegetationGenerator.PlacementOverrides? ov)
        {
            if (item.prefab == null) return;
            var go = VegetationPool.Get(terrain, item, itemIndex, parent, "Paint Vegetation Instance");
            if (go == null) return;
            go.transform.position = pos;

            float baseScale = item.SampleScale(rnd);
            go.transform.localScale = Vector3.one * baseScale;

            float yRot = 0f;
            var upOnPlane = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (upOnPlane.sqrMagnitude < 1e-6f) upOnPlane = Vector3.Cross(forward, Vector3.right).normalized;
            var rot = Quaternion.LookRotation(forward, upOnPlane);
            go.transform.rotation = rot;

            if (item.edgeAutoHeight)
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
    }
}

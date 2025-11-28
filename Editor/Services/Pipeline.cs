using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Utils;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace MrTerrainPainter.Editor.Services
{
    /// <summary>
    /// Pipeline执行上下文 - 封装地形和物品相关参数
    /// </summary>
    public struct PipelineContext
    {
        public Terrain Terrain;
        public Vector3 Center;
        public float Radius;
        public MrTerrainPainter.Runtime.Profiles.VegetationItem Item;
        public int ItemIndex;
        public Transform Parent;
    }

    /// <summary>
    /// Pipeline数据 - 封装候选点和采样结果
    /// </summary>
    public struct PipelineData
    {
        public List<Vector3> Candidates;
        public NativeArray<float> Heights;
        public NativeArray<float> Slopes;
        public NativeArray<float3> Normals;
    }

    public interface IPointSampler
    {
        List<Vector3> Sample(Vector3 center, float radius);
    }

    public interface ICandidateFilter
    {
        bool Pass(int index, Vector3 worldPos, float heightLocal, float slopeDeg);
    }

    public interface IInstanceMutator
    {
        void Mutate(MrTerrainPainter.Runtime.Profiles.VegetationItem item, System.Random rnd, ref Vector3 pos, ref Quaternion rot, ref Vector3 scale, Vector3 normal);
    }

    public interface IInstanceSpawner
    {
        void Spawn(MrTerrainPainter.Runtime.Profiles.VegetationItem item, int itemIndex, Transform parent, Terrain terrain, Vector3 pos, Quaternion rot, Vector3 scale);
    }

    public class CandidateSamplerFromList : IPointSampler
    {
        private readonly List<Vector2> _list;
        private readonly float _y;
        public CandidateSamplerFromList(List<Vector2> list, float y) { _list = list; _y = y; }
        public List<Vector3> Sample(Vector3 center, float radius)
        {
            var res = new List<Vector3>(_list.Count);
            for (int i = 0; i < _list.Count; i++) res.Add(new Vector3(_list[i].x, _y, _list[i].y));
            return res;
        }
    }

    public class HeightSlopeFilter : ICandidateFilter
    {
        private readonly MrTerrainPainter.Runtime.Profiles.VegetationItem _item;
        public HeightSlopeFilter(MrTerrainPainter.Runtime.Profiles.VegetationItem item) { _item = item; }
        public bool Pass(int index, Vector3 worldPos, float heightLocal, float slopeDeg)
        {
            float h = heightLocal;
            if (h < _item.heightRange.x || h > _item.heightRange.y) return false;
            if (slopeDeg < _item.slopeRange.x || slopeDeg > _item.slopeRange.y) return false;
            return true;
        }
    }

    public class FacadeConstraintFilter : ICandidateFilter
    {
        private readonly float _minHeight;
        public FacadeConstraintFilter(float minHeightMeters) { _minHeight = Mathf.Max(0.0001f, minHeightMeters); }
        public bool Pass(int index, Vector3 worldPos, float heightLocal, float slopeDeg)
        {
            return heightLocal >= _minHeight;
        }
    }

    public class CurvatureFilter : ICandidateFilter
    {
        private readonly List<FacadeDetectionService.CliffSlice> _slices;
        private readonly float _maxKappa;
        public CurvatureFilter(List<FacadeDetectionService.CliffSlice> slices, float maxKappa)
        {
            _slices = slices; _maxKappa = Mathf.Max(0.0001f, maxKappa);
        }
        public bool Pass(int index, Vector3 worldPos, float heightLocal, float slopeDeg)
        {
            if (_slices == null || _slices.Count < 3) return true;
            int nearest = 0; float best = float.MaxValue;
            for (int i = 0; i < _slices.Count; i++)
            {
                float d = Vector3.SqrMagnitude(_slices[i].BottomPosition - worldPos);
                if (d < best) { best = d; nearest = i; }
            }
            var i0 = Mathf.Max(0, nearest - 1);
            var i1 = nearest;
            var i2 = Mathf.Min(_slices.Count - 1, nearest + 1);
            var p0 = _slices[i0].BottomPosition; p0.y = 0f;
            var p1 = _slices[i1].BottomPosition; p1.y = 0f;
            var p2 = _slices[i2].BottomPosition; p2.y = 0f;
            var a = (p1 - p0); var b = (p2 - p1);
            float ang = (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f) ? Vector3.Angle(a.normalized, b.normalized) * Mathf.Deg2Rad : 0f;
            float ds = Mathf.Max(0.0001f, (a.magnitude + b.magnitude) * 0.5f);
            float kappa = ang / ds;
            return kappa <= _maxKappa;
        }
    }

    public class EdgeLineSampler : IPointSampler
    {
        private readonly List<FacadeDetectionService.CliffSlice> _slices;
        private readonly float _spacing;
        private readonly Vector3 _center;
        private readonly BrushShape _shape;
        public EdgeLineSampler(List<FacadeDetectionService.CliffSlice> slices, float spacing, Vector3 center, BrushShape shape)
        {
            _slices = slices; _spacing = Mathf.Max(spacing, 0.01f); _center = center; _shape = shape;
        }
        private static bool IsWithinBrush(Vector3 p, Vector3 c, float r, BrushShape s)
        {
            float dx = p.x - c.x; float dz = p.z - c.z;
            if (s == BrushShape.Circle) return (dx * dx + dz * dz) <= r * r;
            return Mathf.Abs(dx) <= r && Mathf.Abs(dz) <= r;
        }
        public List<Vector3> Sample(Vector3 center, float radius)
        {
            var res = new List<Vector3>();
            var grid = new BrushSpatialGrid(_spacing);
            for (int i = 0; i < _slices.Count; i++)
            {
                var s = _slices[i];
                var p = s.BottomPosition;
                if (!IsWithinBrush(p, _center, radius, _shape)) continue;
                var p2 = new Vector2(p.x, p.z);
                if (grid.HasNearby(p2, _spacing)) continue;
                grid.Add(p2);
                res.Add(p);
            }
            return res;
        }
    }

    /// <summary>
    /// 基于弧长的边线采样器：沿切片构成的折线按指定间距重采样，获得更均匀的候选点
    /// </summary>
    public class ArcLengthEdgeSampler : IPointSampler
    {
        private readonly List<FacadeDetectionService.CliffSlice> _slices;
        private readonly float _spacing;
        private readonly Vector3 _center;
        private readonly BrushShape _shape;
        public ArcLengthEdgeSampler(List<FacadeDetectionService.CliffSlice> slices, float spacing, Vector3 center, BrushShape shape)
        {
            _slices = slices; _spacing = Mathf.Max(spacing, 0.01f); _center = center; _shape = shape;
        }
        private static bool IsWithinBrush(Vector3 p, Vector3 c, float r, BrushShape s)
        {
            float dx = p.x - c.x; float dz = p.z - c.z;
            if (s == BrushShape.Circle) return (dx * dx + dz * dz) <= r * r;
            return Mathf.Abs(dx) <= r && Mathf.Abs(dz) <= r;
        }
        public List<Vector3> Sample(Vector3 center, float radius)
        {
            var res = new List<Vector3>();
            if (_slices == null || _slices.Count == 0) return res;

            // 构造折线序列（假设 slices 已按路径顺序）
            var path = new List<Vector3>(_slices.Count);
            for (int i = 0; i < _slices.Count; i++)
            {
                var p = _slices[i].BottomPosition;
                if (IsWithinBrush(p, _center, radius, _shape)) path.Add(p);
            }
            if (path.Count == 0) return res;

            // 按弧长逐步采样
            float accum = 0f;
            Vector3 last = path[0];
            res.Add(last);
            for (int i = 1; i < path.Count; i++)
            {
                var cur = path[i];
                float seg = Vector3.Distance(last, cur);
                if (seg <= 1e-5f) continue;
                accum += seg;
                while (accum >= _spacing)
                {
                    float t = (seg <= 0f) ? 0f : Mathf.Clamp01((_spacing - (accum - seg)) / seg);
                    var p = Vector3.Lerp(last, cur, t);
                    res.Add(p);
                    accum -= _spacing;
                    last = p;
                    seg = Vector3.Distance(last, cur);
                    if (seg <= 1e-5f) break;
                }
                if (seg > 1e-5f) last = cur;
            }
            return res;
        }
    }

    public class StandardMutator : IInstanceMutator
    {
        private readonly MrTerrainPainter.Runtime.Profiles.VegetationItem _item;
        public StandardMutator(MrTerrainPainter.Runtime.Profiles.VegetationItem item) { _item = item; }
        public void Mutate(MrTerrainPainter.Runtime.Profiles.VegetationItem item, System.Random rnd, ref Vector3 pos, ref Quaternion rot, ref Vector3 scale, Vector3 normal)
        {
            float s = item.SampleScale(rnd);
            scale = new Vector3(s, s, s);
            float y = item.SampleYRotation(rnd);
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            bool alignNormal = cfg != null && cfg.normalDirection;
            rot = alignNormal ? Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, normal), normal) * Quaternion.Euler(0f, y, 0f) : Quaternion.Euler(0f, y, 0f);
        }
    }

    /// <summary>
    /// 封边石/立面实例变换器 - 处理EdgeLine分布模式的旋转和缩放
    /// [旧版本] 使用最小高度，不使用实际立面高度（已废弃，保留用于兼容）
    /// </summary>
    public class EdgeLineMutator : IInstanceMutator
    {
        public void Mutate(MrTerrainPainter.Runtime.Profiles.VegetationItem item, System.Random rnd, ref Vector3 pos, ref Quaternion rot, ref Vector3 scale, Vector3 normal)
        {
            // 1. 缩放计算 - 基于立面高度
            float rendererH = PrefabMetricsCache.GetPrefabHeightMeters(item.prefab);
            float minH = 0.0001f;
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            if (cfg != null) minH = Mathf.Max(0.0001f, cfg.minFacadeHeightMeters);
            float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), 1f);
            var baseScale = new Vector3(uni, uni, uni);
            var finalScale = new Vector3(
                Mathf.Max(0.0001f, baseScale.x + item.facadeScaleOffset.x),
                Mathf.Max(0.0001f, baseScale.y + item.facadeScaleOffset.y),
                Mathf.Max(0.0001f, baseScale.z + item.facadeScaleOffset.z));
            scale = finalScale;

            // 2. 旋转计算 - 封边石始终朝向立面法线方向（朝外）
            // 不受 normalDirection 配置影响，因为封边石必须垂直于墙面
            var up = Vector3.up;
            rot = Quaternion.LookRotation(normal, up);
            // 旋转含义：
            //   - Forward (+Z轴) → 立面法线方向（朝外）
            //   - Up (+Y轴) → 垂直向上
            //   - Right (+X轴) → 沿墙面切线方向（自动计算）

            // 3. 嵌入深度偏移 - 让封边石嵌入墙面
            float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
            var horiz = Vector3.ProjectOnPlane(-normal.normalized, Vector3.up);
            if (horiz.sqrMagnitude > 1e-6f)
            {
                horiz.Normalize();
                var off = horiz * depth;
                pos = new Vector3(pos.x + off.x, pos.y, pos.z + off.z);
            }
        }
    }

    /// <summary>
    /// 立面封边石变换器 - 使用实际立面高度完美适配
    /// </summary>
    public class FacadeLineMutator : IInstanceMutator
    {
        private readonly System.Collections.Generic.List<FacadeDetectionService.CliffSlice> slices;
        private readonly System.Collections.Generic.List<Vector3> candidates;

        public FacadeLineMutator(System.Collections.Generic.List<FacadeDetectionService.CliffSlice> slices, System.Collections.Generic.List<Vector3> candidates)
        {
            this.slices = slices;
            this.candidates = candidates;
        }

        public void Mutate(MrTerrainPainter.Runtime.Profiles.VegetationItem item, System.Random rnd, ref Vector3 pos, ref Quaternion rot, ref Vector3 scale, Vector3 normal)
        {
            // 1. 查找当前候选点对应的CliffSlice
            FacadeDetectionService.CliffSlice slice = default;
            float minDistSq = float.MaxValue;
            for (int i = 0; i < slices.Count; i++)
            {
                float distSq = Vector3.SqrMagnitude(slices[i].BottomPosition - pos);
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    slice = slices[i];
                }
            }

            // 2. 缩放计算 - 基于实际立面高度（slice.Height）
            float rendererH = PrefabMetricsCache.GetPrefabHeightMeters(item.prefab);
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            float minH = cfg != null ? Mathf.Max(0.0001f, cfg.minFacadeHeightMeters) : 0.0001f;

            // [关键] 使用 slice.Height（实际立面高度）计算缩放
            // uni = max(minH/rendererH, sliceHeight/rendererH)
            // 这确保封边石至少达到最小高度，但优先使用实际立面高度
            float sliceHeight = slice.Height;
            float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), sliceHeight / Mathf.Max(0.0001f, rendererH));

            var baseScale = new Vector3(uni, uni, uni);
            var finalScale = new Vector3(
                Mathf.Max(0.0001f, baseScale.x + item.facadeScaleOffset.x),
                Mathf.Max(0.0001f, baseScale.y + item.facadeScaleOffset.y),
                Mathf.Max(0.0001f, baseScale.z + item.facadeScaleOffset.z));
            scale = finalScale;

            // 3. 旋转计算 - 封边石的 +Z 方向朝向立面法线
            var up = Vector3.up;
            rot = Quaternion.LookRotation(slice.Normal, up);
            // 额外绕法线的扭转角，来源于 yRotationRange，用于造型差异
            float twistDeg = item.SampleYRotation(rnd);
            rot = Quaternion.AngleAxis(twistDeg, slice.Normal) * rot;
            // 旋转轴向：
            //   - Forward (+Z) → slice.Normal（立面法线，朝外）
            //   - Up (+Y) → Vector3.up（垂直向上）
            //   - Right (+X) → 沿墙面切线方向

            // 4. 位置偏移 - 应用 offsets 和嵌入深度
            float depth = Mathf.Clamp(item.SampleEmbedDepth(rnd), 0f, 1f);
            var rightAxis = Vector3.Normalize(Vector3.Cross(slice.Direction, slice.Normal));

            // offsets: (x=右, y=上, z=前)，相对于立面坐标系
            var off = rightAxis * item.offsets.x
                    + slice.Direction * item.offsets.y
                    + (-slice.Normal.normalized) * (depth + Mathf.Max(0f, item.offsets.z));

            pos += off;
        }
    }

    public class PooledSpawner : IInstanceSpawner
    {
        public void Spawn(MrTerrainPainter.Runtime.Profiles.VegetationItem item, int itemIndex, Transform parent, Terrain terrain, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = VegetationPool.Get(terrain, item, itemIndex, parent, "Create Vegetation Instance");
            if (go == null) return;
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = scale;
            var vi = go.GetComponent<Runtime.Core.VegetationInstance>() ?? go.AddComponent<Runtime.Core.VegetationInstance>();
            vi.sourceTerrain = terrain;
            vi.profileItemIndex = itemIndex;
            vi.sourcePrefabName = item.prefab != null ? item.prefab.name : "";
            VegetationPool.IndexRegister(terrain, go);
        }
    }

    /// <summary>
    /// 立面对齐实例化器：确保底部对齐切片底部、顶部对齐切片顶部，并保持Z轴对齐立面法线
    /// </summary>
    public class FacadeAligningSpawner : IInstanceSpawner
    {
        private readonly System.Collections.Generic.List<FacadeDetectionService.CliffSlice> _slices;
        public FacadeAligningSpawner(System.Collections.Generic.List<FacadeDetectionService.CliffSlice> slices) { _slices = slices; }
        public void Spawn(MrTerrainPainter.Runtime.Profiles.VegetationItem item, int itemIndex, Transform parent, Terrain terrain, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = VegetationPool.Get(terrain, item, itemIndex, parent, "Create Vegetation Instance");
            if (go == null) return;
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = scale;

            // 查找最近的切片（按底部位置），以获取目标高度与法线
            FacadeDetectionService.CliffSlice slice = default;
            float best = float.MaxValue;
            if (_slices != null && _slices.Count > 0)
            {
                for (int i = 0; i < _slices.Count; i++)
                {
                    float d = Vector3.SqrMagnitude(_slices[i].BottomPosition - pos);
                    if (d < best) { best = d; slice = _slices[i]; }
                }
            }

            // 实测当前高度并统一缩放到目标高度
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                float curH = Mathf.Max(0.0001f, b.size.y);
                float targetH = Mathf.Max(0.0001f, slice.TopPosition.y - slice.BottomPosition.y);
                float factor = targetH / curH;
                var s = go.transform.localScale;
                go.transform.localScale = new Vector3(s.x * factor, s.y * factor, s.z * factor);

                // 重新测量并将底部对齐到切片底部高度
                var b2 = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b2.Encapsulate(renderers[i].bounds);
                float deltaBottom = slice.BottomPosition.y - b2.min.y;
                if (Mathf.Abs(deltaBottom) > 1e-5f)
                {
                    var p = go.transform.position;
                    go.transform.position = new Vector3(p.x, p.y + deltaBottom, p.z);
                }
            }

            var vi = go.GetComponent<Runtime.Core.VegetationInstance>() ?? go.AddComponent<Runtime.Core.VegetationInstance>();
            vi.sourceTerrain = terrain;
            vi.profileItemIndex = itemIndex;
            vi.sourcePrefabName = item.prefab != null ? item.prefab.name : "";
            VegetationPool.IndexRegister(terrain, go);
        }
    }

    public class GlobalGridSpawner : IInstanceSpawner
    {
        private readonly BrushSpatialGrid _grid;
        private readonly float _spacing;
        private readonly IInstanceSpawner _inner;
        public GlobalGridSpawner(BrushSpatialGrid grid, float spacing, IInstanceSpawner inner) { _grid = grid; _spacing = Mathf.Max(spacing, 0.01f); _inner = inner; }
        public void Spawn(MrTerrainPainter.Runtime.Profiles.VegetationItem item, int itemIndex, Transform parent, Terrain terrain, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var p2 = new Vector2(pos.x - terrain.transform.position.x, pos.z - terrain.transform.position.z);
            if (_grid != null && _grid.HasNearby(p2, _spacing)) return;
            _inner?.Spawn(item, itemIndex, parent, terrain, pos, rot, scale);
            _grid?.Add(p2);
        }
    }

    public class VegetationPipeline
    {
        private IPointSampler _sampler;
        private ICandidateFilter _filter;
        private IInstanceMutator _mutator;
        private IInstanceSpawner _spawner;
        public static readonly VegetationPipeline Shared = new VegetationPipeline();
        public VegetationPipeline Setup(IPointSampler s, ICandidateFilter f, IInstanceMutator m, IInstanceSpawner p) { _sampler = s; _filter = f; _mutator = m; _spawner = p; return this; }

        /// <summary>
        /// 执行植被生成管线（优化版：从10个参数简化为2个）
        /// </summary>
        public void Run(PipelineContext context, PipelineData data)
        {
            var grid = new BrushSpatialGrid(Mathf.Max(Mathf.Max(context.Item.CoreSpacing, context.Item.CoreMinRadius), 0.01f));
            for (int ci = 0; ci < data.Candidates.Count; ci++)
            {
                var pos = data.Candidates[ci];
                if (!TerrainUtils.IsWithinTerrainBounds(context.Terrain, pos)) continue;
                float h = data.Heights.IsCreated && ci < data.Heights.Length ? data.Heights[ci] + context.Terrain.transform.position.y : pos.y;
                float slope = data.Slopes.IsCreated && ci < data.Slopes.Length ? data.Slopes[ci] : 0f;
                var normal = data.Normals.IsCreated && ci < data.Normals.Length ? (Vector3)data.Normals[ci] : Vector3.up;
                if (!_filter.Pass(ci, pos, h - context.Terrain.transform.position.y, slope)) continue;
                var p2 = new Vector2(pos.x - context.Terrain.transform.position.x, pos.z - context.Terrain.transform.position.z);
                if (grid.HasNearby(p2, Mathf.Max(context.Item.CoreSpacing, 0.01f))) continue;
                pos.y = h;
                var rot = Quaternion.identity;
                var scale = Vector3.one;
                var rnd = new System.Random((int)(pos.x * 13 + pos.z * 7));
                _mutator.Mutate(context.Item, rnd, ref pos, ref rot, ref scale, normal);
                _spawner.Spawn(context.Item, context.ItemIndex, context.Parent, context.Terrain, pos, rot, scale);
                grid.Add(p2);
            }
        }
    }
}

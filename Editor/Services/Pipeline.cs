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

    public class EdgeLineMutator : IInstanceMutator
    {
        public void Mutate(MrTerrainPainter.Runtime.Profiles.VegetationItem item, System.Random rnd, ref Vector3 pos, ref Quaternion rot, ref Vector3 scale, Vector3 normal)
        {
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
            bool alignNormal = cfg != null && cfg.normalDirection;
            var up = Vector3.up;
            rot = alignNormal ? Quaternion.LookRotation(normal, up) : Quaternion.LookRotation(Vector3.forward, up);
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

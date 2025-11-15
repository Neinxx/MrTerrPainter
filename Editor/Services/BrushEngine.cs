using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public enum DistributionType { Uniform, PoissonDisk, Cluster, JitteredGrid }

    public struct ClusterSettings
    {
        public int clusterCount;
        public int childPerCluster;
        public float clusterRadius;
        public float childJitter;
    }

    public static class BrushEngine
    {
        private struct SampleKey
        {
            public BrushShape shape;
            public DistributionType dist;
            public float radius;
            public int count;
            public float spacing;
            public float jitter;
            public int seed;
            public int cCount;
            public int cChild;
            public float cRadius;
            public float cJitter;
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = (int)shape * 397 ^ (int)dist;
                    h = h * 397 ^ radius.GetHashCode();
                    h = h * 397 ^ count;
                    h = h * 397 ^ spacing.GetHashCode();
                    h = h * 397 ^ jitter.GetHashCode();
                    h = h * 397 ^ seed;
                    h = h * 397 ^ cCount;
                    h = h * 397 ^ cChild;
                    h = h * 397 ^ cRadius.GetHashCode();
                    h = h * 397 ^ cJitter.GetHashCode();
                    return h;
                }
            }
        }
        private static readonly System.Collections.Generic.Dictionary<SampleKey, System.Collections.Generic.List<Vector2>> cache = new System.Collections.Generic.Dictionary<SampleKey, System.Collections.Generic.List<Vector2>>();
        private static System.Collections.Generic.List<Vector2> Translate(System.Collections.Generic.List<Vector2> src, Vector2 center)
        {
            var list = new System.Collections.Generic.List<Vector2>(src.Count);
            for (int i = 0; i < src.Count; i++) list.Add(src[i] + center);
            return list;
        }
        public static System.Collections.Generic.List<Vector2> SampleCached(Vector2 center, BrushShape shape, DistributionType dist, float radius, int count, float spacing, float jitter, int seed, ClusterSettings cs)
        {
            var key = new SampleKey
            {
                shape = shape,
                dist = dist,
                radius = radius,
                count = count,
                spacing = spacing,
                jitter = jitter,
                seed = seed,
                cCount = cs.clusterCount,
                cChild = cs.childPerCluster,
                cRadius = cs.clusterRadius,
                cJitter = cs.childJitter
            };
            if (cache.TryGetValue(key, out var off)) return Translate(off, center);
            System.Collections.Generic.List<Vector2> abs;
            switch (dist)
            {
                case DistributionType.PoissonDisk:
                    abs = SamplePoisson(Vector2.zero, radius, shape, count, spacing, jitter, seed);
                    break;
                case DistributionType.Cluster:
                    abs = SampleCluster(Vector2.zero, radius, shape, cs, spacing, seed);
                    break;
                case DistributionType.JitteredGrid:
                    abs = SampleJittered(Vector2.zero, radius, shape, spacing, jitter, new System.Random(seed));
                    break;
                default:
                    abs = SampleUniform(Vector2.zero, radius, shape, count, new System.Random(seed));
                    break;
            }
            cache[key] = abs;
            return Translate(abs, center);
        }
        private static bool InsideShape(Vector2 p, Vector2 center, float radius, BrushShape shape)
        {
            if (shape == BrushShape.Circle)
            {
                return Vector2.SqrMagnitude(p - center) <= radius * radius;
            }
            return Mathf.Abs(p.x - center.x) <= radius && Mathf.Abs(p.y - center.y) <= radius;
        }

        public static List<Vector2> SampleUniform(Vector2 center, float radius, BrushShape shape, int count, System.Random rnd)
        {
            var list = new List<Vector2>(Mathf.Max(count, 0));
            for (int i = 0; i < count; i++)
            {
                if (shape == BrushShape.Circle)
                {
                    float r = Mathf.Sqrt((float)rnd.NextDouble()) * radius;
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    list.Add(center + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
                }
                else
                {
                    float x = (float)rnd.NextDouble() * 2f - 1f;
                    float y = (float)rnd.NextDouble() * 2f - 1f;
                    list.Add(center + new Vector2(x * radius, y * radius));
                }
            }
            return list;
        }

        public static List<Vector2> SamplePoisson(Vector2 center, float radius, BrushShape shape, int desiredCount, float minSpacing, float jitter, int seed)
        {
            var list = new List<Vector2>();
            float r = Mathf.Max(minSpacing, 0.0001f);
            float cell = r / Mathf.Sqrt(2f);
            var grid = new Dictionary<(int,int), List<Vector2>>();
            var active = new List<Vector2>();
            var rnd = new System.Random(seed);
            Vector2 first = center;
            float fr = (float)rnd.NextDouble() * radius * 0.5f;
            float fa = (float)rnd.NextDouble() * Mathf.PI * 2f;
            if (shape == BrushShape.Circle) first = center + new Vector2(Mathf.Cos(fa) * fr, Mathf.Sin(fa) * fr);
            AddPoint(first, cell, grid, active, list);
            int k = 30;
            int guard = Mathf.Max(desiredCount, 1) * k;
            while (active.Count > 0 && list.Count < desiredCount && guard-- > 0)
            {
                int ai = rnd.Next(0, active.Count);
                var baseP = active[ai];
                bool found = false;
                for (int it = 0; it < k; it++)
                {
                    float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float rad = r * (1f + (float)rnd.NextDouble());
                    float jr = r * jitter;
                    rad += ((float)rnd.NextDouble() * 2f - 1f) * jr;
                    var cand = baseP + new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
                    if (!InsideShape(cand, center, radius, shape)) continue;
                    if (ValidPoisson(cand, r, cell, grid))
                    {
                        AddPoint(cand, cell, grid, active, list);
                        found = true;
                        if (list.Count >= desiredCount) break;
                    }
                }
                if (!found)
                {
                    active.RemoveAt(ai);
                }
            }
            return list;
        }

        public static List<Vector2> SampleJittered(Vector2 center, float radius, BrushShape shape, float cellSize, float jitter, System.Random rnd)
        {
            var list = new List<Vector2>();
            int half = Mathf.CeilToInt(radius / cellSize);
            for (int gx = -half; gx <= half; gx++)
            {
                for (int gy = -half; gy <= half; gy++)
                {
                    var cellCenter = center + new Vector2(gx * cellSize, gy * cellSize);
                    float ox = ((float)rnd.NextDouble() * 2f - 1f) * cellSize * jitter;
                    float oy = ((float)rnd.NextDouble() * 2f - 1f) * cellSize * jitter;
                    var p = cellCenter + new Vector2(ox, oy);
                    if (!InsideShape(p, center, radius, shape)) continue;
                    list.Add(p);
                }
            }
            return list;
        }

        public static List<Vector2> SampleCluster(Vector2 center, float radius, BrushShape shape, ClusterSettings cs, float minSpacing, int seed)
        {
            var rnd = new System.Random(seed);
            var centers = SamplePoisson(center, radius, shape, Mathf.Max(cs.clusterCount, 1), Mathf.Max(minSpacing, 0.0001f), 0f, seed);
            var list = new List<Vector2>();
            for (int i = 0; i < centers.Count; i++)
            {
                var c = centers[i];
                for (int j = 0; j < Mathf.Max(cs.childPerCluster, 1); j++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float d = (float)rnd.NextDouble() * cs.clusterRadius;
                    d += ((float)rnd.NextDouble() * 2f - 1f) * cs.childJitter;
                    var p = c + new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
                    if (!InsideShape(p, center, radius, shape)) continue;
                    list.Add(p);
                }
            }
            return list;
        }

        private static void AddPoint(Vector2 p, float cell, Dictionary<(int,int), List<Vector2>> grid, List<Vector2> active, List<Vector2> list)
        {
            var k = Key(p, cell);
            if (!grid.TryGetValue(k, out var l)) { l = new List<Vector2>(); grid[k] = l; }
            l.Add(p);
            active.Add(p);
            list.Add(p);
        }

        private static (int,int) Key(Vector2 p, float cell)
        {
            return (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell));
        }

        private static bool ValidPoisson(Vector2 p, float minDist, float cell, Dictionary<(int,int), List<Vector2>> grid)
        {
            var k = Key(p, cell);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var nk = (k.Item1 + dx, k.Item2 + dy);
                if (!grid.TryGetValue(nk, out var l)) continue;
                for (int i = 0; i < l.Count; i++)
                {
                    if (Vector2.SqrMagnitude(l[i] - p) < minDist * minDist) return false;
                }
            }
            return true;
        }
    }
}

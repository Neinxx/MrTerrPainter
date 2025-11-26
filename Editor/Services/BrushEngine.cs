using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public enum DistributionType { Uniform, PoissonDisk, Cluster, JitteredGrid, Natural, AdaptivePoisson, Halton, EdgeLine }

    public struct ClusterSettings
    {
        public int clusterCount;
        public int childPerCluster;
        public float clusterRadius;
        public float childJitter;
    }

    public static class BrushEngine
    {
        private static readonly Stack<List<Vector2>> s_listPool = new();
        public static List<Vector2> AcquireList(int capacity)
        {
            if (s_listPool.Count > 0)
            {
                var l = s_listPool.Pop();
                l.Clear();
                if (l.Capacity < capacity) l.Capacity = capacity;
                return l;
            }
            return new System.Collections.Generic.List<Vector2>(capacity);
        }
        public static void ReleaseList(System.Collections.Generic.List<Vector2> list)
        {
            if (list == null) return;
            list.Clear();
            s_listPool.Push(list);
        }

        private static readonly Stack<List<Vector3>> s_listPool3 = new();
        public static List<Vector3> AcquireList3(int capacity)
        {
            if (s_listPool3.Count > 0)
            {
                var l = s_listPool3.Pop();
                l.Clear();
                if (l.Capacity < capacity) l.Capacity = capacity;
                return l;
            }
            return new System.Collections.Generic.List<Vector3>(capacity);
        }
        public static void ReleaseList3(System.Collections.Generic.List<Vector3> list)
        {
            if (list == null) return;
            list.Clear();
            s_listPool3.Push(list);
        }

#if UNITY_BURST
        [Unity.Burst.BurstCompile]
        private struct PoissonDiskJob : Unity.Jobs.IJob
        {
            public Unity.Mathematics.float2 Center;
            public float Radius;
            public BrushShape Shape;
            public int DesiredCount;
            public float MinSpacing;
            public float Jitter;
            public uint Seed;
            public Unity.Collections.NativeMultiHashMap<Unity.Mathematics.int2, Unity.Mathematics.float2> Grid;
            public Unity.Collections.NativeList<Unity.Mathematics.float2> Active;
            public Unity.Collections.NativeList<Unity.Mathematics.float2> Result;

            public void Execute()
            {
                float r = Unity.Mathematics.math.max(MinSpacing, 0.0001f);
                float cell = r / Unity.Mathematics.math.sqrt(2f);
                var rnd = new Unity.Mathematics.Random(Seed == 0 ? 1u : Seed);
                float fr = rnd.NextFloat(0f, Radius * 0.5f);
                float fa = rnd.NextFloat(0f, Unity.Mathematics.math.PI * 2f);
                Unity.Mathematics.float2 first = Center;
                if (Shape == BrushShape.Circle) first = Center + new Unity.Mathematics.float2(Unity.Mathematics.math.cos(fa) * fr, Unity.Mathematics.math.sin(fa) * fr);
                AddPoint(first, cell);
                int k = 30;
                int guard = Unity.Mathematics.math.max(DesiredCount, 1) * k;
                while (Active.Length > 0 && Result.Length < DesiredCount && guard-- > 0)
                {
                    int ai = rnd.NextInt(0, Active.Length);
                    var baseP = Active[ai];
                    bool found = false;
                    for (int it = 0; it < k; it++)
                    {
                        float ang = rnd.NextFloat(0f, Unity.Mathematics.math.PI * 2f);
                        float rad = r * (1f + rnd.NextFloat());
                        float jr = r * Jitter;
                        rad += rnd.NextFloat(-jr, jr);
                        var cand = baseP + new Unity.Mathematics.float2(Unity.Mathematics.math.cos(ang) * rad, Unity.Mathematics.math.sin(ang) * rad);
                        if (!InsideShape(cand)) continue;
                        if (ValidPoisson(cand, r, cell))
                        {
                            AddPoint(cand, cell);
                            found = true;
                            if (Result.Length >= DesiredCount) break;
                        }
                    }
                    if (!found)
                    {
                        Active.RemoveAt(ai);
                    }
                }
            }

            private bool InsideShape(Unity.Mathematics.float2 p)
            {
                if (Shape == BrushShape.Circle)
                {
                    return Unity.Mathematics.math.lengthsq(p - Center) <= Radius * Radius;
                }
                return Unity.Mathematics.math.abs(p.x - Center.x) <= Radius && Unity.Mathematics.math.abs(p.y - Center.y) <= Radius;
            }

            private Unity.Mathematics.int2 Key(Unity.Mathematics.float2 p, float cell)
            {
                return new Unity.Mathematics.int2((int)Unity.Mathematics.math.floor(p.x / cell), (int)Unity.Mathematics.math.floor(p.y / cell));
            }

            private bool ValidPoisson(Unity.Mathematics.float2 p, float minDist, float cell)
            {
                var k = Key(p, cell);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var nk = new Unity.Mathematics.int2(k.x + dx, k.y + dy);
                    Unity.Collections.NativeMultiHashMapIterator<Unity.Mathematics.int2> it;
                    Unity.Mathematics.float2 v;
                    if (Grid.TryGetFirstValue(nk, out v, out it))
                    {
                        do
                        {
                            if (Unity.Mathematics.math.lengthsq(v - p) < minDist * minDist) return false;
                        }
                        while (Grid.TryGetNextValue(out v, ref it));
                    }
                }
                return true;
            }

            private void AddPoint(Unity.Mathematics.float2 p, float cell)
            {
                var k = Key(p, cell);
                Grid.Add(k, p);
                Active.Add(p);
                Result.Add(p);
            }
        }

        public static System.Collections.Generic.List<Vector2> SamplePoissonBurst(Vector2 center, float radius, BrushShape shape, int desiredCount, float minSpacing, float jitter, int seed)
        {
            var res = new Unity.Collections.NativeList<Unity.Mathematics.float2>(Unity.Collections.Allocator.TempJob);
            var act = new Unity.Collections.NativeList<Unity.Mathematics.float2>(Unity.Collections.Allocator.TempJob);
            var grid = new Unity.Collections.NativeMultiHashMap<Unity.Mathematics.int2, Unity.Mathematics.float2>(Mathf.Max(desiredCount * 8, 16), Unity.Collections.Allocator.TempJob);
            var job = new PoissonDiskJob
            {
                Center = new Unity.Mathematics.float2(center.x, center.y),
                Radius = radius,
                Shape = shape,
                DesiredCount = Mathf.Max(desiredCount, 1),
                MinSpacing = Mathf.Max(minSpacing, 0.0001f),
                Jitter = jitter,
                Seed = (uint)(seed <= 0 ? 1 : seed),
                Grid = grid,
                Active = act,
                Result = res
            };
            job.Execute();
            var list = AcquireList(res.Length);
            for (int i = 0; i < res.Length; i++)
            {
                var f2 = res[i];
                list.Add(new Vector2(f2.x, f2.y));
            }
            grid.Dispose();
            act.Dispose();
            res.Dispose();
            return list;
        }
#else
        public static System.Collections.Generic.List<Vector2> SamplePoissonBurst(Vector2 center, float radius, BrushShape shape, int desiredCount, float minSpacing, float jitter, int seed)
        {
            return SamplePoisson(center, radius, shape, desiredCount, minSpacing, jitter, seed);
        }
#endif
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
            var list = AcquireList(Mathf.Max(count, 0));
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

        private static float Halton(int index, int b)
        {
            float f = 1f;
            float r = 0f;
            int i = Mathf.Max(index, 1);
            while (i > 0)
            {
                f /= b;
                r += f * (i % b);
                i /= b;
            }
            return r;
        }

        public static List<Vector2> SampleHaltonUniform(Vector2 center, float radius, BrushShape shape, int count, int seed)
        {
            var list = AcquireList(Mathf.Max(count, 0));
            int start = Mathf.Max(seed, 1);
            for (int i = 0; i < count; i++)
            {
                float u = Halton(start + i, 2);
                float v = Halton(start + i, 3);
                if (shape == BrushShape.Circle)
                {
                    float r = Mathf.Sqrt(u) * radius;
                    float a = v * Mathf.PI * 2f;
                    list.Add(center + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
                }
                else
                {
                    float x = (u * 2f - 1f) * radius;
                    float y = (v * 2f - 1f) * radius;
                    list.Add(center + new Vector2(x, y));
                }
            }
            return list;
        }

        public static List<Vector2> SamplePoisson(Vector2 center, float radius, BrushShape shape, int desiredCount, float minSpacing, float jitter, int seed)
        {
            var list = AcquireList(desiredCount);
            float r = Mathf.Max(minSpacing, 0.0001f);
            float cell = r / Mathf.Sqrt(2f);
            var grid = new Dictionary<(int, int), List<Vector2>>();
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
            int half = Mathf.CeilToInt(radius / cellSize);
            var list = AcquireList(Mathf.Max((half * 2 + 1) * (half * 2 + 1), 0));
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
            var centers = SamplePoissonBurst(center, radius, shape, Mathf.Max(cs.clusterCount, 1), Mathf.Max(minSpacing, 0.0001f), 0f, seed);
            var list = AcquireList(cs.clusterCount * Mathf.Max(cs.childPerCluster, 1));
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
            ReleaseList(centers);
            return list;
        }

        public static List<Vector2> SampleNatural(Vector2 center, float radius, BrushShape shape, int desiredCount, float baseSpacing, int seed)
        {
            int count = Mathf.Max(desiredCount, 1);
            float spacing = Mathf.Max(baseSpacing, 0.0001f);
            var rnd = new System.Random(seed);
            var list = AcquireList(count);
            var candidates = SampleUniform(center, radius, shape, count * 2, rnd);
            float scale = 0.2f;
            int accepted = 0;
            var grid = new Dictionary<(int, int), List<Vector2>>();
            var active = new List<Vector2>();
            float cell = spacing / Mathf.Sqrt(2f);
            for (int i = 0; i < candidates.Count && accepted < count; i++)
            {
                var p = candidates[i];
                float nx = p.x * scale + seed * 0.013f;
                float ny = p.y * scale + seed * 0.021f;
                float n = Fbm(nx, ny, 4);
                float prob = Mathf.Clamp01(n);
                if (rnd.NextDouble() > prob) continue;
                if (!ValidPoisson(p, spacing, cell, grid)) continue;
                AddPoint(p, cell, grid, active, list);
                accepted++;
            }
            return list;
        }

        public static List<Vector2> SampleAdaptivePoisson(Vector2 center, float radius, BrushShape shape, int desiredCount, float minSpacing, float maxSpacing, float jitter, float noiseWeight, int seed)
        {
            int count = Mathf.Max(desiredCount, 1);
            float minR = Mathf.Max(minSpacing, 0.0001f);
            float maxR = Mathf.Max(maxSpacing, minR);
            var list = AcquireList(count);
            var rnd = new System.Random(seed);
            var grid = new Dictionary<(int, int), List<(Vector2, float)>>();
            int k = 30;
            int guard = count * k;
            Vector2 first = center;
            float fr = (float)rnd.NextDouble() * radius * 0.5f;
            float fa = (float)rnd.NextDouble() * Mathf.PI * 2f;
            if (shape == BrushShape.Circle) first = center + new Vector2(Mathf.Cos(fa) * fr, Mathf.Sin(fa) * fr);
            float fnoise = Fbm(first.x * 0.2f + seed * 0.013f, first.y * 0.2f + seed * 0.021f, 4);
            float t0 = Mathf.Clamp01(fnoise);
            if (!Mathf.Approximately(noiseWeight, 1f)) t0 = Mathf.Pow(t0, Mathf.Max(0.0001f, noiseWeight));
            float frad = Mathf.Lerp(maxR, minR, t0);
            AddAdaptivePoint(first, frad, minR / Mathf.Sqrt(2f), grid, list);
            var active = new List<(Vector2, float)>();
            active.Add((first, frad));
            while (active.Count > 0 && list.Count < count && guard-- > 0)
            {
                int ai = rnd.Next(0, active.Count);
                var baseP = active[ai].Item1;
                bool found = false;
                for (int it = 0; it < k; it++)
                {
                    float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float rad = active[ai].Item2 * (1f + (float)rnd.NextDouble());
                    float jr = active[ai].Item2 * jitter;
                    rad += ((float)rnd.NextDouble() * 2f - 1f) * jr;
                    var cand = baseP + new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
                    if (!InsideShape(cand, center, radius, shape)) continue;
                    float n = Fbm(cand.x * 0.2f + seed * 0.013f, cand.y * 0.2f + seed * 0.021f, 4);
                    float t = Mathf.Clamp01(n);
                    if (!Mathf.Approximately(noiseWeight, 1f)) t = Mathf.Pow(t, Mathf.Max(0.0001f, noiseWeight));
                    float localR = Mathf.Lerp(maxR, minR, t);
                    if (ValidAdaptive(cand, localR, minR / Mathf.Sqrt(2f), grid))
                    {
                        AddAdaptivePoint(cand, localR, minR / Mathf.Sqrt(2f), grid, list);
                        active.Add((cand, localR));
                        found = true;
                        if (list.Count >= count) break;
                    }
                }
                if (!found)
                {
                    active.RemoveAt(ai);
                }
            }
            return list;
        }

        private static (int, int) KeyAdaptive(Vector2 p, float cell)
        {
            return (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell));
        }

        private static bool ValidAdaptive(Vector2 p, float r, float cell, Dictionary<(int, int), List<(Vector2, float)>> grid)
        {
            var k = KeyAdaptive(p, cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var nk = (k.Item1 + dx, k.Item2 + dy);
                    if (!grid.TryGetValue(nk, out var l)) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        float rr = Mathf.Max(r, l[i].Item2);
                        if (Vector2.SqrMagnitude(l[i].Item1 - p) < rr * rr) return false;
                    }
                }
            return true;
        }

        private static void AddAdaptivePoint(Vector2 p, float r, float cell, Dictionary<(int, int), List<(Vector2, float)>> grid, List<Vector2> list)
        {
            var k = KeyAdaptive(p, cell);
            if (!grid.TryGetValue(k, out var l)) { l = new List<(Vector2, float)>(); grid[k] = l; }
            l.Add((p, r));
            list.Add(p);
        }

        private static float Fbm(float x, float y, int octaves)
        {
            float v = 0f;
            float amp = 0.5f;
            float freq = 1f;
            for (int i = 0; i < Mathf.Max(octaves, 1); i++)
            {
                v += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                freq *= 2f;
                amp *= 0.5f;
            }
            return v;
        }

        private static void AddPoint(Vector2 p, float cell, Dictionary<(int, int), List<Vector2>> grid, List<Vector2> active, List<Vector2> list)
        {
            var k = Key(p, cell);
            if (!grid.TryGetValue(k, out var l)) { l = new List<Vector2>(); grid[k] = l; }
            l.Add(p);
            active.Add(p);
            list.Add(p);
        }

        private static (int, int) Key(Vector2 p, float cell)
        {
            return (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell));
        }

        private static bool ValidPoisson(Vector2 p, float minDist, float cell, Dictionary<(int, int), List<Vector2>> grid)
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

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
#if BURST_PRESENT
using Unity.Burst;
#endif

namespace MrTerrainPainter.Editor.Services
{
    public static class BrushJobs
    {
#if BURST_PRESENT
        [BurstCompile]
#endif
        private struct UniformJob : IJobParallelFor
        {
            public float2 center;
            public float radius;
            public int shape;
            public Unity.Mathematics.Random rnd;
            public NativeArray<float2> outPoints;
            public void Execute(int index)
            {
                if (shape == (int)BrushShape.Circle)
                {
                    float r = math.sqrt(rnd.NextFloat()) * radius;
                    float a = rnd.NextFloat() * math.PI * 2f;
                    outPoints[index] = center + new float2(math.cos(a) * r, math.sin(a) * r);
                }
                else
                {
                    float x = rnd.NextFloat(-1f, 1f);
                    float y = rnd.NextFloat(-1f, 1f);
                    outPoints[index] = center + new float2(x * radius, y * radius);
                }
            }
        }

#if BURST_PRESENT
        [BurstCompile]
#endif
        private struct JitterJob : IJobParallelFor
        {
            public float2 center;
            public int half;
            public float cellSize;
            public float jitter;
            public int shape;
            public int seed;
            public NativeArray<float2> outPoints;
            public void Execute(int index)
            {
                int dim = half * 2 + 1;
                int gx = index % dim - half;
                int gy = index / dim - half;
                float2 cellCenter = center + new float2(gx * cellSize, gy * cellSize);
                float rx = math.frac(math.sin((index + seed) * 12.9898f) * 43758.5453f);
                float ry = math.frac(math.sin((index + seed) * 78.233f) * 15431.824f);
                float2 off = new float2((rx * 2f - 1f) * cellSize * jitter, (ry * 2f - 1f) * cellSize * jitter);
                float2 p = cellCenter + off;
                bool inside;
                if (shape == (int)BrushShape.Circle)
                {
                    float rad = half * cellSize;
                    inside = math.lengthsq(p - center) <= rad * rad;
                }
                else
                {
                    inside = (math.abs(p.x - center.x) <= half * cellSize && math.abs(p.y - center.y) <= half * cellSize);
                }
                outPoints[index] = inside ? p : new float2(float.NaN, float.NaN);
            }
        }

        public static List<Vector2> SampleCandidates(Vector2 center, BrushShape shape, DistributionType dist, float radius, int count, float spacing, float jitter, int seed, ClusterSettings cs)
        {
            if (count <= 0) return new List<Vector2>();
            switch (dist)
            {
                case DistributionType.Uniform:
                {
                    var arr = new NativeArray<float2>(count, Allocator.TempJob);
                    var job = new UniformJob
                    {
                        center = new float2(center.x, center.y),
                        radius = radius,
                        shape = (int)shape,
                        rnd = new Unity.Mathematics.Random((uint)(seed == 0 ? 12345 : seed)),
                        outPoints = arr
                    };
                    var handle = job.Schedule(count, 64);
                    handle.Complete();
                    var list = new List<Vector2>(count);
                    for (int i = 0; i < arr.Length; i++) list.Add(new Vector2(arr[i].x, arr[i].y));
                    arr.Dispose();
                    return list;
                }
                case DistributionType.JitteredGrid:
                {
                    int half = Mathf.CeilToInt(radius / Mathf.Max(spacing, 0.01f));
                    int cells = (half * 2 + 1) * (half * 2 + 1);
                    var arr = new NativeArray<float2>(cells, Allocator.TempJob);
                    var job = new JitterJob
                    {
                        center = new float2(center.x, center.y),
                        half = half,
                        cellSize = spacing,
                        jitter = jitter,
                        shape = (int)shape,
                        seed = seed,
                        outPoints = arr
                    };
                    var handle = job.Schedule(cells, 64);
                    handle.Complete();
                    var list = new List<Vector2>(cells);
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var v = arr[i];
                        if (!float.IsNaN(v.x)) list.Add(new Vector2(v.x, v.y));
                    }
                    arr.Dispose();
                    return list;
                }
                case DistributionType.Cluster:
                    return BrushEngine.SampleCluster(center, radius, shape, cs, spacing, seed);
                default:
                    return BrushEngine.SamplePoisson(center, radius, shape, count, spacing, jitter, seed);
            }
        }

#if BURST_PRESENT
        [BurstCompile]
#endif
        private struct HeightSlopeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> heights;
            public int res;
            public float2 sizeXZ;
            public float sizeY;
            public float2 terrainPosXZ;
            [ReadOnly] public NativeArray<float2> pointsWorldXZ;
            public NativeArray<float> outHeightLocal;
            public NativeArray<float> outSlope;
            public void Execute(int index)
            {
                var wp = pointsWorldXZ[index] - terrainPosXZ;
                float ux = math.clamp(wp.x / sizeXZ.x, 0f, 1f);
                float uz = math.clamp(wp.y / sizeXZ.y, 0f, 1f);
                float fx = ux * (res - 1);
                float fz = uz * (res - 1);
                int x0 = (int)math.floor(fx);
                int z0 = (int)math.floor(fz);
                int x1 = math.min(x0 + 1, res - 1);
                int z1 = math.min(z0 + 1, res - 1);
                float tx = fx - x0;
                float tz = fz - z0;
                int i00 = z0 * res + x0;
                int i10 = z0 * res + x1;
                int i01 = z1 * res + x0;
                int i11 = z1 * res + x1;
                float h00 = heights[i00] * sizeY;
                float h10 = heights[i10] * sizeY;
                float h01 = heights[i01] * sizeY;
                float h11 = heights[i11] * sizeY;
                float hx0 = math.lerp(h00, h10, tx);
                float hx1 = math.lerp(h01, h11, tx);
                float h = math.lerp(hx0, hx1, tz);
                outHeightLocal[index] = h;
                float ddx = (h10 - h00 + h11 - h01) * 0.5f;
                float ddz = (h01 - h00 + h11 - h10) * 0.5f;
                ddx /= math.max(sizeXZ.x / (res - 1), 1e-5f);
                ddz /= math.max(sizeXZ.y / (res - 1), 1e-5f);
                float slope = math.degrees(math.atan(math.sqrt(ddx * ddx + ddz * ddz)));
                outSlope[index] = slope;
            }
        }

        public static void SampleHeightsAndSlopes(Terrain terrain, IList<Vector2> pointsWorldXZ, out List<float> heightsOut, out List<float> slopesOut)
        {
            heightsOut = new List<float>(pointsWorldXZ.Count);
            slopesOut = new List<float>(pointsWorldXZ.Count);
            var td = terrain != null ? terrain.terrainData : null;
            if (td == null || pointsWorldXZ.Count == 0) return;
            int res = td.heightmapResolution;
            var all = td.GetHeights(0, 0, res, res);
            var flat = new NativeArray<float>(res * res, Allocator.TempJob);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    flat[z * res + x] = all[z, x];
            var pts = new NativeArray<float2>(pointsWorldXZ.Count, Allocator.TempJob);
            for (int i = 0; i < pointsWorldXZ.Count; i++) pts[i] = new float2(pointsWorldXZ[i].x, pointsWorldXZ[i].y);
            var outH = new NativeArray<float>(pointsWorldXZ.Count, Allocator.TempJob);
            var outS = new NativeArray<float>(pointsWorldXZ.Count, Allocator.TempJob);
            var job = new HeightSlopeJob
            {
                heights = flat,
                res = res,
                sizeXZ = new float2(td.size.x, td.size.z),
                sizeY = td.size.y,
                terrainPosXZ = new float2(terrain.transform.position.x, terrain.transform.position.z),
                pointsWorldXZ = pts,
                outHeightLocal = outH,
                outSlope = outS
            };
            var handle = job.Schedule(pointsWorldXZ.Count, 64);
            handle.Complete();
            for (int i = 0; i < pointsWorldXZ.Count; i++)
            {
                heightsOut.Add(outH[i]);
                slopesOut.Add(outS[i]);
            }
            flat.Dispose();
            pts.Dispose();
            outH.Dispose();
            outS.Dispose();
        }
    }
}
#endif
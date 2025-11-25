using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class ContourDetectionService
    {
        public class ContourPath
        {
            public List<Vector3> Points = new List<Vector3>();
            public bool IsClosed;
        }

        public static List<ContourPath> ScanContours(Terrain t, Bounds bounds, float slopeThreshold)
        {
            var paths = new List<ContourPath>();
            if (t == null) return paths;
            var td = t.terrainData;
            int res = td.heightmapResolution;
            Vector3 tPos = t.transform.position;
            int xMin = Mathf.FloorToInt((bounds.min.x - tPos.x) / td.size.x * (res - 1));
            int xMax = Mathf.CeilToInt((bounds.max.x - tPos.x) / td.size.x * (res - 1));
            int zMin = Mathf.FloorToInt((bounds.min.z - tPos.z) / td.size.z * (res - 1));
            int zMax = Mathf.CeilToInt((bounds.max.z - tPos.z) / td.size.z * (res - 1));
            xMin = Mathf.Clamp(xMin, 0, res - 2); xMax = Mathf.Clamp(xMax, 0, res - 2);
            zMin = Mathf.Clamp(zMin, 0, res - 2); zMax = Mathf.Clamp(zMax, 0, res - 2);

            var segments = new List<(Vector3, Vector3)>();
            for (int z = zMin; z < zMax; z++)
            {
                for (int x = xMin; x < xMax; x++)
                {
                    float s00 = GetSlopeAtPixel(t, x, z);
                    float s10 = GetSlopeAtPixel(t, x + 1, z);
                    float s11 = GetSlopeAtPixel(t, x + 1, z + 1);
                    float s01 = GetSlopeAtPixel(t, x, z + 1);
                    int mask = 0;
                    if (s00 >= slopeThreshold) mask |= 1;
                    if (s10 >= slopeThreshold) mask |= 2;
                    if (s11 >= slopeThreshold) mask |= 4;
                    if (s01 >= slopeThreshold) mask |= 8;
                    if (mask == 0 || mask == 15) continue;
                    AddSegments(segments, mask, x, z, s00, s10, s11, s01, slopeThreshold, t);
                }
            }
            paths = BuildPathsFromSegments(segments, 0.1f);
            return paths;
        }

        static float GetSlopeAtPixel(Terrain t, int x, int z)
        {
            var td = t.terrainData;
            float nx = Mathf.Clamp01(x / (float)(td.heightmapResolution - 1));
            float nz = Mathf.Clamp01(z / (float)(td.heightmapResolution - 1));
            Vector3 n = td.GetInterpolatedNormal(nx, nz);
            return Vector3.Angle(n, Vector3.up);
        }

        static void AddSegments(List<(Vector3, Vector3)> list, int mask, int x, int z, float v0, float v1, float v2, float v3, float iso, Terrain t)
        {
            var td = t.terrainData;
            int hm = td.heightmapResolution - 1;
            float nx(float u) => Mathf.Clamp01(u / hm);
            float nz(float v) => Mathf.Clamp01(v / hm);
            Vector3 ToWorld(float u, float v)
            {
                float fx = nx(u);
                float fz = nz(v);
                float y = td.GetInterpolatedHeight(fx, fz);
                return t.transform.position + new Vector3(fx * td.size.x, y, fz * td.size.z);
            }
            float Lerp(float a, float b, float va, float vb)
            {
                float denom = (vb - va);
                if (Mathf.Abs(denom) < 1e-6f) return (a + b) * 0.5f;
                return a + (b - a) * (iso - va) / denom;
            }
            // Minimal marching squares cases: handle edges with interpolation (simplified)
            // We'll process masks with single crossing along cell edges to produce segments
            // Edges: left (x,z)-(x,z+1) ; right (x+1,z)-(x+1,z+1) ; bottom (x,z)-(x+1,z) ; top (x,z+1)-(x+1,z+1)
            Vector3? pA = null, pB = null;
            switch (mask)
            {
                case 1: // only s00 >= iso
                case 14:
                    {
                        float u1 = Lerp(x, x + 1, v0, v1);
                        float v1p = z;
                        float v2p = Lerp(z, z + 1, v0, v3);
                        float u2 = x;
                        pA = ToWorld(u1, v1p);
                        pB = ToWorld(u2, v2p);
                        break;
                    }
                case 2: // only s10 >= iso
                case 13:
                    {
                        float u1 = Lerp(x, x + 1, v0, v1);
                        float v1p = z;
                        float v2p = Lerp(z, z + 1, v1, v2);
                        float u2 = x + 1;
                        pA = ToWorld(u1, v1p);
                        pB = ToWorld(u2, v2p);
                        break;
                    }
                case 4: // only s11 >= iso
                case 11:
                    {
                        float u1 = Lerp(x + 1, x, v2, v3);
                        float v1p = z + 1;
                        float v2p = Lerp(z + 1, z, v2, v1);
                        float u2 = x + 1;
                        pA = ToWorld(u1, v1p);
                        pB = ToWorld(u2, v2p);
                        break;
                    }
                case 8: // only s01 >= iso
                case 7:
                    {
                        float u1 = Lerp(x, x + 1, v3, v2);
                        float v1p = z + 1;
                        float v2p = Lerp(z + 1, z, v3, v0);
                        float u2 = x;
                        pA = ToWorld(u1, v1p);
                        pB = ToWorld(u2, v2p);
                        break;
                    }
                default:
                    break;
            }
            if (pA.HasValue && pB.HasValue) list.Add((pA.Value, pB.Value));
        }

        static List<ContourPath> BuildPathsFromSegments(List<(Vector3 p1, Vector3 p2)> segments, float linkThreshold)
        {
            var paths = new List<ContourPath>();
            var unused = new List<(Vector3 p1, Vector3 p2)>(segments);
            while (unused.Count > 0)
            {
                var seg = unused[unused.Count - 1]; unused.RemoveAt(unused.Count - 1);
                var path = new ContourPath();
                path.Points.Add(seg.p1);
                path.Points.Add(seg.p2);
                bool extended = true;
                while (extended)
                {
                    extended = false;
                    for (int i = unused.Count - 1; i >= 0; i--)
                    {
                        var s = unused[i];
                        if (Vector3.Distance(path.Points[path.Points.Count - 1], s.p1) <= linkThreshold)
                        {
                            path.Points.Add(s.p2); unused.RemoveAt(i); extended = true; break;
                        }
                        if (Vector3.Distance(path.Points[path.Points.Count - 1], s.p2) <= linkThreshold)
                        {
                            path.Points.Add(s.p1); unused.RemoveAt(i); extended = true; break;
                        }
                    }
                }
                path.IsClosed = path.Points.Count > 2 && Vector3.Distance(path.Points[0], path.Points[path.Points.Count - 1]) <= linkThreshold;
                paths.Add(path);
            }
            return paths;
        }

        public static List<FacadeDetectionService.CliffSlice> ConvertToSlices(ContourPath path, Terrain t)
        {
            var slices = new List<FacadeDetectionService.CliffSlice>();
            if (path == null || path.Points == null || path.Points.Count < 2 || t == null) return slices;
            for (int i = 0; i < path.Points.Count; i++)
            {
                var pos = path.Points[i];
                if (MrTerrainPainter.Editor.Utils.TerrainUtils.TryGetHeightAndNormal(t, pos, out float h, out Vector3 n))
                {
                    pos.y = h;
                }
                var prev = path.Points[Mathf.Max(0, i - 1)];
                var next = path.Points[Mathf.Min(path.Points.Count - 1, i + 1)];
                var tangent = (next - prev);
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.forward;
                tangent = tangent.normalized;
                var up = Vector3.up;
                var normal = Vector3.Cross(tangent, up).normalized;
                // 修正法线指向低处
                float hForward = t.SampleHeight(pos + normal * 0.5f);
                float hBack = t.SampleHeight(pos - normal * 0.5f);
                if (hBack < hForward) normal = -normal;
                float estimatedHeight = 2f;
                slices.Add(new FacadeDetectionService.CliffSlice
                {
                    BottomPosition = pos,
                    TopPosition = pos + Vector3.up * estimatedHeight,
                    Normal = normal,
                    Direction = up
                });
            }
            return slices;
        }
    }
}
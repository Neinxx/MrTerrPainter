using System.Collections.Generic;
using UnityEngine;
using MrTerrainPainter.Editor.Utils;

namespace MrTerrainPainter.Editor.Services
{
    public static class GlobalTerrainScanner
    {
        public class FacadePath
        {
            public Terrain Terrain;
            public List<Vector3> WorldPoints = new List<Vector3>();
            public float Length;
        }

        public static List<FacadePath> ScanAllTerrains(List<Terrain> terrains, float slopeThreshold, float minPathLength, float simplifyEpsilon)
        {
            var allPaths = new List<FacadePath>();
            if (terrains == null) return allPaths;
            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                var segments = ExtractSegments(t, slopeThreshold);
                var rawPaths = LinkSegments(segments);
                foreach (var path in rawPaths)
                {
                    float len = GetPathLength(path);
                    if (len < Mathf.Max(0.01f, minPathLength)) continue;
                    var worldPath = new List<Vector3>(path.Count);
                    for (int i = 0; i < path.Count; i++) worldPath.Add(TerrainPointToWorld(t, path[i]));
                    var simplified = GeometryUtils.SimplifyPathRDP(worldPath, Mathf.Max(0.01f, simplifyEpsilon));
                    allPaths.Add(new FacadePath { Terrain = t, WorldPoints = simplified ?? worldPath, Length = len });
                }
            }
            return allPaths;
        }

        static List<(Vector2 p1, Vector2 p2)> ExtractSegments(Terrain t, float threshold)
        {
            var segments = new List<(Vector2, Vector2)>();
            var td = t.terrainData;
            int w = td.heightmapResolution;
            int h = td.heightmapResolution;
            for (int y = 0; y < h - 1; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    int mask = 0;
                    if (GetSlope(t, x, y) >= threshold) mask |= 1;         // BL
                    if (GetSlope(t, x + 1, y) >= threshold) mask |= 2;     // BR
                    if (GetSlope(t, x + 1, y + 1) >= threshold) mask |= 4; // TR
                    if (GetSlope(t, x, y + 1) >= threshold) mask |= 8;     // TL
                    if (mask == 0 || mask == 15) continue;
                    AddSegmentForMask(mask, x, y, segments);
                }
            }
            return segments;
        }

        static float GetSlope(Terrain t, int x, int y)
        {
            float u = x / (float)(t.terrainData.heightmapResolution - 1);
            float v = y / (float)(t.terrainData.heightmapResolution - 1);
            Vector3 n = t.terrainData.GetInterpolatedNormal(u, v);
            return Vector3.Angle(n, Vector3.up);
        }

        static void AddSegmentForMask(int mask, int x, int y, List<(Vector2, Vector2)> list)
        {
            Vector2 top = new Vector2(x + 0.5f, y + 1f);
            Vector2 bottom = new Vector2(x + 0.5f, y);
            Vector2 left = new Vector2(x, y + 0.5f);
            Vector2 right = new Vector2(x + 1f, y + 0.5f);
            switch (mask)
            {
                case 1: list.Add((left, bottom)); break;
                case 2: list.Add((bottom, right)); break;
                case 3: list.Add((left, right)); break;
                case 4: list.Add((right, top)); break;
                case 5: list.Add((left, top)); list.Add((bottom, right)); break;
                case 6: list.Add((bottom, top)); break;
                case 7: list.Add((left, top)); break;
                case 8: list.Add((top, left)); break;
                case 9: list.Add((top, bottom)); break;
                case 10: list.Add((top, right)); list.Add((bottom, left)); break;
                case 11: list.Add((top, right)); break;
                case 12: list.Add((right, left)); break;
                case 13: list.Add((bottom, left)); break;
                case 14: list.Add((left, bottom)); break;
            }
        }

        static List<List<Vector2>> LinkSegments(List<(Vector2 p1, Vector2 p2)> segments)
        {
            var paths = new List<List<Vector2>>();
            if (segments == null || segments.Count == 0) return paths;
            var pool = new HashSet<(Vector2, Vector2)>(segments);
            while (pool.Count > 0)
            {
                var first = System.Linq.Enumerable.First(pool);
                pool.Remove(first);
                var currentPath = new List<Vector2> { first.Item1, first.Item2 };
                bool grown = true;
                while (grown)
                {
                    grown = false;
                    var tail = currentPath[currentPath.Count - 1];
                    foreach (var seg in pool)
                    {
                        if ((seg.Item1 - tail).sqrMagnitude < 1e-4f)
                        { currentPath.Add(seg.Item2); pool.Remove(seg); grown = true; break; }
                        if ((seg.Item2 - tail).sqrMagnitude < 1e-4f)
                        { currentPath.Add(seg.Item1); pool.Remove(seg); grown = true; break; }
                    }
                }
                paths.Add(currentPath);
            }
            return paths;
        }

        static Vector3 TerrainPointToWorld(Terrain t, Vector2 gridPos)
        {
            var td = t.terrainData;
            float u = gridPos.x / (td.heightmapResolution - 1);
            float v = gridPos.y / (td.heightmapResolution - 1);
            float y = td.GetInterpolatedHeight(u, v);
            var local = new Vector3(u * td.size.x, y, v * td.size.z);
            return t.transform.position + local;
        }

        static float GetPathLength(List<Vector2> path)
        {
            float l = 0f; for (int i = 0; i < path.Count - 1; i++) l += Vector2.Distance(path[i], path[i + 1]); return l;
        }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Editor.Utils
{
    public static class GeometryUtils
    {
        static float PerpendicularDistance(Vector3 pt, Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            float mag = Mathf.Sqrt(dx * dx + dz * dz);
            if (mag > 0f) { dx /= mag; dz /= mag; }
            float pvx = pt.x - a.x;
            float pvz = pt.z - a.z;
            float pvDot = pvx * dx + pvz * dz;
            float dsx = pvx - pvDot * dx;
            float dsz = pvz - pvDot * dz;
            return Mathf.Sqrt(dsx * dsx + dsz * dsz);
        }

        public static List<Vector3> SimplifyPathRDP(List<Vector3> points, float epsilon)
        {
            if (points == null || points.Count < 3) return points;
            int first = 0;
            int last = points.Count - 1;
            var keep = new List<int> { first, last };
            SimplifySection(points, first, last, epsilon, keep);
            keep.Sort();
            var result = new List<Vector3>(keep.Count);
            for (int i = 0; i < keep.Count; i++) result.Add(points[keep[i]]);
            return result;
        }

        static void SimplifySection(List<Vector3> points, int first, int last, float epsilon, List<int> keep)
        {
            float max = 0f; int idx = 0;
            for (int i = first + 1; i < last; i++)
            {
                float d = PerpendicularDistance(points[i], points[first], points[last]);
                if (d > max) { max = d; idx = i; }
            }
            if (max > epsilon)
            {
                keep.Add(idx);
                SimplifySection(points, first, idx, epsilon, keep);
                SimplifySection(points, idx, last, epsilon, keep);
            }
        }
    }
}
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MrTerrainPainter.Editor.Services;

namespace MrTerrainPainter.Editor.Utils
{
    public static class SplineUtils
    {
        public static Vector3 GetPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t; float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        public static List<FacadeDetectionService.CliffSlice> ResampleSlicesSmoothly(List<FacadeDetectionService.CliffSlice> originalSlices, float spacing)
        {
            var result = new List<FacadeDetectionService.CliffSlice>();
            if (originalSlices == null || originalSlices.Count < 4) return originalSlices ?? result;

            spacing = Mathf.Max(spacing, 0.01f);
            var points = originalSlices.Select(s => s.BottomPosition).ToList();
            var heights = originalSlices.Select(s => s.Height).ToList();
            // 平均原始法线用于外侧校正
            Vector3 avgN = Vector3.zero;
            for (int i = 0; i < originalSlices.Count; i++) avgN += originalSlices[i].Normal;
            avgN = avgN.sqrMagnitude > 1e-6f ? avgN.normalized : Vector3.forward;

            for (int i = 0; i < points.Count - 3; i++)
            {
                Vector3 p0 = points[i];
                Vector3 p1 = points[i + 1];
                Vector3 p2 = points[i + 2];
                Vector3 p3 = points[i + 3];

                float segmentLen = Vector3.Distance(p1, p2);
                int steps = Mathf.Max(1, Mathf.CeilToInt(segmentLen / spacing));

                for (int s = 0; s <= steps; s++)
                {
                    float t = steps == 0 ? 0f : (s / (float)steps);
                    Vector3 pos = GetPoint(p0, p1, p2, p3, t);
                    // 切线与法线（法线滞后：取前向微步）
                    float lag = Mathf.Max(0.01f, spacing * 0.05f);
                    Vector3 posAhead = GetPoint(p0, p1, p2, p3, Mathf.Clamp01(t + lag));
                    Vector3 tangent = (posAhead - pos);
                    tangent.y = 0f;
                    if (tangent.sqrMagnitude < 1e-6f) tangent = (p2 - p1);
                    tangent = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.right;
                    Vector3 up = Vector3.up;
                    Vector3 normal = Vector3.Cross(tangent, up).normalized;
                    if (Vector3.Dot(normal, avgN) < 0f) normal = -normal;

                    // 高度线性插值（使用相邻控制点高度）
                    float h = Mathf.Lerp(heights[i + 1], heights[i + 2], t);

                    result.Add(new FacadeDetectionService.CliffSlice
                    {
                        BottomPosition = pos,
                        TopPosition = new Vector3(pos.x, pos.y + h, pos.z),
                        Normal = normal,
                        Direction = up
                    });
                }
            }
            return result;
        }
    }
}
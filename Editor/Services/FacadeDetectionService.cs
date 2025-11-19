using MrTerrainPainter.Editor.Utils;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class FacadeDetectionService
    {
        public struct FacadeInfo
        {
            public Vector3 topPos;
            public Vector3 bottomPos;
            public float heightMeters;
            public Vector3 forward;
            public Vector3 right;
        }

        public static bool TryDetectFacade(Terrain t, Vector3 foot, float enterSlopeDeg, float exitSlopeDeg, float stepMeters, float maxDistMeters, out FacadeInfo info)
        {
            info = default;
            if (t == null) return false;
            if (!TerrainUtils.TryGetHeightAndNormal(t, foot, out var h0, out var n0)) return false;

            var up = Vector3.up;
            var forward = Vector3.ProjectOnPlane(n0, up);
            if (forward.sqrMagnitude < 1e-6f) return false;
            forward.Normalize();
            var right = Vector3.Cross(up, forward).normalized;

            float enter = Mathf.Clamp(enterSlopeDeg, 0f, 90f);
            float exit = Mathf.Clamp(exitSlopeDeg, 0f, 90f);
            float step = Mathf.Max(stepMeters, 0.05f);
            float maxD = Mathf.Max(maxDistMeters, step);
            float hysteresis = 1.0f;
            float epsilon = 0.2f;

            bool foundEnter = false;
            Vector3 posEnter = foot;
            bool foundExit = false;
            Vector3 posExit = foot;

            Vector3 ScanSlope(Vector3 p)
            {
                // 微分降噪：pos±right*ε 的坡度均值
                float s0 = SampleSlope(t, p);
                float s1 = SampleSlope(t, p + right * epsilon);
                float s2 = SampleSlope(t, p - right * epsilon);
                float s = (s0 + s1 + s2) / 3f;
                if (!TerrainUtils.TryGetHeightAndNormal(t, p, out var h, out var nn)) nn = n0;
                return new Vector3(s, h, 0f);
            }

            bool ScanOneDirection(Vector3 dir, out Vector3 enterPos, out Vector3 exitPos)
            {
                enterPos = foot;
                exitPos = foot;
                for (float d = 0f; d <= maxD + 0.0001f; d += step)
                {
                    var p = foot + dir * d;
                    var sv = ScanSlope(p);
                    if (sv.x >= enter + hysteresis) { enterPos = p; foundEnter = true; break; }
                }
                if (!foundEnter) return false;
                for (float d = step; d <= maxD + 0.0001f; d += step)
                {
                    var p = enterPos + dir * d;
                    var sv = ScanSlope(p);
                    if (sv.x <= exit - hysteresis) { exitPos = p; foundExit = true; break; }
                }
                return foundExit;
            }

            Vector3 posEnterNeg, posExitNeg;
            Vector3 posEnterPos, posExitPos;
            bool okNeg = ScanOneDirection(-forward, out posEnterNeg, out posExitNeg);
            foundEnter = false; foundExit = false;
            bool okPos = ScanOneDirection(forward, out posEnterPos, out posExitPos);
            if (!okNeg && !okPos) return false;

            // 选择高度差更大的区间
            float hBNeg = TerrainUtils.TryGetHeightAndNormal(t, posEnterNeg, out var hbNeg, out var _) ? hbNeg : h0;
            float hTNeg = TerrainUtils.TryGetHeightAndNormal(t, posExitNeg, out var htNeg, out var _) ? htNeg : h0;
            float hBNegDiff = Mathf.Max(0f, htNeg - hBNeg);
            float hBPos = TerrainUtils.TryGetHeightAndNormal(t, posEnterPos, out var hbPos, out var _) ? hbPos : h0;
            float hTPos = TerrainUtils.TryGetHeightAndNormal(t, posExitPos, out var htPos, out var _) ? htPos : h0;
            float hBPosDiff = Mathf.Max(0f, htPos - hBPos);

            if (okNeg && (!okPos || hBNegDiff >= hBPosDiff)) { posEnter = posEnterNeg; posExit = posExitNeg; }
            else { posEnter = posEnterPos; posExit = posExitPos; }

            // 区间二分精化到 ~0.05m
            float lo = 0f;
            float hi = Vector3.Distance(posEnter, posExit);
            for (int i = 0; i < 10; i++)
            {
                float mid = (lo + hi) * 0.5f;
                var pMid = posEnter + (-forward) * mid;
                float sMid = ScanSlope(pMid).x;
                if (sMid <= exit) hi = mid; else lo = mid;
                if (Mathf.Abs(hi - lo) <= 0.05f) break;
            }
            var refinedBottom = posEnter;
            var refinedTop = posEnter + (Vector3.Normalize(posExit - posEnter)) * hi;

            if (!TerrainUtils.TryGetHeightAndNormal(t, refinedBottom, out var hB, out var nB)) hB = h0;
            if (!TerrainUtils.TryGetHeightAndNormal(t, refinedTop, out var hT, out var nT)) hT = h0;
            float heightMeters = Mathf.Max(0f, hT - hB);

            info = new FacadeInfo
            {
                topPos = new Vector3(refinedTop.x, hT, refinedTop.z),
                bottomPos = new Vector3(refinedBottom.x, hB, refinedBottom.z),
                heightMeters = heightMeters,
                forward = forward,
                right = right
            };
            return heightMeters > 0f;
        }

        private static float SampleSlope(Terrain t, Vector3 p)
        {
            if (TerrainUtils.TryGetHeightAndNormal(t, p, out var h, out var n))
            {
                return TerrainUtils.ComputeSlope(n);
            }
            return 0f;
        }
    }
}

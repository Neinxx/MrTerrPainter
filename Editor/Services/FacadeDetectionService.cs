using MrTerrainPainter.Editor.Utils;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class FacadeDetectionService
    {
        public struct CliffSlice
        {
            public Vector3 BottomPosition;
            public Vector3 TopPosition;
            public Vector3 Normal;
            public Vector3 Direction;
            public float Height => Vector3.Distance(TopPosition, BottomPosition);
        }

        public static System.Collections.Generic.List<CliffSlice> ApplyGlobalConstraints(System.Collections.Generic.List<CliffSlice> slices, float minHeightMeters, bool enforceParallelXZ, float offsetRightMeters, float offsetOutMeters)
        {
            if (slices == null) return slices;
            float minH = Mathf.Max(0.0001f, minHeightMeters);
            var origH = new float[slices.Count];
            for (int i = 0; i < slices.Count; i++)
            {
                var s0 = slices[i];
                origH[i] = Mathf.Max(0f, s0.TopPosition.y - s0.BottomPosition.y);
            }
            if (slices.Count >= 3)
            {
                int win = 5;
                int r = win / 2;
                float[] coeff = new float[] { -3f/35f, 12f/35f, 17f/35f, 12f/35f, -3f/35f };
                var smoothed = new Vector3[slices.Count];
                for (int i = 0; i < slices.Count; i++)
                {
                    Vector3 sb = Vector3.zero;
                    for (int k = -r, idx = 0; k <= r; k++, idx++)
                    {
                        int j = Mathf.Clamp(i + k, 0, slices.Count - 1);
                        sb += slices[j].BottomPosition * coeff[idx];
                    }
                    smoothed[i] = sb;
                }
                for (int i = 0; i < slices.Count; i++)
                {
                    int ip = Mathf.Max(0, i - 1);
                    int inext = Mathf.Min(slices.Count - 1, i + 1);
                    var bp = smoothed[ip];
                    var bn = smoothed[inext];
                    var tangent = new Vector3(bn.x - bp.x, 0f, bn.z - bp.z);
                    if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.right;
                    tangent.Normalize();
                    var up = Vector3.up;
                    var faceN = Vector3.Normalize(Vector3.Cross(tangent, up));
                    var s = slices[i];
                    s.Direction = up;
                    s.Normal = faceN;
                    var alignXZ = smoothed[i];
                    if (enforceParallelXZ)
                    {
                        Vector3 bottomAligned = new Vector3(alignXZ.x, s.BottomPosition.y, alignXZ.z);
                        if (TerrainUtils.TryGetHeightAndNormal(Terrain.activeTerrain, bottomAligned, out var hb, out var _)) bottomAligned.y = hb;
                        float desiredH = Mathf.Max(minH, origH[i]);
                        Vector3 topAligned = new Vector3(alignXZ.x, bottomAligned.y + desiredH, alignXZ.z);
                        var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
                        bottomAligned += rightAxis * offsetRightMeters + s.Normal * offsetOutMeters;
                        topAligned += rightAxis * offsetRightMeters + s.Normal * offsetOutMeters;
                        s.BottomPosition = bottomAligned;
                        s.TopPosition = topAligned;
                    }
                    slices[i] = s;
                }
            }
            return slices;
        }
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
            float step = Mathf.Max(stepMeters, 0.1f);
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

        public static System.Collections.Generic.List<CliffSlice> TraceVirtualFacade(
            Terrain t,
            Vector3 start,
            float lengthMeters,
            float enterSlopeDeg,
            float exitSlopeDeg,
            float stepMeters,
            MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode mode,
            int window,
            float sigma)
        {
            var slices = new System.Collections.Generic.List<CliffSlice>();
            if (t == null) return slices;
            if (!TerrainUtils.TryGetHeightAndNormal(t, start, out var h0, out var n0)) return slices;
            var up = Vector3.up;
            var forward = Vector3.ProjectOnPlane(n0, up);
            if (forward.sqrMagnitude < 1e-6f) return slices;
            forward.Normalize();
            var right = Vector3.Cross(up, forward).normalized;

            float enter = Mathf.Clamp(enterSlopeDeg, 0f, 90f);
            float exit = Mathf.Clamp(exitSlopeDeg, 0f, 90f);
            float step = Mathf.Max(stepMeters, 0.05f);
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(0.1f, lengthMeters) / step));
            float epsilon = 0.2f;

            Vector3 ScanEnter(Vector3 p)
            {
                for (float d = 0f; d <= lengthMeters + 0.0001f; d += step)
                {
                    var q = p + (-forward) * d;
                    float s = (SampleSlope(t, q) + SampleSlope(t, q + right * epsilon) + SampleSlope(t, q - right * epsilon)) / 3f;
                    if (s >= enter) return q;
                }
                return p;
            }
            Vector3 ScanExit(Vector3 enterPos)
            {
                for (float d = step; d <= lengthMeters + 0.0001f; d += step)
                {
                    var q = enterPos + (-forward) * d;
                    float s = (SampleSlope(t, q) + SampleSlope(t, q + right * epsilon) + SampleSlope(t, q - right * epsilon)) / 3f;
                    if (s <= exit) return q;
                }
                return enterPos;
            }
            Vector3 RefineBottom(Vector3 enterPos)
            {
                float bestSlope = float.MaxValue;
                float bestH = float.MaxValue;
                Vector3 best = enterPos;
                float localWin = Mathf.Max(step * 3f, 1f);
                float inc = Mathf.Max(step * 0.25f, 0.1f);
                for (float d = 0f; d <= localWin + 0.0001f; d += inc)
                {
                    var q = enterPos + (forward) * d; // 向坡底方向搜索更平坦处
                    float s = (SampleSlope(t, q) + SampleSlope(t, q + right * epsilon) + SampleSlope(t, q - right * epsilon)) / 3f;
                    if (!TerrainUtils.TryGetHeightAndNormal(t, q, out var h, out var _)) continue;
                    if (s < bestSlope || (Mathf.Abs(s - bestSlope) < 0.1f && h < bestH))
                    {
                        bestSlope = s; bestH = h; best = new Vector3(q.x, h, q.z);
                    }
                }
                return best;
            }

            for (int dirSign = -1; dirSign <= 1; dirSign += 2)
            {
                var lateralDir = dirSign < 0 ? right : -right;
                for (int i = -steps; i <= steps; i++)
                {
                    var basePos = start + lateralDir * (i * step);
                    var bEnter = ScanEnter(basePos);
                    var bPos = RefineBottom(bEnter);
                    var tPos = ScanExit(bPos);
                    if (TerrainUtils.TryGetHeightAndNormal(t, bPos, out var hb, out var nb) && TerrainUtils.TryGetHeightAndNormal(t, tPos, out var ht, out var nt))
                    {
                        var bottom = new Vector3(bPos.x, hb, bPos.z);
                        var top = new Vector3(tPos.x, ht, tPos.z);
                        var dir = (top - bottom);
                        var normalSmoothed = ((nb + nt) * 0.5f).normalized;
                        slices.Add(new CliffSlice
                        {
                            BottomPosition = bottom,
                            TopPosition = top,
                            Direction = up,
                            Normal = Vector3.zero
                        });
                    }
                }
            }

            int win = Mathf.Max(3, (window % 2 == 0) ? window + 1 : window);
            if (slices.Count >= win)
            {
                var tmp = new System.Collections.Generic.List<CliffSlice>(slices.Count);
                if (mode == MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode.Mean)
                {
                    int r = win / 2;
                    for (int i = 0; i < slices.Count; i++)
                    {
                        int a0 = Mathf.Max(0, i - r);
                        int a1 = Mathf.Min(slices.Count - 1, i + r);
                        Vector3 btm = Vector3.zero, top = Vector3.zero, nacc = Vector3.zero;
                        int cnt = 0;
                        for (int k = a0; k <= a1; k++) { btm += slices[k].BottomPosition; top += slices[k].TopPosition; nacc += slices[k].Normal; cnt++; }
                        btm /= cnt; top /= cnt; nacc = nacc.normalized;
                        var dir = Vector3.up;
                        // 重新贴地：平滑后回采高度
                        if (TerrainUtils.TryGetHeightAndNormal(t, btm, out var hb, out var _)) btm.y = hb;
                        if (TerrainUtils.TryGetHeightAndNormal(t, top, out var ht, out var _)) top.y = ht;
                        tmp.Add(new CliffSlice { BottomPosition = btm, TopPosition = top, Normal = nacc, Direction = dir });
                    }
                }
                else if (mode == MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode.Gaussian)
                {
                    int r = win / 2;
                    float s = Mathf.Max(0.1f, sigma);
                    // 预计算高斯权重
                    var wts = new float[win];
                    float sumW = 0f;
                    for (int i = -r, idx = 0; i <= r; i++, idx++) { float w = Mathf.Exp(-(i * i) / (2f * s * s)); wts[idx] = w; sumW += w; }
                    for (int i = 0; i < win; i++) wts[i] /= sumW;
                    for (int i = 0; i < slices.Count; i++)
                    {
                        Vector3 btm = Vector3.zero, top = Vector3.zero, nacc = Vector3.zero;
                        for (int k = -r, idx = 0; k <= r; k++, idx++)
                        {
                            int j = Mathf.Clamp(i + k, 0, slices.Count - 1);
                            float w = wts[idx];
                            btm += slices[j].BottomPosition * w;
                            top += slices[j].TopPosition * w;
                            nacc += slices[j].Normal * w;
                        }
                        nacc = nacc.normalized;
                        var dir = Vector3.up;
                        if (TerrainUtils.TryGetHeightAndNormal(t, btm, out var hb, out var _)) btm.y = hb;
                        if (TerrainUtils.TryGetHeightAndNormal(t, top, out var ht, out var _)) top.y = ht;
                        tmp.Add(new CliffSlice { BottomPosition = btm, TopPosition = top, Normal = nacc, Direction = dir });
                    }
                }
                else
                {
                    int r = win / 2;
                    for (int i = 0; i < slices.Count; i++)
                    {
                        int a0 = Mathf.Max(0, i - r);
                        int a1 = Mathf.Min(slices.Count - 1, i + r);
                        // 组件中位数
                        System.Span<float> bx = stackalloc float[win];
                        System.Span<float> by = stackalloc float[win];
                        System.Span<float> bz = stackalloc float[win];
                        System.Span<float> tx = stackalloc float[win];
                        System.Span<float> ty = stackalloc float[win];
                        System.Span<float> tz = stackalloc float[win];
                        System.Span<float> nx = stackalloc float[win];
                        System.Span<float> ny = stackalloc float[win];
                        System.Span<float> nz = stackalloc float[win];
                        int idx = 0;
                        for (int k = a0; k <= a1; k++)
                        {
                            bx[idx] = slices[k].BottomPosition.x;
                            by[idx] = slices[k].BottomPosition.y;
                            bz[idx] = slices[k].BottomPosition.z;
                            tx[idx] = slices[k].TopPosition.x;
                            ty[idx] = slices[k].TopPosition.y;
                            tz[idx] = slices[k].TopPosition.z;
                            nx[idx] = slices[k].Normal.x;
                            ny[idx] = slices[k].Normal.y;
                            nz[idx] = slices[k].Normal.z;
                            idx++;
                        }
                        System.Array.Sort(bx.ToArray()); System.Array.Sort(by.ToArray()); System.Array.Sort(bz.ToArray());
                        System.Array.Sort(tx.ToArray()); System.Array.Sort(ty.ToArray()); System.Array.Sort(tz.ToArray());
                        System.Array.Sort(nx.ToArray()); System.Array.Sort(ny.ToArray()); System.Array.Sort(nz.ToArray());
                        int mid = idx / 2;
                        var btm = new Vector3(bx[mid], by[mid], bz[mid]);
                        var top = new Vector3(tx[mid], ty[mid], tz[mid]);
                        var nacc = new Vector3(nx[mid], ny[mid], nz[mid]).normalized;
                        var dir = Vector3.up;
                        if (TerrainUtils.TryGetHeightAndNormal(t, btm, out var hb, out var _)) btm.y = hb;
                        if (TerrainUtils.TryGetHeightAndNormal(t, top, out var ht, out var _)) top.y = ht;
                        tmp.Add(new CliffSlice { BottomPosition = btm, TopPosition = top, Normal = nacc, Direction = dir });
                    }
                }
                slices = tmp;
            }
            // 依据双轨重算面的法线：normal = normalize(cross(tangent, up))，并使其指向地形外侧（与forward同向）
            if (slices.Count >= 2)
            {
                for (int i = 0; i < slices.Count; i++)
                {
                    int ip = Mathf.Max(0, i - 1);
                    int inext = Mathf.Min(slices.Count - 1, i + 1);
                    var bp = slices[ip].BottomPosition;
                    var bn = slices[inext].BottomPosition;
                    var tangent = new Vector3(bn.x - bp.x, 0f, bn.z - bp.z);
                    if (tangent.sqrMagnitude < 1e-6f) tangent = right * 1f; // 退化处理
                    tangent.Normalize();
                    var faceN = Vector3.Normalize(Vector3.Cross(tangent, up));
                    if (Vector3.Dot(faceN, forward) < 0f) faceN = -faceN;
                    var s = slices[i];
                    s.Normal = faceN;
                    s.Direction = up;
                    s.TopPosition = new Vector3(s.BottomPosition.x, s.TopPosition.y, s.BottomPosition.z);
                    slices[i] = s;
                }
            }
            return slices;
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

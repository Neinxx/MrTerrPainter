using MrTerrainPainter.Editor.Utils;
using MrTerrainPainter.Runtime.Profiles;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class FacadeDetectionService
    {
        public class FacadeTraceRequest
        {
            public Terrain terrain;
            public Vector3 start;
            public float lengthMeters;
            public float enterSlopeDeg;
            public float exitSlopeDeg;
            public float stepMeters;
            public MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode mode;
            public int window;
            public float sigma;
            public bool useGlobalConstraints;
            public float minHeightMeters;
            public float offsetRightMeters;
            public float offsetOutMeters;
        }

        public class FacadeTraceBuilder
        {
            private readonly FacadeTraceRequest r = new FacadeTraceRequest();
            public FacadeTraceBuilder Terrain(Terrain t) { r.terrain = t; return this; }
            public FacadeTraceBuilder Start(Vector3 s) { r.start = s; return this; }
            public FacadeTraceBuilder Length(float l) { r.lengthMeters = l; return this; }
            public FacadeTraceBuilder Slopes(float enter, float exit) { r.enterSlopeDeg = enter; r.exitSlopeDeg = exit; return this; }
            public FacadeTraceBuilder Step(float step) { r.stepMeters = step; return this; }
            public FacadeTraceBuilder Smoothing(MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode m, int win, float sig) { r.mode = m; r.window = win; r.sigma = sig; return this; }
            public FacadeTraceBuilder FromItem(MrTerrainPainter.Runtime.Profiles.VegetationItem item)
            {
                if (item == null) return this;
                r.enterSlopeDeg = item.edgeSlopeEnter;
                r.exitSlopeDeg = item.edgeSlopeExit;
                r.stepMeters = item.probeStep;
                r.mode = item.facadeSmoothingMode;
                r.window = item.facadeSmoothingWindow;
                r.sigma = item.facadeSmoothingSigma;
                return this;
            }
            public FacadeTraceBuilder FromConfig(MrTerrainPainter.Editor.Config.MrTerrainPainterConfig cfg)
            {
                if (cfg == null) return this;
                r.useGlobalConstraints = true;
                r.minHeightMeters = cfg.minFacadeHeightMeters;
                r.offsetRightMeters = cfg.curveOffsetRightMeters;
                r.offsetOutMeters = cfg.curveOffsetOutMeters;
                return this;
            }
            public FacadeTraceRequest Build() { return r; }
        }

        public class FacadePath
        {
            public Terrain SourceTerrain;
            public System.Collections.Generic.List<CliffSlice> SmoothSlices;
            public float TotalLength;
        }
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
                float[] coeff = new float[] { -3f / 35f, 12f / 35f, 17f / 35f, 12f / 35f, -3f / 35f };
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
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            float minH = cfg != null ? Mathf.Max(0.0001f, cfg.minFacadeHeightMeters) : 0.3f;
            return heightMeters >= minH;
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

            System.Func<Vector3, (float, float)> Slope3 = pos =>
            {
                float s0 = SampleSlope(t, pos);
                float sL = SampleSlope(t, pos - right * epsilon);
                float sR = SampleSlope(t, pos + right * epsilon);
                float mean = (s0 + sL + sR) / 3f;
                float maxv = Mathf.Max(s0, Mathf.Max(sL, sR));
                return (mean, maxv);
            };
            Vector3 ScanEnterCanny(Vector3 p, out float high, out float low)
            {
                var acc = new System.Collections.Generic.List<float>();
                for (float d = 0f; d <= lengthMeters + 0.0001f; d += step)
                {
                    var q = p + (-forward) * d;
                    var v = Slope3(q);
                    acc.Add(v.Item2);
                }
                float mean = 0f, varv = 0f;
                for (int i = 0; i < acc.Count; i++) mean += acc[i];
                mean /= Mathf.Max(1, acc.Count);
                for (int i = 0; i < acc.Count; i++) varv += (acc[i] - mean) * (acc[i] - mean);
                varv /= Mathf.Max(1, acc.Count);
                float std = Mathf.Sqrt(varv);
                high = mean + std;
                low = mean + std * 0.5f;
                for (float d = 0f; d <= lengthMeters + 0.0001f; d += step)
                {
                    var q = p + (-forward) * d;
                    var v = Slope3(q);
                    float s = v.Item2;
                    if (s >= high) return q;
                }
                return p;
            }
            Vector3 ScanExitCanny(Vector3 enterPos, float low)
            {
                for (float d = step; d <= lengthMeters + 0.0001f; d += step)
                {
                    var q = enterPos + (-forward) * d;
                    var v = Slope3(q);
                    if (v.Item2 <= low) return q;
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
                    var bEnter = ScanEnterCanny(basePos, out float high, out float low);
                    var bPos = RefineBottom(bEnter);
                    var tPos = ScanExitCanny(bPos, low);
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
            var cfgG = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            float minHGlobal = cfgG != null ? Mathf.Max(0.0001f, cfgG.minFacadeHeightMeters) : 0.3f;
            if (slices.Count > 0)
            {
                for (int i = slices.Count - 1; i >= 0; i--)
                {
                    if (slices[i].Height < minHGlobal) slices.RemoveAt(i);
                }
            }
            return slices;
        }

        public static System.Collections.Generic.List<CliffSlice> TraceVirtualFacade(FacadeTraceRequest req)
        {
            var slices = TraceVirtualFacade(req.terrain, req.start, req.lengthMeters, req.enterSlopeDeg, req.exitSlopeDeg, req.stepMeters, req.mode, req.window, req.sigma);
            if (req.useGlobalConstraints)
            {
                slices = ApplyGlobalConstraints(slices, req.minHeightMeters, true, req.offsetRightMeters, req.offsetOutMeters);
            }
            return slices;
        }

        public static System.Collections.Generic.List<CliffSlice> FilterByMinimumWidth(System.Collections.Generic.List<CliffSlice> slices, float minLenMeters, float spacingThreshold, float maxAngleDeg)
        {
            var res = new System.Collections.Generic.List<CliffSlice>();
            if (slices == null || slices.Count == 0) return res;
            int n = slices.Count;
            int i = 0;
            while (i < n)
            {
                int start = i;
                float len = 0f;
                var prev = slices[i];
                i++;
                while (i < n)
                {
                    var curr = slices[i];
                    float dist = Vector3.Distance(prev.BottomPosition, curr.BottomPosition);
                    float ang = Vector3.Angle(prev.Normal, curr.Normal);
                    if (dist > spacingThreshold * 2f || ang > maxAngleDeg) break;
                    len += dist;
                    prev = curr;
                    i++;
                }
                if (len >= Mathf.Max(0.0001f, minLenMeters))
                {
                    for (int k = start; k < i; k++) res.Add(slices[k]);
                }
            }
            return res;
        }

        private static bool InsideBrush(Vector3 pos, Vector3 center, float radius, MrTerrainPainter.Editor.Services.BrushShape shape)
        {
            float dx = pos.x - center.x; float dz = pos.z - center.z;
            if (shape == MrTerrainPainter.Editor.Services.BrushShape.Circle) return (dx * dx + dz * dz) <= radius * radius;
            return Mathf.Abs(dx) <= radius && Mathf.Abs(dz) <= radius;
        }

        internal class FacadeGrid
        {
            private readonly float cellSize;
            private readonly System.Collections.Generic.Dictionary<(int, int), System.Collections.Generic.List<Vector2>> cells = new();
            public FacadeGrid(float spacing) { cellSize = Mathf.Max(spacing, 0.01f); }
            private (int, int) Key(Vector2 p) => (Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize));
            public void Add(Vector2 p)
            {
                var k = Key(p);
                if (!cells.TryGetValue(k, out var list)) { list = new System.Collections.Generic.List<Vector2>(); cells[k] = list; }
                list.Add(p);
            }
            public bool HasNearby(Vector2 p, float minDist)
            {
                var k = Key(p);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var nk = (k.Item1 + dx, k.Item2 + dy);
                        if (!cells.TryGetValue(nk, out var list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (Vector2.SqrMagnitude(list[i] - p) < minDist * minDist) return true;
                        }
                    }
                return false;
            }
        }

        public static void ProcessFacadeAndPlace(
            Terrain terrain,
            Vector3 center,
            float radius,
            VegetationItem item,
            MrTerrainPainter.Editor.Services.BrushShape shape,
            System.Action<CliffSlice> onPlace)
        {
            if (terrain == null || item == null || onPlace == null) return;
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            var req = new FacadeTraceBuilder()
                .Terrain(terrain)
                .Start(center)
                .Length(radius * 2f)
                .FromItem(item)
                .Smoothing(cfg != null ? cfg.facadeSmoothMode : MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode.Gaussian,
                           cfg != null ? Mathf.Max(3, cfg.facadeSmoothWindow) : 5,
                           cfg != null ? Mathf.Max(0.1f, cfg.facadeSmoothSigma) : 1f)
                .FromConfig(cfg)
                .Build();
            var slices = TraceVirtualFacade(req);
            float rendererWMinLen = MrTerrainPainter.Editor.Services.PrefabMetricsCache.GetPrefabHorizontalExtentMeters(item.prefab);
            float minLenSeg = Mathf.Max(rendererWMinLen, item.edgeReferenceWidthMeters);
            slices = FilterByMinimumWidth(slices, minLenSeg, Mathf.Max(item.CoreSpacing, 0.01f), 30f);

            // [改进方案] 使用弧长自适应采样替代RDP+样条插值
            // 优点: 1) 消除RDP棱角伪影  2) 曲线间距均匀  3) 保留原始地形细节  4) 性能更优(O(n))
            if (slices != null && slices.Count > 2)
            {
                float targetSpacing = Mathf.Max(item.CoreSpacing, 0.01f);
                slices = ArcLengthResample(slices, targetSpacing);
            }
            if (slices == null || slices.Count == 0) return;
            var grid = new FacadeGrid(Mathf.Max(item.CoreSpacing, 0.01f));
            for (int i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                if (!InsideBrush(s.BottomPosition, center, radius, shape)) continue;
                var p2 = new Vector2(s.BottomPosition.x - terrain.transform.position.x, s.BottomPosition.z - terrain.transform.position.z);
                float rendererW = MrTerrainPainter.Editor.Services.PrefabMetricsCache.GetPrefabHorizontalExtentMeters(item.prefab);
                float rendererH = MrTerrainPainter.Editor.Services.PrefabMetricsCache.GetPrefabHeightMeters(item.prefab);
                float minH = cfg != null ? Mathf.Max(0.0001f, cfg.minFacadeHeightMeters) : 0.0001f;
                float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), s.Height / Mathf.Max(0.0001f, rendererH));
                float spacingThresh = Mathf.Max(item.CoreSpacing, rendererW * uni);
                if (grid.HasNearby(p2, spacingThresh)) continue;
                grid.Add(p2);
                onPlace(s);
            }
        }

        /// <summary>
        /// 弧长参数化均匀重采样 - 解决曲线不平滑和间距不均问题
        /// </summary>
        static System.Collections.Generic.List<CliffSlice> ArcLengthResample(System.Collections.Generic.List<CliffSlice> raw, float targetSpacing)
        {
            if (raw == null || raw.Count < 2) return raw;

            // 1. 计算累积弧长
            float[] arcLengths = new float[raw.Count];
            arcLengths[0] = 0f;
            for (int i = 1; i < raw.Count; i++)
            {
                float segLen = Vector3.Distance(raw[i].BottomPosition, raw[i - 1].BottomPosition);
                arcLengths[i] = arcLengths[i - 1] + segLen;
            }
            float totalLength = arcLengths[arcLengths.Length - 1];
            if (totalLength < 0.01f) return raw;

            // 2. 基于目标间距计算采样点数
            int numSamples = Mathf.Max(2, Mathf.CeilToInt(totalLength / Mathf.Max(targetSpacing, 0.01f)));
            var result = new System.Collections.Generic.List<CliffSlice>(numSamples);

            // 3. 沿弧长均匀采样并插值
            for (int i = 0; i < numSamples; i++)
            {
                float t = i / (float)(numSamples - 1); // [0,1]
                float targetArc = t * totalLength;

                // 二分查找对应的原始切片区间
                int idx = System.Array.BinarySearch(arcLengths, targetArc);
                if (idx < 0) idx = ~idx - 1; // 未精确命中，取左侧区间
                idx = Mathf.Clamp(idx, 0, raw.Count - 2);

                // 计算区间内的局部参数
                float arcStart = arcLengths[idx];
                float arcEnd = arcLengths[idx + 1];
                float arcRange = arcEnd - arcStart;
                float localT = arcRange > 0.0001f ? (targetArc - arcStart) / arcRange : 0f;
                localT = Mathf.Clamp01(localT);

                // 线性插值切片属性
                var s0 = raw[idx];
                var s1 = raw[idx + 1];
                var interpolated = new CliffSlice
                {
                    BottomPosition = Vector3.Lerp(s0.BottomPosition, s1.BottomPosition, localT),
                    TopPosition = Vector3.Lerp(s0.TopPosition, s1.TopPosition, localT),
                    Normal = Vector3.Slerp(s0.Normal, s1.Normal, localT).normalized,
                    Direction = Vector3.up
                };

                result.Add(interpolated);
            }

            return result;
        }

        /// <summary>
        /// 平滑高度插值 - 使用距离加权(IDW)替代最近邻，消除高度突变
        /// </summary>
        static float ApproximateHeightSmooth(System.Collections.Generic.List<CliffSlice> raw, Vector3 pos, int kNearest = 3)
        {
            if (raw == null || raw.Count == 0) return 1f;
            if (raw.Count == 1) return raw[0].Height;

            // 找到最近的k个切片
            var distances = new System.Collections.Generic.List<(int index, float distSq)>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                float distSq = Vector3.SqrMagnitude(raw[i].BottomPosition - pos);
                distances.Add((i, distSq));
            }
            distances.Sort((a, b) => a.distSq.CompareTo(b.distSq));

            int k = Mathf.Min(kNearest, distances.Count);

            // IDW插值 (Inverse Distance Weighting - 距离平方反比)
            float sumWeight = 0f;
            float sumHeight = 0f;
            for (int i = 0; i < k; i++)
            {
                float distSq = distances[i].distSq;
                // 特殊处理：如果距离极近(<1cm)，直接返回该高度
                if (distSq < 0.0001f) return raw[distances[i].index].Height;

                float weight = 1f / Mathf.Max(distSq, 0.0001f);
                sumWeight += weight;
                sumHeight += raw[distances[i].index].Height * weight;
            }

            return sumWeight > 0f ? sumHeight / sumWeight : raw[0].Height;
        }

        static float ApproximateHeight(System.Collections.Generic.List<CliffSlice> raw, Vector3 pos)
        {
            float min = float.MaxValue; float h = 1f;
            for (int i = 0; i < raw.Count; i++)
            {
                float d = Vector3.SqrMagnitude(raw[i].BottomPosition - pos);
                if (d < min) { min = d; h = raw[i].Height; }
            }
            return h;
        }

        private static float SampleSlope(Terrain t, Vector3 p)
        {
            if (TerrainUtils.TryGetHeightAndNormal(t, p, out var h, out var n))
            {
                return TerrainUtils.ComputeSlope(n);
            }
            return 0f;
        }

        public static System.Collections.Generic.List<FacadePath> ScanTerrainForFacades(
            Terrain terrain,
            Bounds scanBounds,
            MrTerrainPainter.Runtime.Profiles.VegetationItem item,
            float rdpEpsilon,
            float smoothSpacing)
        {
            var paths = new System.Collections.Generic.List<FacadePath>();
            if (terrain == null || item == null) return paths;
            var cfg = MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            var contours = ContourDetectionService.ScanContours(terrain, scanBounds, Mathf.Clamp(item.edgeSlopeThreshold, 0f, 90f));
            for (int ci = 0; ci < contours.Count; ci++)
            {
                var contour = contours[ci];
                if (contour.Points == null || contour.Points.Count < 5) continue;
                var rawSlices = ContourDetectionService.ConvertToSlices(contour, terrain);
                if (rawSlices == null || rawSlices.Count < 2) continue;

                // [改进方案] 使用弧长自适应采样替代RDP+样条插值
                var rebuilt = ArcLengthResample(rawSlices, Mathf.Max(0.01f, smoothSpacing));
                if (rebuilt == null || rebuilt.Count < 2) continue;

                if (rebuilt.Count > 1)
                {
                    float total = 0f;
                    for (int i = 0; i < rebuilt.Count - 1; i++) total += Vector3.Distance(rebuilt[i].BottomPosition, rebuilt[i + 1].BottomPosition);
                    paths.Add(new FacadePath { SourceTerrain = terrain, SmoothSlices = rebuilt, TotalLength = total });
                }
            }
            return paths;
        }

        static System.Collections.Generic.List<System.Collections.Generic.List<CliffSlice>> SplitIntoSegmentsInternal(System.Collections.Generic.List<CliffSlice> allSlices, float gapThreshold)
        {
            var segments = new System.Collections.Generic.List<System.Collections.Generic.List<CliffSlice>>();
            if (allSlices == null || allSlices.Count == 0) return segments;
            var current = new System.Collections.Generic.List<CliffSlice> { allSlices[0] };
            segments.Add(current);
            for (int i = 1; i < allSlices.Count; i++)
            {
                if (Vector3.Distance(allSlices[i].BottomPosition, allSlices[i - 1].BottomPosition) > gapThreshold)
                {
                    current = new System.Collections.Generic.List<CliffSlice> { allSlices[i] };
                    segments.Add(current);
                }
                else current.Add(allSlices[i]);
            }
            return segments;
        }
    }
}

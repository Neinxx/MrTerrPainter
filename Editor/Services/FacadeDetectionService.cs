using MrTerrainPainter.Editor.Utils;
using MrTerrainPainter.Runtime.Profiles;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class FacadeDetectionService
    {
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
            var slices = TraceVirtualFacade(
                terrain,
                center,
                radius * 2f,
                item.edgeSlopeEnter,
                item.edgeSlopeExit,
                item.probeStep,
                cfg != null ? cfg.facadeSmoothMode : MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode.Gaussian,
                cfg != null ? Mathf.Max(3, cfg.facadeSmoothWindow) : 5,
                cfg != null ? Mathf.Max(0.1f, cfg.facadeSmoothSigma) : 1f);
            slices = ApplyGlobalConstraints(slices, cfg != null ? cfg.minFacadeHeightMeters : 0.3f, true, cfg != null ? cfg.curveOffsetRightMeters : 0f, cfg != null ? cfg.curveOffsetOutMeters : 0f);
            float rendererWMinLen = MrTerrainPainter.Editor.Services.BrushPainter.GetPrefabHorizontalExtentMeters(item.prefab);
            float minLenSeg = Mathf.Max(rendererWMinLen, item.edgeReferenceWidthMeters);
            slices = FilterByMinimumWidth(slices, minLenSeg, Mathf.Max(item.CoreSpacing, 0.01f), 30f);
            if (slices != null && slices.Count > 3)
            {
                float eps = cfg != null ? Mathf.Max(0.01f, cfg.facadeRdpEpsilon) : 0.5f;
                var pts = new System.Collections.Generic.List<Vector3>(slices.Count);
                for (int i = 0; i < slices.Count; i++) pts.Add(slices[i].BottomPosition);
                var simple = MrTerrainPainter.Editor.Utils.GeometryUtils.SimplifyPathRDP(pts, eps);
                if (simple != null && simple.Count >= 2)
                {
                    var rebuilt = new System.Collections.Generic.List<CliffSlice>();
                    for (int i = 0; i < simple.Count - 1; i++)
                    {
                        Vector3 p0 = i > 0 ? simple[i - 1] : simple[i];
                        Vector3 p1 = simple[i];
                        Vector3 p2 = simple[i + 1];
                        Vector3 p3 = i < simple.Count - 2 ? simple[i + 2] : p2;
                        float seg = Vector3.Distance(p1, p2);
                        int steps = Mathf.Max(1, Mathf.CeilToInt(seg / Mathf.Max(item.CoreSpacing, 0.01f)));
                        for (int k = 0; k <= steps; k++)
                        {
                            float t = steps == 0 ? 0f : (k / (float)steps);
                            var pos = MrTerrainPainter.Editor.Utils.SplineUtils.GetPoint(p0, p1, p2, p3, t);
                            float lag = Mathf.Max(0.01f, item.CoreSpacing * 0.05f);
                            var ahead = MrTerrainPainter.Editor.Utils.SplineUtils.GetPoint(p0, p1, p2, p3, Mathf.Clamp01(t + lag));
                            var tan = ahead - pos; tan.y = 0f; if (tan.sqrMagnitude < 1e-6f) tan = p2 - p1; tan = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.right;
                            var up = Vector3.up;
                            var n = Vector3.Cross(tan, up).normalized;
                            float height = ApproximateHeight(slices, pos);
                            rebuilt.Add(new CliffSlice
                            {
                                BottomPosition = pos,
                                TopPosition = new Vector3(pos.x, pos.y + height, pos.z),
                                Normal = n,
                                Direction = up
                            });
                        }
                    }
                    slices = rebuilt;
                }
            }
            if (slices == null || slices.Count == 0) return;
            var grid = new FacadeGrid(Mathf.Max(item.CoreSpacing, 0.01f));
            for (int i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                if (!InsideBrush(s.BottomPosition, center, radius, shape)) continue;
                var p2 = new Vector2(s.BottomPosition.x - terrain.transform.position.x, s.BottomPosition.z - terrain.transform.position.z);
                float rendererW = MrTerrainPainter.Editor.Services.BrushPainter.GetPrefabHorizontalExtentMeters(item.prefab);
                float rendererH = MrTerrainPainter.Editor.Services.BrushPainter.GetPrefabHeightMeters(item.prefab);
                float minH = cfg != null ? Mathf.Max(0.0001f, cfg.minFacadeHeightMeters) : 0.0001f;
                float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), s.Height / Mathf.Max(0.0001f, rendererH));
                float spacingThresh = Mathf.Max(item.CoreSpacing, rendererW * uni);
                if (grid.HasNearby(p2, spacingThresh)) continue;
                grid.Add(p2);
                onPlace(s);
            }
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
            var slices = TraceVirtualFacade(
                terrain,
                scanBounds.center,
                scanBounds.size.x,
                item.edgeSlopeEnter,
                item.edgeSlopeExit,
                item.probeStep,
                cfg != null ? cfg.facadeSmoothMode : MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode.Gaussian,
                cfg != null ? Mathf.Max(3, cfg.facadeSmoothWindow) : 5,
                cfg != null ? Mathf.Max(0.1f, cfg.facadeSmoothSigma) : 1f);
            slices = ApplyGlobalConstraints(slices, cfg != null ? cfg.minFacadeHeightMeters : 0.3f, true, cfg != null ? cfg.curveOffsetRightMeters : 0f, cfg != null ? cfg.curveOffsetOutMeters : 0f);
            if (slices == null || slices.Count == 0) return paths;
            var segments = SplitIntoSegmentsInternal(slices, item.probeStep * 2f);
            for (int seg = 0; seg < segments.Count; seg++)
            {
                var raw = segments[seg];
                if (raw == null || raw.Count < 2) continue;
                var pts = new System.Collections.Generic.List<Vector3>(raw.Count);
                for (int i = 0; i < raw.Count; i++) pts.Add(raw[i].BottomPosition);
                var simple = MrTerrainPainter.Editor.Utils.GeometryUtils.SimplifyPathRDP(pts, Mathf.Max(0.01f, rdpEpsilon));
                if (simple == null || simple.Count < 2) continue;
                var rebuilt = new System.Collections.Generic.List<CliffSlice>();
                for (int i = 0; i < simple.Count - 1; i++)
                {
                    Vector3 p0 = i > 0 ? simple[i - 1] : simple[i];
                    Vector3 p1 = simple[i];
                    Vector3 p2 = simple[i + 1];
                    Vector3 p3 = i < simple.Count - 2 ? simple[i + 2] : p2;
                    float segLen = Vector3.Distance(p1, p2);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / Mathf.Max(0.01f, smoothSpacing)));
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = steps == 0 ? 0f : (k / (float)steps);
                        var pos = MrTerrainPainter.Editor.Utils.SplineUtils.GetPoint(p0, p1, p2, p3, t);
                        float lag = Mathf.Max(0.01f, smoothSpacing * 0.05f);
                        var ahead = MrTerrainPainter.Editor.Utils.SplineUtils.GetPoint(p0, p1, p2, p3, Mathf.Clamp01(t + lag));
                        var tan = ahead - pos; tan.y = 0f; if (tan.sqrMagnitude < 1e-6f) tan = p2 - p1; tan = tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.right;
                        var up = Vector3.up;
                        var n = Vector3.Cross(tan, up).normalized;
                        float height = ApproximateHeight(raw, pos);
                        rebuilt.Add(new CliffSlice
                        {
                            BottomPosition = pos,
                            TopPosition = new Vector3(pos.x, pos.y + height, pos.z),
                            Normal = n,
                            Direction = up
                        });
                    }
                }
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

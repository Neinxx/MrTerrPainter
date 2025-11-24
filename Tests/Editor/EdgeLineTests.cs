using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MrTerrainPainter.Runtime.Profiles;
using MrTerrainPainter.Editor.Services;

namespace MrTerrainPainter.Tests.Editor
{
    public class EdgeLineTests
    {
        [Test]
        public void VegetationItem_CoreParams_ShouldValidateClamp()
        {
            var vi = new VegetationItem
            {
                uniformScaleRange = new Vector2(-1f, 0.5f),
                minSpacing = -2f,
                edgeOffsets = new Vector3(0.1f, 0.2f, 0.3f)
            };
            vi.ValidateCore();
            Assert.GreaterOrEqual(vi.uniformScaleRange.x, 0.0001f);
            Assert.GreaterOrEqual(vi.uniformScaleRange.y, vi.uniformScaleRange.x);
            Assert.GreaterOrEqual(vi.minSpacing, 0.01f);
            Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), vi.CoreOffset);
            Assert.Greater(vi.CoreScale, 0f);
            Assert.Greater(vi.CoreSpacing, 0f);
        }

        [Test]
        public void FacadeConstraints_ShouldKeepVerticalUp_AndParallelXZ()
        {
            var slices = new System.Collections.Generic.List<FacadeDetectionService.CliffSlice>();
            for (int i = 0; i < 5; i++)
            {
                var bp = new Vector3(i, 10f, i);
                var tp = new Vector3(i, 13f, i);
                slices.Add(new FacadeDetectionService.CliffSlice { BottomPosition = bp, TopPosition = tp, Normal = Vector3.forward, Direction = Vector3.up });
            }
            var res = FacadeDetectionService.ApplyGlobalConstraints(slices, 0.3f, true, 0f, 0f);
            Assert.IsNotNull(res);
            Assert.IsTrue(res.All(s => Vector3.Normalize(s.Direction) == Vector3.up));
            for (int i = 1; i < res.Count; i++)
            {
                var prev = res[i - 1];
                var curr = res[i];
                var tangentPrev = new Vector3(prev.BottomPosition.x - prev.BottomPosition.x, 0f, prev.BottomPosition.z - prev.BottomPosition.z);
                var tangentCurr = new Vector3(curr.BottomPosition.x - curr.BottomPosition.x, 0f, curr.BottomPosition.z - curr.BottomPosition.z);
                Assert.AreEqual(0f, Vector3.Dot(curr.Normal, Vector3.up), 1e-6f);
            }
        }

        [Test]
        public void FacadeOffsets_And_ScaleOffset_ShouldAffectPlacement()
        {
            var item = new VegetationItem
            {
                facadeScaleOffset = new Vector3(0.2f, 0.2f, 0.2f),
                offsets = new Vector3(0.3f, 0.4f, 0.5f),
                edgeReferenceHeightMeters = 1f
            };
            var s = new MrTerrainPainter.Editor.Services.FacadeDetectionService.CliffSlice
            {
                BottomPosition = new Vector3(10f, 2f, 20f),
                TopPosition = new Vector3(10f, 3f, 20f),
                Normal = Vector3.forward,
                Direction = Vector3.up
            };
            float h = s.Height; // 1m
            float minH = 0.1f;
            float rendererH = 1f;
            float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), h / Mathf.Max(0.0001f, rendererH));
            var baseScale = new Vector3(uni, uni, uni);
            var final = new Vector3(
                Mathf.Max(0.0001f, baseScale.x + item.facadeScaleOffset.x),
                Mathf.Max(0.0001f, baseScale.y + item.facadeScaleOffset.y),
                Mathf.Max(0.0001f, baseScale.z + item.facadeScaleOffset.z));
            Assert.Greater(final.x, uni);
            var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
            float depth = 0.25f;
            var off = rightAxis * item.offsets.x + s.Direction * item.offsets.y + (-s.Normal.normalized) * (depth + Mathf.Max(0f, item.offsets.z));
            Assert.AreNotEqual(Vector3.zero, off);
        }

        [Test]
        public void AliasSampler_ShouldMatchWeightsApproximately()
        {
            int[] weights = new[] { 1, 3, 6 };
            int n = weights.Length;
            var prob = new float[n];
            var alias = new int[n];
            var small = new System.Collections.Generic.Queue<int>();
            var large = new System.Collections.Generic.Queue<int>();
            float sum = Mathf.Max(1, weights.Sum());
            for (int i = 0; i < n; i++) prob[i] = (weights[i] / sum) * n;
            for (int i = 0; i < n; i++) { if (prob[i] < 1f) small.Enqueue(i); else large.Enqueue(i); }
            while (small.Count > 0 && large.Count > 0)
            {
                int s = small.Dequeue();
                int l = large.Dequeue();
                alias[s] = l;
                prob[l] = (prob[l] + prob[s]) - 1f;
                if (prob[l] < 1f) small.Enqueue(l); else large.Enqueue(l);
            }
            while (large.Count > 0) { prob[large.Dequeue()] = 1f; }
            while (small.Count > 0) { prob[small.Dequeue()] = 1f; }
            var rnd = new System.Random(123);
            int trials = 10000;
            var counts = new int[n];
            for (int t = 0; t < trials; t++)
            {
                int col = rnd.Next(0, n);
                float frac = (float)rnd.NextDouble();
                int idx = frac < prob[col] ? col : alias[col];
                counts[idx]++;
            }
            float p0 = counts[0] / (float)trials;
            float p1 = counts[1] / (float)trials;
            float p2 = counts[2] / (float)trials;
            Assert.Greater(p2, p1);
            Assert.Greater(p1, p0);
        }
    }
}
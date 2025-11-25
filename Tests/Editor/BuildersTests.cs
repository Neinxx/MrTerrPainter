using NUnit.Framework;
using UnityEngine;
using MrTerrainPainter.Editor.Services;

namespace MrTerrainPainter.Tests.Editor
{
    public class BuildersTests
    {
        [Test]
        public void CandidateBuilder_BuildsEquivalentCandidates()
        {
            var center = new Vector2(10f, 20f);
            float radius = 5f;
            int desired = 50;
            float minSpacing = 0.5f;
            float jitter = 0.2f;
            int seed = 123;
            var rnd = new System.Random(seed);

            var req = new VegetationGenerator.CandidateBuilder()
                .Center(center)
                .Radius(radius)
                .Shape(BrushShape.Circle)
                .Desired(desired)
                .MinSpacing(minSpacing)
                .Jitter(jitter)
                .Seed(seed)
                .Distribution(DistributionType.Uniform)
                .UseBurst(false)
                .Random(rnd)
                .Build();

            var viaBuilder = VegetationGenerator.BuildCandidates(req);
            Assert.IsNotNull(viaBuilder);
            Assert.Greater(viaBuilder.Count, 0);
        }

        [Test]
        public void FilterByMinimumWidth_RespectsMinimumLength()
        {
            var slices = new System.Collections.Generic.List<FacadeDetectionService.CliffSlice>();
            for (int i = 0; i < 10; i++)
            {
                var p = new Vector3(i * 0.5f, 0f, 0f);
                slices.Add(new FacadeDetectionService.CliffSlice { BottomPosition = p, TopPosition = p + Vector3.up, Normal = Vector3.forward, Direction = Vector3.up });
            }
            float minLen = 2.0f;
            float spacing = 0.5f;
            float maxAng = 45f;
            var res = FacadeDetectionService.FilterByMinimumWidth(slices, minLen, spacing, maxAng);
            Assert.IsNotNull(res);
            Assert.Greater(res.Count, 0);
            var res2 = FacadeDetectionService.FilterByMinimumWidth(slices, 100f, spacing, maxAng);
            Assert.IsNotNull(res2);
            Assert.AreEqual(0, res2.Count);
        }
    }
}

using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public interface IFilterStrategy
    {
        VegetationGenerator.FilterSettings BuildFilter();
    }

    public interface IPlacementOverrideStrategy
    {
        VegetationGenerator.PlacementOverrides BuildOverrides();
    }

    public class DefaultFilterStrategy : IFilterStrategy
    {
        private readonly VegetationGenerator.NoiseSettings noise;
        public DefaultFilterStrategy(VegetationGenerator.NoiseSettings noise) { this.noise = noise; }
        public VegetationGenerator.FilterSettings BuildFilter()
        {
            return new VegetationGenerator.FilterSettings { noise = noise ?? new VegetationGenerator.NoiseSettings() };
        }
    }

    public class DefaultPlacementOverrideStrategy : IPlacementOverrideStrategy
    {
        private readonly System.Func<Vector2> getScale;
        private readonly System.Func<Vector2> getYRot;
        private readonly System.Func<Vector2> getHeight;
        private readonly System.Func<Vector2> getSlope;
        public DefaultPlacementOverrideStrategy(System.Func<Vector2> scale, System.Func<Vector2> yrot, System.Func<Vector2> height, System.Func<Vector2> slope)
        {
            getScale = scale; getYRot = yrot; getHeight = height; getSlope = slope;
        }
        public VegetationGenerator.PlacementOverrides BuildOverrides()
        {
            return new VegetationGenerator.PlacementOverrides
            {
                scaleRange = getScale != null ? getScale() : Vector2.one,
                yRotationRange = getYRot != null ? getYRot() : new Vector2(0f, 30f),
                heightRange = getHeight != null ? getHeight() : new Vector2(0f, 1000f),
                slopeRange = getSlope != null ? getSlope() : new Vector2(0f, 90f)
            };
        }
    }
}

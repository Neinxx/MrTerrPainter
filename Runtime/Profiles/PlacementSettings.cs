using System;
using UnityEngine;

namespace MrTerrainPainter.Runtime.Profiles
{
    public interface IPlacementSettings { }

    [Serializable]
    public class StandardPlacementSettings : IPlacementSettings
    {
        public Vector2 heightRange = new Vector2(0f, 1000f);
        public Vector2 slopeRange = new Vector2(0f, 90f);
        public bool alignToTerrainNormal = true;
    }

    [Serializable]
    public class FacadeDetectionSettings
    {
        public float edgeSlopeEnter = 30f;
        public float edgeSlopeExit = 25f;
        public float probeStep = 0.5f;
        public float probeMaxDist = 6f;
        public float referenceHeightMeters = 1f;
        public Vector3 offsets = Vector3.zero;
        public FacadeSmoothingMode facadeSmoothingMode = FacadeSmoothingMode.Mean;
        public int facadeSmoothingWindow = 3;
        public float facadeSmoothingSigma = 1f;
    }

    [Serializable]
    public class LandscapePlacementSettings : IPlacementSettings
    {
        public float edgeSlopeThreshold = 75f;
        public Vector2 embedDepthRange = new Vector2(0.1f, 0.3f);
        public float edgeReferenceWidthMeters = 1f;
        public bool edgeAutoHeight = true;
        public float edgeReferenceHeightMeters = 1f;
        public Vector3 edgeOffsets = Vector3.zero;

        public bool edgeStacking = false;
        public float edgeStackingOffsetMeters = 0f;
        public float edgeTopScaleBias = 1f;
        public Vector3 facadeScaleOffset = Vector3.zero;

        public FacadeDetectionSettings facade = new FacadeDetectionSettings();
    }
}

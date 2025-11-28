using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class FacadePathSmoother
    {
        public static List<FacadeDetectionService.CliffSlice> ApplyGlobalConstraints(List<FacadeDetectionService.CliffSlice> slices, float minHeight, bool clampTop, float rdpeps, float smoothSigma)
        {
            return FacadeDetectionService.ApplyGlobalConstraints(slices, minHeight, clampTop, rdpeps, smoothSigma);
        }

        public static List<FacadeDetectionService.CliffSlice> ResampleSmooth(List<FacadeDetectionService.CliffSlice> slices, float spacing)
        {
            return MrTerrainPainter.Editor.Utils.SplineUtils.ResampleSlicesSmoothly(slices, spacing);
        }
    }
}

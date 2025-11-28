using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class FacadeDetector
    {
        public static bool TryDetect(Terrain terrain, Vector3 pos, float enterSlope, float exitSlope, float step, float maxDist, out FacadeDetectionService.FacadeInfo info)
        {
            return FacadeDetectionService.TryDetectFacade(terrain, pos, enterSlope, exitSlope, step, maxDist, out info);
        }
    }
}

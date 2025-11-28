using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Runtime.Profiles;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public struct FacadeTraceRequest
    {
        public Terrain Terrain;
        public Vector3 Start;
        public float Length;
        public VegetationItem ItemRef;
        public MrTerrainPainterConfig Config;
        public BrushSettings Brush;
    }
}

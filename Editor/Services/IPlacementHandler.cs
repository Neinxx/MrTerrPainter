using MrTerrainPainter.Runtime.Profiles;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public interface IPlacementHandler
    {
        bool CanHandle(VegetationItem item, BrushSettings bs);
        void Paint(PaintContext context);
    }
}

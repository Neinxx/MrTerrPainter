using MrTerrainPainter.Editor.Services;

namespace MrTerrainPainter.Editor.Tools
{
    public static class MTPBrushContext
    {
        private static BrushSettings _brush;
        public static BrushSettings Brush
        {
            get
            {
                if (_brush == null) _brush = new BrushSettings();
                return _brush;
            }
        }
    }
}

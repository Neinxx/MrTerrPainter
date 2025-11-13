using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Views.Tabs
{
    public class PaintingTabView
    {
        private readonly MrTerrainPainterWindow window;
        private readonly VisualElement paintRoot;

        public PaintingTabView(MrTerrainPainterWindow window, VisualElement paintRoot)
        {
            this.window = window;
            this.paintRoot = paintRoot;
        }

        public void Setup()
        {
            var paintParam = paintRoot.Q<VisualElement>("PaintParameter") ?? paintRoot;
            window.BindBrushControls(paintParam);
            window.BindContralNamedControls();
        }
    }
}

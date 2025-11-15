using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Utils
{
    public static class PageAssembler
    {
        public static bool EnsureStylesAndValidate(Config.MrTerrainPainterConfig cfg, VisualElement root, out string reason)
        {
            reason = string.Empty;
            var styleSheet = Config.ConfigTools.GetStylesUss(cfg);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            return true;
        }

        public static VisualElement Assemble(VisualElement container, VisualTreeAsset vta)
        {
            if (container == null) return null;
            VisualElement elem;
            if (vta != null)
            {
                elem = vta.Instantiate();
            }
            else
            {
                elem = new VisualElement();
                elem.Add(new Label("未找到UXML文件"));
            }
            container.Add(elem);
            return elem;
        }
    }
}

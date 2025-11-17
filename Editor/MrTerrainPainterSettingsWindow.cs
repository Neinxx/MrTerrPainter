using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Views.Tabs;
using MrTerrainPainter.Editor.Utils;

namespace MrTerrainPainter.Editor
{
    public class MrTerrainPainterSettingsWindow : EditorWindow
    {
        private MrTerrainPainterConfig config;

        public static void Open()
        {
            var wnd = GetWindow<MrTerrainPainterSettingsWindow>(true, "Mr Terrain Painter Settings", true);
            wnd.minSize = new Vector2(640, 480);
            wnd.Show();
            wnd.BuildUI();
        }

        private void OnEnable()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            config = ConfigTools.LoadOrCreateAsset();
            rootVisualElement.Clear();
            var settingsUxml = ConfigTools.GetSettingsUxml();
            var page = PageAssembler.Assemble(rootVisualElement, settingsUxml);
            var view = new SettingsTabView(config, page);
            view.Setup();
        }
    }
}

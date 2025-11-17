using UnityEditor;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor
{
    public class MTPSettingsProvider : SettingsProvider
    {
        public MTPSettingsProvider(string path, SettingsScope scopes) : base(path, scopes) {}

        public override void OnActivate(string searchContext, VisualElement root)
        {
            var vt = Config.ConfigTools.GetSettingsUxml();
            if (vt != null) root.Add(vt.Instantiate());
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new MTPSettingsProvider("Project/Mr Terrain Painter", SettingsScope.Project);
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Tools
{
    public static class UxmlRenamer
    {
        [MenuItem("Tools/MTP/Rename UXML To Canonical Names")]
        public static void Rename()
        {
            var map = new Dictionary<string, string>
            {
                {"MrTerrainPainterWindowStart", "MrTerrainPainterWindow.Start"},
                {"MrTerrainPainterWindowControl", "MrTerrainPainterWindow.Control"},
                {"MrTerrainPainterWindowPaintPage", "MrTerrainPainterWindow.Paint"},
                {"MrTerrainPainterWindowGenerate", "MrTerrainPainterWindow.Generate"},
                {"MrTerrainPainterVegetationShared", "MrTerrainPainterWindow.VegetationShared"},
                {"VegetationShared", "MrTerrainPainterWindow.VegetationShared"},
                {"VegetationProfileRow", "MrTerrainPainterWindow.VegetationProfileRow"},
                {"MrTerrainPainterVegetationProfileRow", "MrTerrainPainterWindow.VegetationProfileRow"},
                {"VegetationProfilePrefabIcon", "MrTerrainPainterWindow.PrefabIcon"},
                {"PrefabIcon", "MrTerrainPainterWindow.PrefabIcon"},
                {"MrTerrainPainterWindowVegetationProfileDraggableArea", "MrTerrainPainterWindow.VegetationProfileDraggableArea"},
                {"VegetationProfileDraggableArea", "MrTerrainPainterWindow.VegetationProfileDraggableArea"},
                {"MTPBrushOverlay", "MTPBrushOverlay"},
                {"MrTerrainPainterBrushOverlay", "MTPBrushOverlay"},
                {"MrTerrainPainterSettings", "MrTerrainPainter.Settings"},
                {"MTPSettings", "MrTerrainPainter.Settings"},
                {"MTPTerrainPainterSettingsMappinger", "MrTerrainPainter.Settings.Mapping"},
                {"MrTerrainPainterSettingsMapping", "MrTerrainPainter.Settings.Mapping"}
            };

            int renamed = 0;
            foreach (var kv in map)
            {
                var guids = AssetDatabase.FindAssets("t:VisualTreeAsset name:" + kv.Key);
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var currentName = System.IO.Path.GetFileNameWithoutExtension(path);
                    var targetName = kv.Value;
                    if (currentName == targetName) continue;
                    var result = AssetDatabase.RenameAsset(path, targetName);
                    if (string.IsNullOrEmpty(result)) renamed++;
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.EditorTools;
using UnityEditor.Callbacks;
using UnityEngine;
using MrTerrainPainter.Editor.Tools;
using MrTerrainPainter.Runtime.Profiles;

namespace MrTerrainPainter.Editor
{
    public static class MTPEntryPoints
    {
        [Shortcut("MTP/Open Window", KeyCode.M, ShortcutModifiers.Alt)]
        public static void ShortcutOpenWindow() => MrTerrainPainterWindow.GetOrOpen();

        [Shortcut("MTP/Toggle Brush Tool", KeyCode.B, ShortcutModifiers.Alt)]
        public static void ToggleBrushTool()
        {
            if (Selection.activeGameObject?.GetComponent<Terrain>() == null) return;

            var toolType = typeof(MTPBrushTool);
            if (ToolManager.activeToolType == toolType)
                ToolManager.RestorePreviousTool();
            else
                ToolManager.SetActiveTool(toolType);
        }

        [MenuItem("CONTEXT/Terrain/Mr Terrain Painter/Open Window")]
        private static void ContextOpenWindow(MenuCommand _) => MrTerrainPainterWindow.GetOrOpen();

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int _)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is not VegetationProfile) return false;

            var win = MrTerrainPainterWindow.GetOrOpen();
            win?.Focus();
            // 延迟确保 UI 加载完毕后跳转
            EditorApplication.delayCall += () => win?.OpenPaintingSettings();
            return true;
        }
    }
}
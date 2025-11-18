
using MrTerrainPainter.Editor.Services;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using MrTerrainPainter.Editor.Controllers;

namespace MrTerrainPainter.Editor.Tools
{
    [EditorTool("Mr Terrain Brush", typeof(Terrain))]
    public class MTPBrushTool : EditorTool
    {
        private System.Random rnd;
        private int lastSeed;
        private SceneInteractionService sceneService;
        private TerrainController terrainController = new TerrainController();
        private PaintingController paintingController = new PaintingController();
        public override void OnActivated()
        {
            if (sceneService != null) return;
            var filter = new DefaultFilterStrategy(new VegetationGenerator.NoiseSettings());
            var placement = new DefaultPlacementOverrideStrategy(
                () => Vector2.one,
                () => new Vector2(0f, 30f),
                () => new Vector2(0f, 1000f),
                () => new Vector2(0f, 90f)
            );
            sceneService = new SceneInteractionService(
                terrainController,
                paintingController,
                () => MTPBrushContext.CurrentProfile,
                () => new System.Collections.Generic.List<Terrain>(terrainController.GetSelectedTerrains()),
                MTPBrushContext.Brush,
                filter,
                placement,
                () => false,
                () => true,
                () => MrTerrainPainter.Editor.Utils.EditorSceneUtils.MarkSceneDirty(),
                pos =>
                {
                    if (terrainController.TryFindNearestTerrain(pos, out var nearest)) return nearest;
                    return null;
                },
                () =>
                {
                    var p = MTPBrushContext.CurrentProfile;
                    var seed = p != null ? p.randomSeed : 12345;
                    if (rnd == null || lastSeed != seed) { rnd = new System.Random(seed); lastSeed = seed; }
                    return rnd;
                },
                true
            );
        }

        public override void OnToolGUI(EditorWindow window)
        {
            var sceneView = window as SceneView;
            if (sceneView == null) return;
            if (sceneService == null) OnActivated();
            sceneService.OnSceneGUI();
        }


    }

    public static class MTPBrushShortcuts
    {
        [Shortcut("MTP/Brush/Increase Size", KeyCode.RightBracket)]
        public static void IncreaseSize()
        {
            var b = MTPBrushContext.Brush;
            b.size = Mathf.Min(100f, b.size + 0.5f);
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.Repaint(); else SceneView.RepaintAll();
        }

        [Shortcut("MTP/Brush/Decrease Size", KeyCode.LeftBracket)]
        public static void DecreaseSize()
        {
            var b = MTPBrushContext.Brush;
            b.size = Mathf.Max(0.5f, b.size - 0.5f);
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.Repaint(); else SceneView.RepaintAll();
        }
    }
}

using System.Linq;
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
        private SceneInteractionService sceneService;
        private TerrainController terrainController = new TerrainController();
        private PaintingController paintingController = new PaintingController();
        public override void OnActivated()
        {

        }

        public override void OnToolGUI(EditorWindow window)
        {
            var sceneView = window as SceneView;
            if (sceneView == null) return;
            if (sceneService == null)
            {
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
                    () =>
                    {
                        var t = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Terrain>() : null;
                        var list = new System.Collections.Generic.List<Terrain>();
                        if (t != null) list.Add(t);
                        return list;
                    },
                    MTPBrushContext.Brush,
                    filter,
                    placement,
                    () => false,
                    () => true,
                    () => MarkSceneDirty(),
                    pos =>
                    {
                        var active = Terrain.activeTerrains;
                        if (active == null || active.Length == 0) return null;
                        float best = float.MaxValue; Terrain bestT = null;
                        for (int i = 0; i < active.Length; i++)
                        {
                            var t = active[i];
                            var d = Vector3.Distance(t.transform.position, pos);
                            if (d < best) { best = d; bestT = t; }
                        }
                        return bestT;
                    },
                    () => { if (rnd == null) rnd = new System.Random(); return rnd; },
                    true
                );
            }
            sceneService.OnSceneGUI();
        }

        private static void MarkSceneDirty()
        {
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }
    }

    public static class MTPBrushShortcuts
    {
        [Shortcut("MTP/Brush/Increase Size", KeyCode.RightBracket)]
        public static void IncreaseSize()
        {
            var b = MTPBrushContext.Brush;
            b.size = Mathf.Min(100f, b.size + 0.5f);
        }

        [Shortcut("MTP/Brush/Decrease Size", KeyCode.LeftBracket)]
        public static void DecreaseSize()
        {
            var b = MTPBrushContext.Brush;
            b.size = Mathf.Max(0.5f, b.size - 0.5f);
        }
    }
}

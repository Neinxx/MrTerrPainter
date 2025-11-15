using System.Linq;
using MrTerrainPainter.Editor.Services;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace MrTerrainPainter.Editor.Tools
{
    [EditorTool("Mr Terrain Brush")]
    public class MTPBrushTool : EditorTool
    {
        private System.Random rnd;
        private Vector3 lastPos;
        private Vector3 lastNormal = Vector3.up;
        public override void OnActivated()
        {
            if (!MrTerrainPainter.Editor.MrTerrainPainterWindow.TryGet(out var _))
            {
                MrTerrainPainter.Editor.MrTerrainPainterWindow.GetOrOpen();
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            var sceneView = window as SceneView;
            if (sceneView == null) return;
            var e = Event.current;
            var brush = MTPBrushContext.Brush;

            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Terrain hitTerrain;
            Vector3 hitPos;
            Vector3 hitNormal;
            if (TryGetTerrainHit(ray, out hitTerrain, out hitPos, out hitNormal))
            {
                lastPos = hitPos;
                lastNormal = hitNormal;
            }

            if (e.type == EventType.Repaint)
            {
                if (lastPos != Vector3.zero)
                {
                    BrushPainter.DrawPreview(lastPos, lastNormal, brush);
                }
            }

            if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            {
                if (hitTerrain != null)
                {
                    if (rnd == null) rnd = new System.Random();
                    if (e.button == 1)
                    {
                        BrushPainter.Erase(hitTerrain, lastPos, brush, true);
                        e.Use();
                    }
                    else if (e.button == 0)
                    {
                        MrTerrainPainter.Editor.MrTerrainPainterWindow.TryGet(out var win);
                        var profile = win != null ? win.GetCurrentProfile() : null;
                        if (profile != null)
                        {
                            var cfg = MrTerrainPainter.Editor.Config.ConfigTools.LoadOrCreateAsset();
                            var mapping = MrTerrainPainter.Editor.Config.ConfigTools.BuildTypeMapping(cfg);
                            BrushPainter.Paint(hitTerrain, profile, lastPos, brush, rnd, mapping);
                        }
                        e.Use();
                    }
                }
            }
        }

        private static bool TryGetTerrainHit(Ray ray, out Terrain terrain, out Vector3 pos, out Vector3 normal)
        {
            terrain = null;
            pos = Vector3.zero;
            normal = Vector3.up;
            float best = float.MaxValue;
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                var col = t.GetComponent<TerrainCollider>();
                if (col != null)
                {
                    if (col.Raycast(ray, out var hit, 10000f))
                    {
                        if (hit.distance < best)
                        {
                            best = hit.distance;
                            terrain = t;
                            pos = hit.point;
                            if (Editor.Utils.TerrainUtils.TryGetHeightAndNormal(terrain, pos, out var h, out var n))
                            {
                                pos.y = h;
                                normal = n;
                            }
                        }
                    }
                }
            }
            return terrain != null;
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

using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Controllers;
using MrTerrainPainter.Editor.Tools;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using PrefabType = MrTerrainPainter.Runtime.Profiles.PrefabType;

namespace MrTerrainPainter.Editor.Services
{
    public class SceneInteractionService
    {
        private readonly TerrainController terrainController;
        private readonly PaintingController paintingController;
        private readonly System.Func<VegetationProfile> getCurrentProfile;
        private readonly System.Func<List<Terrain>> getSelectedTerrains;
        private BrushSettings brush;
        private readonly IFilterStrategy filterStrategy;
        private readonly IPlacementOverrideStrategy placementStrategy;
        private readonly System.Func<bool> isGenerateMode;
        private readonly System.Func<bool> isPaintMode;
        private readonly System.Action markSceneDirty;
        private readonly System.Func<Vector3, Terrain> nearestTerrain;
        private readonly System.Func<System.Random> getRandom;
        private readonly bool allowWhenBrushToolActive;
        private Vector3 _lastPaintPos;
        private bool _hasLastPaintPos;

        public SceneInteractionService(
            TerrainController terrainController,
            PaintingController paintingController,
            System.Func<VegetationProfile> getCurrentProfile,
            System.Func<List<Terrain>> getSelectedTerrains,
            BrushSettings brush,
            IFilterStrategy filterStrategy,
            IPlacementOverrideStrategy placementStrategy,
            System.Func<bool> isGenerateMode,
            System.Func<bool> isPaintMode,
            System.Action markSceneDirty,
            System.Func<Vector3, Terrain> nearestTerrain,
            System.Func<System.Random> getRandom,
            bool allowWhenBrushToolActive = false)
        {
            this.terrainController = terrainController;
            this.paintingController = paintingController;
            this.getCurrentProfile = getCurrentProfile;
            this.getSelectedTerrains = getSelectedTerrains;
            this.brush = brush;
            this.filterStrategy = filterStrategy;
            this.placementStrategy = placementStrategy;
            this.isGenerateMode = isGenerateMode;
            this.isPaintMode = isPaintMode;
            this.markSceneDirty = markSceneDirty;
            this.nearestTerrain = nearestTerrain;
            this.getRandom = getRandom;
            this.allowWhenBrushToolActive = allowWhenBrushToolActive;
            MrTerrainPainter.Editor.Tools.MTPBrushContext.BrushReplaced += OnBrushReplaced;
        }

        private void OnBrushReplaced()
        {
            brush = MrTerrainPainter.Editor.Tools.MTPBrushContext.Brush;
        }

        public void OnSceneGUI()
        {
            var e = Event.current;
            if (e != null && (e.type == EventType.MouseLeaveWindow))
            {
                _hasLastPaintPos = false;
            }
            if (!allowWhenBrushToolActive && UnityEditor.EditorTools.ToolManager.activeToolType == typeof(Tools.MTPBrushTool)) return;
            HandleLayoutControl(e);
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Terrain hitTerrain = null;
            Vector3 hitPos = Vector3.zero;
            Vector3 hitNormal = Vector3.up;
            bool hasHit = terrainController != null && terrainController.TryGetTerrainHit(ray, out hitTerrain, out hitPos, out hitNormal);
            RenderBrushPreview(hasHit, hitPos, hitNormal, e);

            // 处理鼠标按下和拖拽事件
            if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            {
                if (!hasHit) return;
                if (isPaintMode()) { HandlePaintMouse(e, hitTerrain, hitPos); return; }
            }
            else if (e.type == EventType.MouseUp)
            {
                _hasLastPaintPos = false;
            }
        }

        private void HandleLayoutControl(Event e)
        {
            // 仅在绘画模式下处理布局控制，不考虑生成模式的修饰键
            if (isPaintMode())
            {
                if (e.type == EventType.Layout)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                }
            }
        }

        private void RenderBrushPreview(bool hasHit, Vector3 hitPos, Vector3 hitNormal, Event e)
        {
            if (e.type != EventType.Repaint) return;
            if (!hasHit || !isPaintMode()) return;
            BrushPainter.DrawPreview(hitPos, hitNormal, brush);
        }

        // 移除Generate模式的处理逻辑，确保只有绘画模式生效
        private void HandlePaintMouse(Event e, Terrain hitTerrain, Vector3 hitPos)
        {
            Terrain terrain = hitTerrain;
            if (terrain == null && terrainController != null)
            {
                if (terrainController.TryFindNearestTerrain(hitPos, out var nearest)) terrain = nearest;
            }
            if (terrain == null) return;

            // 严格判断：只有右键（button == 1）且无修饰键时擦除
            if (e.button == 1 && !e.shift && !e.control && !e.alt)
            {
                BrushPainter.Erase(terrain, hitPos, brush, true);
                markSceneDirty?.Invoke();
                e.Use();
            }
            // 严格判断：只有左键（button == 0）且无修饰键时绘制
            else if (e.button == 0 && !e.shift && !e.control && !e.alt)
            {
                float factor = Mathf.Max(0f, brush.strokeSpacingFactor);
                float spacing = brush.useAbsoluteStrokeSpacing ? brush.strokeSpacingAbsolute : brush.size * factor;
                if (spacing <= 0f || !_hasLastPaintPos)
                {
                    VegetationPainterOnTerrain(terrain, hitPos);
                    _lastPaintPos = hitPos;
                    _hasLastPaintPos = true;
                    e.Use();
                    return;
                }
                float threshold = Mathf.Max(0.01f, spacing);
                if ((hitPos - _lastPaintPos).sqrMagnitude >= threshold * threshold)
                {
                    // FacadeStone+EdgeLine 检测失败阻止绘制
                    var profile = getCurrentProfile?.Invoke();
                    bool isFacadeEdgeMode = brush != null && brush.distribution == DistributionType.EdgeLine && profile != null && profile.Items.Any(it => it != null && it.prefabType == PrefabType.Landscape);
                    if (isFacadeEdgeMode)
                    {
                        var item = profile.Items.FirstOrDefault(it => it != null && it.prefabType == PrefabType.Landscape);
                        if (item != null)
                        {
                            if (!FacadeDetectionService.TryDetectFacade(terrain, hitPos, item.edgeSlopeEnter, item.edgeSlopeExit, item.probeStep, item.probeMaxDist, out var _))
                            {
                                Handles.Label(hitPos + Vector3.up * 0.2f, "未检测到立面（坡度不足或探测范围不足）");
                                return;
                            }
                        }
                    }
                    VegetationPainterOnTerrain(terrain, hitPos);
                    _lastPaintPos = hitPos;
                }
                e.Use();
            }
        }

        private void VegetationPainterOnTerrain(Terrain terrain, Vector3 center)
        {
            var profile = getCurrentProfile?.Invoke();
            if (terrain == null || profile == null || profile.IsEmpty()) return;
            var ov = placementStrategy.BuildOverrides();
            var extras = new List<VegetationProfile>(MTPBrushContext.ExtraProfiles as IEnumerable<VegetationProfile>);
            paintingController?.PaintOnTerrain(terrain, center, profile, extras, brush, getRandom(), ov, brush.mixExtraProfiles);
            markSceneDirty?.Invoke();
        }
    }
}

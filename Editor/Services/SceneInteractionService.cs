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

            // 只处理鼠标按下和拖拽事件，不考虑修饰键
            if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            {
                if (!hasHit) return;

                // 仅在绘画模式下处理，忽略生成模式的修饰键逻辑
                if (isPaintMode())
                {
                    HandlePaintMouse(e, hitTerrain, hitPos);
                    return;
                }
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

            var terrain = nearestTerrain?.Invoke(hitPos);
            var profile = getCurrentProfile?.Invoke();
            bool isFacadeEdgeMode = brush != null && brush.distribution == DistributionType.EdgeLine && profile != null && profile.Items.Any(it => it != null && it.prefabType == PrefabType.Landscape);

            if (isFacadeEdgeMode && terrain != null)
            {
                var item = profile.Items.FirstOrDefault(it => it != null && it.prefabType == PrefabType.Landscape);
                if (item != null)
                {
                    if (FacadeDetectionService.TryDetectFacade(terrain, hitPos, item.edgeSlopeEnter, item.edgeSlopeExit, item.probeStep, item.probeMaxDist, out var info))
                    {
                        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                        Handles.color = new Color(1f, 0.3f, 0.3f, 0.95f);
                        Handles.SphereHandleCap(0, info.topPos, Quaternion.identity, 0.3f, EventType.Repaint);
                        Handles.color = new Color(0.3f, 1f, 0.3f, 0.95f);
                        Handles.SphereHandleCap(0, info.bottomPos, Quaternion.identity, 0.3f, EventType.Repaint);
                        Handles.color = Color.white;
                        var tip = info.bottomPos + info.forward.normalized * Mathf.Max(brush.size * 0.6f, 0.5f);
                        Handles.DrawAAPolyLine(4f, new Vector3[] { info.bottomPos, tip });
                        Handles.color = new Color(0.2f, 1f, 1f, 0.9f);
                        float length = brush.size * 2f;
                        float step = Mathf.Max(item.minSpacing, 0.01f);
                        for (float u = -length * 0.5f; u <= length * 0.5f + 0.0001f; u += step)
                        {
                            var p = info.bottomPos + info.right * u;
                            var q = p + info.forward * 0.6f;
                            Handles.DrawAAPolyLine(2f, new Vector3[] { p, q });
                        }
                        BrushPainter.DrawPreview(hitPos, hitNormal, brush);
                        return;
                    }
                    else
                    {
                        Handles.Label(hitPos + Vector3.up * 0.2f, "未检测到立面（坡度不足或探测范围不足）");
                        return;
                    }
                }
            }

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

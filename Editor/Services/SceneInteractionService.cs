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
        public class Options
        {
            public TerrainController terrainController;
            public PaintingController paintingController;
            public System.Func<VegetationProfile> getCurrentProfile;
            public System.Func<System.Collections.Generic.List<Terrain>> getSelectedTerrains;
            public BrushSettings brush;
            public IFilterStrategy filterStrategy;
            public IPlacementOverrideStrategy placementStrategy;
            public System.Func<bool> isGenerateMode;
            public System.Func<bool> isPaintMode;
            public System.Action markSceneDirty;
            public System.Func<Vector3, Terrain> nearestTerrain;
            public System.Func<System.Random> getRandom;
            public bool allowWhenBrushToolActive;
            public System.Func<System.Collections.Generic.List<FacadeDetectionService.FacadePath>> getCachedPaths;
            public System.Func<System.Collections.Generic.List<GlobalTerrainScanner.FacadePath>> getGlobalPaths;
        }

        public class Builder
        {
            private readonly Options _o = new Options();
            public Builder TerrainController(TerrainController v) { _o.terrainController = v; return this; }
            public Builder PaintingController(PaintingController v) { _o.paintingController = v; return this; }
            public Builder GetCurrentProfile(System.Func<VegetationProfile> v) { _o.getCurrentProfile = v; return this; }
            public Builder GetSelectedTerrains(System.Func<System.Collections.Generic.List<Terrain>> v) { _o.getSelectedTerrains = v; return this; }
            public Builder Brush(BrushSettings v) { _o.brush = v; return this; }
            public Builder FilterStrategy(IFilterStrategy v) { _o.filterStrategy = v; return this; }
            public Builder PlacementStrategy(IPlacementOverrideStrategy v) { _o.placementStrategy = v; return this; }
            public Builder IsGenerateMode(System.Func<bool> v) { _o.isGenerateMode = v; return this; }
            public Builder IsPaintMode(System.Func<bool> v) { _o.isPaintMode = v; return this; }
            public Builder MarkSceneDirty(System.Action v) { _o.markSceneDirty = v; return this; }
            public Builder NearestTerrain(System.Func<Vector3, Terrain> v) { _o.nearestTerrain = v; return this; }
            public Builder GetRandom(System.Func<System.Random> v) { _o.getRandom = v; return this; }
            public Builder AllowWhenBrushToolActive(bool v) { _o.allowWhenBrushToolActive = v; return this; }
            public Builder GetCachedPaths(System.Func<System.Collections.Generic.List<FacadeDetectionService.FacadePath>> v) { _o.getCachedPaths = v; return this; }
            public Builder GetGlobalPaths(System.Func<System.Collections.Generic.List<GlobalTerrainScanner.FacadePath>> v) { _o.getGlobalPaths = v; return this; }
            public Options Build() { return _o; }
        }
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
        private readonly System.Func<List<FacadeDetectionService.FacadePath>> getCachedPaths;
        private readonly System.Func<List<GlobalTerrainScanner.FacadePath>> getGlobalPaths;
        private readonly bool allowWhenBrushToolActive;
        private Vector3 _lastPaintPos;
        private bool _hasLastPaintPos;
        private PreviewData _preview = new PreviewData();
        private double _lastPreviewTime;
        private const double _previewIntervalSeconds = 0.05; // 50ms throttle

        public class PreviewData
        {
            public Terrain terrain;
            public Vector3 center;
            public Vector3 normal;
            public System.Collections.Generic.List<FacadeDetectionService.CliffSlice> slices;
            public System.Collections.Generic.List<Vector3> bottomLine;
            public System.Collections.Generic.List<Vector3> topLine;
            public float prefabW;
            public float prefabH;
            public bool hasData;
            public void Clear()
            {
                terrain = null; center = Vector3.zero; normal = Vector3.up;
                slices = null; bottomLine = null; topLine = null; prefabW = prefabH = 0f; hasData = false;
            }
        }

        public SceneInteractionService(Options o)
        {
            this.terrainController = o.terrainController;
            this.paintingController = o.paintingController;
            this.getCurrentProfile = o.getCurrentProfile;
            this.getSelectedTerrains = o.getSelectedTerrains;
            this.brush = o.brush;
            this.filterStrategy = o.filterStrategy;
            this.placementStrategy = o.placementStrategy;
            this.isGenerateMode = o.isGenerateMode;
            this.isPaintMode = o.isPaintMode;
            this.markSceneDirty = o.markSceneDirty;
            this.nearestTerrain = o.nearestTerrain;
            this.getRandom = o.getRandom;
            this.getCachedPaths = o.getCachedPaths;
            this.getGlobalPaths = o.getGlobalPaths;
            this.allowWhenBrushToolActive = o.allowWhenBrushToolActive;
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
            if (hasHit)
            {
                if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.Layout)
                {
                    double now = EditorApplication.timeSinceStartup;
                    if (now - _lastPreviewTime >= _previewIntervalSeconds)
                    {
                        _lastPreviewTime = now;
                        UpdatePreviewData(hitTerrain, hitPos, hitNormal);
                    }
                }
            }
            else _preview.Clear();
            RenderBrushPreview(hasHit, hitPos, hitNormal, e);
            if (e.type == EventType.Repaint) RenderCachedFacades();

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

        private void RenderCachedFacades()
        {
            var gpaths = getGlobalPaths != null ? getGlobalPaths.Invoke() : null;
            var paths = getCachedPaths != null ? getCachedPaths.Invoke() : null;
            var cfg = MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
            if (gpaths != null && gpaths.Count > 0)
            {
                var color = cfg != null ? cfg.facadePreviewBottomColor : new Color(0f, 1f, 0f, 0.8f);
                foreach (var gp in gpaths)
                {
                    var pts = gp.WorldPoints;
                    if (pts == null || pts.Count < 2) continue;
                    Handles.color = color;
                    Handles.DrawAAPolyLine(3f, pts.ToArray());
                    float total = 0f;
                    for (int i = 0; i < pts.Count - 1; i++) total += Vector3.Distance(pts[i], pts[i + 1]);
                    Handles.Label(pts[0] + Vector3.up * 0.25f, $"Length: {total:F1}m");
                    Handles.Label(pts[pts.Count - 1] + Vector3.up * 0.25f, $"Length: {total:F1}m");
                }
                return;
            }
            if (paths == null || paths.Count == 0) return;
            var bottomColor = cfg != null ? cfg.facadePreviewBottomColor : new Color(0f, 1f, 0f, 0.8f);
            var topColor = cfg != null ? cfg.facadePreviewTopColor : new Color(1f, 0.2f, 0.2f, 0.8f);
            foreach (var path in paths)
            {
                var slices = path.SmoothSlices;
                if (slices == null || slices.Count < 2) continue;
                var bottom = new Vector3[slices.Count];
                var top = new Vector3[slices.Count];
                for (int i = 0; i < slices.Count; i++) { bottom[i] = slices[i].BottomPosition; top[i] = slices[i].TopPosition; }
                Handles.color = bottomColor;
                Handles.DrawAAPolyLine(3f, bottom);
                Handles.color = topColor;
                Handles.DrawAAPolyLine(3f, top);
                Handles.color = new Color(1f, 1f, 1f, 0.25f);
                float acc = 0f; float spacing = Mathf.Max(1f, brush != null ? brush.size * 0.1f : 1f);
                for (int i = 0; i < slices.Count - 1; i++)
                {
                    acc += Vector3.Distance(bottom[i], bottom[i + 1]);
                    if (acc >= spacing || i == 0 || i == slices.Count - 2)
                    {
                        Handles.DrawLine(bottom[i], top[i]);
                        acc = 0f;
                    }
                }
                Handles.Label(top[top.Length - 1] + Vector3.up * 0.25f, $"Facade {path.TotalLength:F1}m");
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
            BrushPainter.DrawPreview(_preview, brush);
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

        private void UpdatePreviewData(Terrain terrain, Vector3 center, Vector3 normal)
        {
            if (terrain == null || brush == null) { _preview.Clear(); return; }
            _preview.terrain = terrain; _preview.center = center; _preview.normal = normal; _preview.hasData = true;
            _preview.slices = null; _preview.bottomLine = null; _preview.topLine = null; _preview.prefabW = _preview.prefabH = 0f;
            if (brush.distribution == DistributionType.EdgeLine)
            {
                var profile = getCurrentProfile?.Invoke();
                var itemRef = profile != null ? profile.Items.FirstOrDefault(it => it != null && it.prefabType == PrefabType.Landscape) : null;
                if (itemRef != null)
                {
                    var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? MrTerrainPainter.Editor.Config.ConfigTools.GetCachedConfig();
                    var req = new FacadeDetectionService.FacadeTraceBuilder()
                        .Terrain(terrain).Start(center).Length(brush.size * 2f).FromItem(itemRef).FromConfig(cfg).Build();
                    var slices = FacadeDetectionService.TraceVirtualFacade(req);
                    if (slices != null && slices.Count > 0)
                    {
                        _preview.slices = slices;
                        var bl = new System.Collections.Generic.List<Vector3>(slices.Count);
                        var tl = new System.Collections.Generic.List<Vector3>(slices.Count);
                        for (int i = 0; i < slices.Count; i++) { bl.Add(slices[i].BottomPosition); tl.Add(slices[i].TopPosition); }
                        _preview.bottomLine = bl; _preview.topLine = tl;
                        _preview.prefabW = BrushPainter.GetPrefabHorizontalExtentMeters(itemRef.prefab);
                        _preview.prefabH = BrushPainter.GetPrefabHeightMeters(itemRef.prefab);
                    }
                }
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

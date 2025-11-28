using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using MrTerrainPainter.Runtime.Profiles;
using MrTerrainPainter.Editor.Utils;
using PrefabType = MrTerrainPainter.Runtime.Profiles.PrefabType;

namespace MrTerrainPainter.Editor.Services
{
    /// <summary>
    /// 笔刷预览渲染器，负责在Scene视图中绘制笔刷预览和物体Ghost预览
    /// </summary>
    public static class BrushPreviewRenderer
    {
        private static Material _ghostMaterial;
        private const int INSTANCE_BATCH_SIZE = 1023;

        /// <summary>
        /// 绘制笔刷预览（带数据版本）
        /// </summary>
        public static void DrawPreview(SceneInteractionService.PreviewData data, BrushSettings bs, bool isConfigComplete)
        {
            if (bs == null || !bs.preview) return;

            DrawWireframePreview(data, bs, isConfigComplete);
        }

        /// <summary>
        /// 绘制笔刷预览（简化版本）
        /// </summary>
        public static void DrawPreview(Vector3 center, Vector3 normal, BrushSettings bs, bool isConfigComplete)
        {
            if (bs == null || !bs.preview) return;

            DrawWireframePreview(center, normal, bs, isConfigComplete);
        }

        /// <summary>
        /// 绘制物体Ghost预览
        /// </summary>
        public static void DrawGhostPreview(Terrain terrain, Vector3 center, BrushSettings bs, VegetationProfile profile)
        {
            if (!bs.preview || profile == null || terrain == null) return;

            EnsureGhostMaterial();
            if (_ghostMaterial == null) return;

            var items = profile.Items;
            if (items == null || items.Count == 0) return;
            var item = items[0];
            if (item == null || item.prefab == null) return;

            Mesh mesh = null;
            var meshFilter = item.prefab.GetComponentInChildren<MeshFilter>();
            if (meshFilter != null) mesh = meshFilter.sharedMesh;
            if (mesh == null) return;

            int previewMaxPoints = Mathf.Min(bs.maxPoints, 300);
            var centerXZ = new Vector2(center.x, center.z);
            int desired = VegetationGenerator.ComputeDesiredCandidateCount(bs.shape, bs.size, item.CoreSpacing, previewMaxPoints, previewMaxPoints);

            var candidates = VegetationGenerator.BuildCandidates(
                centerXZ, bs.size, bs.shape, desired, item.CoreSpacing,
                bs.minSpacingJitter, 12345, bs.distribution,
                bs.useBurstPoisson, bs.cluster,
                bs.adaptiveMinFactor, bs.adaptiveMaxFactor, bs.adaptiveNoiseWeight,
                new System.Random(12345));

            if (bs.distribution != DistributionType.EdgeLine)
            {
                float repelDist = Mathf.Max(Mathf.Max(item.CoreSpacing, item.CoreMinRadius), 0.01f) * 0.8f;
                var centerXZ1 = new Vector2(center.x, center.z);
                BrushEngineExtensions.ApplyRelaxation(candidates, centerXZ1, bs.size, repelDist, 2);
            }

            List<Matrix4x4> matrices = new List<Matrix4x4>();
            var rnd = new System.Random(12345);

            foreach (var c in candidates)
            {
                Vector3 pos = new Vector3(c.x, 0, c.y);
                if (TerrainUtils.TryGetHeightAndNormal(terrain, pos, out float h, out Vector3 n))
                {
                    pos.y = h;
                    Quaternion rot = Quaternion.Euler(0, item.SampleYRotation(rnd), 0);
                    var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config;
                    if (cfg != null && cfg.normalDirection)
                        rot = Quaternion.FromToRotation(Vector3.up, n) * rot;

                    Vector3 scale = Vector3.one * item.SampleScale(rnd);
                    matrices.Add(Matrix4x4.TRS(pos, rot, scale));
                }
            }

            if (matrices.Count > 0)
            {
                bool canInstance = SystemInfo.supportsInstancing && _ghostMaterial.enableInstancing;
                if (canInstance)
                {
                    for (int i = 0; i < matrices.Count; i += INSTANCE_BATCH_SIZE)
                    {
                        int count = Mathf.Min(INSTANCE_BATCH_SIZE, matrices.Count - i);
                        var batch = matrices.GetRange(i, count);
                        Graphics.DrawMeshInstanced(mesh, 0, _ghostMaterial, batch.ToArray(), count, null, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
                    }
                }
                else
                {
                    for (int i = 0; i < matrices.Count; i++)
                    {
                        Graphics.DrawMesh(mesh, matrices[i], _ghostMaterial, 0, null, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                    }
                }
            }
        }

        /// <summary>
        /// 绘制立面切片预览
        /// </summary>
        public static void DrawFacadeSlicesPreview(List<FacadeDetectionService.CliffSlice> slices, BrushSettings bs, MrTerrainPainter.Runtime.Profiles.VegetationItem item)
        {
            if (bs == null || !bs.preview || slices == null || slices.Count == 0) return;
            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? Config.ConfigTools.GetCachedConfig();
            var st = bs.previewStyle;

            var bottoms = new Vector3[slices.Count];
            var tops = new Vector3[slices.Count];
            float depthPreview = 0f;
            if (item != null)
            {
                depthPreview = Mathf.Clamp01((item.embedDepthRange.x + item.embedDepthRange.y) * 0.5f);
            }
            for (int i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
                var offset = Vector3.zero;
                if (item != null)
                {
                    offset = rightAxis * item.offsets.x + s.Direction * item.offsets.y + (-s.Normal.normalized) * (depthPreview + Mathf.Max(0f, item.offsets.z));
                }
                bottoms[i] = s.BottomPosition + offset;
                tops[i] = s.TopPosition + offset;
            }

            if (cfg != null)
            {
                Handles.color = cfg.facadePreviewBottomColor;
                Handles.DrawAAPolyLine(st.ringWidth, bottoms);
                Handles.color = cfg.facadePreviewTopColor;
                Handles.DrawAAPolyLine(st.ringWidth, tops);
            }
            else
            {
                Handles.color = Color.green;
                Handles.DrawAAPolyLine(st.ringWidth, bottoms);
                Handles.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                Handles.DrawAAPolyLine(st.ringWidth, tops);
            }

            Handles.color = new Color(1f, 1f, 0.2f, 0.9f);
            float len = Mathf.Max(0.5f, bs.size * 0.25f);
            for (int i = 0; i < slices.Count; i++)
            {
                var o = bottoms[i];
                var n = slices[i].Normal;
                var tip = o + n.normalized * len;
                Handles.DrawAAPolyLine(st.ringWidth, o, tip);
            }
        }

        /// <summary>
        /// 绘制立面轨道和标记
        /// </summary>
        public static void DrawFacadeRailsAndTicks(List<FacadeDetectionService.CliffSlice> slices, BrushPreviewStyle st, Color ring, VegetationItem item)
        {
            if (slices.Count < 2) return;
            var left = new Vector3[slices.Count];
            var right = new Vector3[slices.Count];
            float w = item.edgeReferenceWidthMeters * 0.5f;
            float rendererH = PrefabMetricsCache.GetPrefabHeightMeters(item.prefab);
            float minH = (MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? Config.ConfigTools.GetCachedConfig())?.minFacadeHeightMeters ?? 0.0001f;
            float uni = Mathf.Max(minH / Mathf.Max(0.0001f, rendererH), 1f);
            float scaleX = Mathf.Max(0.0001f, uni + item.facadeScaleOffset.x);
            float wEff = Mathf.Max(0.0001f, item.edgeReferenceWidthMeters * scaleX) * 0.5f;
            w = wEff;
            float depthPreview = Mathf.Clamp01((item.embedDepthRange.x + item.embedDepthRange.y) * 0.5f);
            for (int i = 0; i < slices.Count; i++)
            {
                var s = slices[i];
                var rightAxis = Vector3.Normalize(Vector3.Cross(s.Direction, s.Normal));
                var basePos = s.BottomPosition + rightAxis * item.offsets.x + s.Direction * item.offsets.y + (-s.Normal.normalized) * (depthPreview + Mathf.Max(0f, item.offsets.z));
                left[i] = basePos - s.Normal * w;
                right[i] = basePos + s.Normal * w;
            }
            Handles.color = ring;
            Handles.DrawAAPolyLine(st.ringWidth, left);
            Handles.DrawAAPolyLine(st.ringWidth, right);
        }

        #region Private Helpers

        private static void DrawWireframePreview(SceneInteractionService.PreviewData data, BrushSettings bs, bool isConfigComplete)
        {
            Handles.zTest = CompareFunction.Always;
            var st = bs.previewStyle;
            var fill = isConfigComplete ? st.fillColor : new Color(1f, 0f, 0f, 0.15f);
            var ring = isConfigComplete ? st.ringColor : new Color(1f, 0f, 0f, 0.9f);

            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? Config.ConfigTools.GetCachedConfig();
            bool useNormalDir = cfg != null && cfg.normalDirection;
            var center = data.hasData ? data.center : Vector3.zero;
            var planeN = (useNormalDir && data.hasData) ? data.normal.normalized : Vector3.up;

            DrawShapeGizmo(center, planeN, bs, fill, ring, st);

            if (useNormalDir && data.hasData)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.9f);
                Handles.DrawAAPolyLine(st.ringWidth, center, center + planeN * (bs.size * 0.6f));
            }

            if (bs.distribution == DistributionType.EdgeLine && data.slices != null && data.slices.Count > 1)
            {
                var profile = MrTerrainPainter.Editor.Tools.MTPBrushContext.CurrentProfile;
                var itemRef = profile != null ? profile.Items.FirstOrDefault(it => it != null && it.prefabType == PrefabType.Landscape) : null;

                DrawFacadeSlicesPreview(data.slices, bs, itemRef);
                if (itemRef != null)
                {
                    DrawFacadeRailsAndTicks(data.slices, st, ring, itemRef);
                }
                Handles.Label(center + Vector3.up * 0.25f, $"Render {data.prefabW:F2}m x {data.prefabH:F2}m");
            }
        }

        private static void DrawWireframePreview(Vector3 center, Vector3 normal, BrushSettings bs, bool isConfigComplete)
        {
            Handles.zTest = CompareFunction.Always;
            var st = bs.previewStyle;
            var fill = isConfigComplete ? st.fillColor : new Color(1f, 0f, 0f, 0.15f);
            var ring = isConfigComplete ? st.ringColor : new Color(1f, 0f, 0f, 0.9f);

            var cfg = MrTerrainPainter.Editor.Tools.MTPBrushContext.Config ?? Config.ConfigTools.GetCachedConfig();
            bool useNormalDir = cfg != null && cfg.normalDirection;
            var planeN = useNormalDir ? normal.normalized : Vector3.up;
            var raisedCenter = center + planeN * 0.02f;

            DrawShapeGizmo(raisedCenter, planeN, bs, fill, ring, st);

            if (useNormalDir)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.9f);
                Handles.DrawAAPolyLine(st.ringWidth, raisedCenter, raisedCenter + planeN * (bs.size * 0.6f));
            }
        }

        private static void DrawShapeGizmo(Vector3 center, Vector3 normal, BrushSettings bs, Color fill, Color ring, BrushPreviewStyle st)
        {
            if (bs.shape == BrushShape.Circle)
            {
                Handles.color = fill;
                Handles.DrawSolidDisc(center, normal, bs.size);
                Handles.color = ring;
                DrawCircleAA(center, normal, bs.size, st.ringWidth);

                float innerR = Mathf.Clamp(bs.size * Mathf.Clamp01(1f - bs.hardness), 0f, bs.size);
                if (innerR > 0f)
                {
                    Handles.color = st.innerColor;
                    DrawCircleAA(center, normal, innerR, st.innerWidth);
                }

                if (st.showLabel)
                {
                    var sp = HandleUtility.WorldToGUIPoint(center + new Vector3(0f, 0.02f, bs.size + 0.1f));
                    Handles.BeginGUI();
                    var c = GUI.color;
                    GUI.color = st.labelColor;
                    GUI.Label(new Rect(sp.x + st.labelOffset.x, sp.y + st.labelOffset.y, 100, 20), $"Size {bs.size:F1}");
                    GUI.color = c;
                    Handles.EndGUI();
                }
            }
            else
            {
                Vector3 half = new(bs.size, 0f, bs.size);
                Handles.color = fill;
                Handles.DrawSolidRectangleWithOutline(new[] {
                    center + new Vector3(-half.x, 0, -half.z), center + new Vector3(-half.x, 0, half.z),
                    center + new Vector3(half.x, 0, half.z), center + new Vector3(half.x, 0, -half.z)
                }, fill, ring);
                Handles.color = ring;
                DrawRectAA(center, half, st.ringWidth);
            }
        }

        private static void DrawCircleAA(Vector3 center, Vector3 normal, float radius, float width)
        {
            const int segments = 64;
            var pts = new Vector3[segments + 1];
            var n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            var rot = Quaternion.FromToRotation(Vector3.up, n);
            for (int i = 0; i <= segments; i++)
            {
                float ang = (i / (float)segments) * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                pts[i] = center + rot * (dir * radius);
            }
            Handles.DrawAAPolyLine(width, pts);
        }

        private static void DrawRectAA(Vector3 center, Vector3 half, float width)
        {
            var a = center + new Vector3(-half.x, 0, -half.z);
            var b = center + new Vector3(-half.x, 0, half.z);
            var c = center + new Vector3(half.x, 0, half.z);
            var d = center + new Vector3(half.x, 0, -half.z);
            Handles.DrawAAPolyLine(width, a, b, c, d, a);
        }

        private static void EnsureGhostMaterial()
        {
            if (_ghostMaterial != null) return;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader != null)
            {
                _ghostMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _ghostMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _ghostMaterial.SetInt("_ZWrite", 0);
                _ghostMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                _ghostMaterial.SetColor("_Color", new Color(0.2f, 0.8f, 1f, 0.3f));
            }
        }

        private static bool TryFindTerrainAt(Vector3 pos, out Terrain found)
        {
            found = null;
            var terrains = Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t != null && TerrainUtils.IsWithinTerrainBounds(t, pos))
                {
                    found = t;
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}

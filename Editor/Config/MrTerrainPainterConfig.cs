using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using MrTerrainPainter.Runtime.Profiles;
using MTPPrefabType = MrTerrainPainter.Runtime.Profiles.PrefabType;

namespace MrTerrainPainter.Editor.Config
{
    // 工具窗口持久化的简单配置（可扩展）
    public class MrTerrainPainterConfig : ScriptableObject
    {
        // --- 静态常量定义 ---
        // 建议将配置文件的路径定义为常量，方便统一管理和修改
        private const string RootFolderName = "MrTerrPainterV1";
        private const string EditorFolderName = "Editor";
        private const string ConfigFolderName = "Config";
        public const string ConfigAssetDirectory = "Assets/" + RootFolderName + "/" + EditorFolderName + "/" + ConfigFolderName;
        public const string ConfigAssetName = nameof(MrTerrainPainterConfig) + ".asset";
        public const string ConfigAssetPath = ConfigAssetDirectory + "/" + ConfigAssetName;
        // ----------------------

        [Header("画笔设置")]
        public bool showPreview = true;
        public float defaultBrushSize = 5f;
        public float defaultBrushStrength = 1f;
        public float defaultBrushDensityScale = 1f;
        public float defaultBrushHardness = 1f;
        public bool defaultUseJobs = true;

        [Header("运行时/生成设置")]
        public bool showPoolInHierarchy = false;
        public string recipeGenerationPath = "Assets/MrTerrainPainter/Data";
        public MTPPrefabType defaultGenerationType = MTPPrefabType.Prop;

        // 使用 MappingEntry 统一管理生成映射

        [Header("UI 资源")]
        public VisualTreeAsset startUxml;
        public VisualTreeAsset controlUxml;
        public VisualTreeAsset paintUxml;
        public VisualTreeAsset generateUxml;
        public VisualTreeAsset vegetationProfileRowUxml;
        public VisualTreeAsset prefabIconUxml;
        public VisualTreeAsset draggableAreaUxml;
        public VisualTreeAsset vegetationSharedUxml;
        public VisualTreeAsset brushOverlayUxml;
        public StyleSheet stylesUss;

        [System.Serializable]
        public class MappingEntry
        {
            public Transform node;
            public MTPPrefabType type = MTPPrefabType.Prop;
            public int layer = -1;
        }
        public List<MappingEntry> mappingEntries = new List<MappingEntry>();
    }

#if UNITY_EDITOR

    /// <summary>
    /// 配置文件的加载、保存和验证工具
    /// </summary>
    public static class ConfigTools
    {
        private static readonly System.Collections.Generic.Dictionary<string, VisualTreeAsset> _uxmlCache = new System.Collections.Generic.Dictionary<string, VisualTreeAsset>();
        private static readonly System.Collections.Generic.Dictionary<string, StyleSheet> _styleCache = new System.Collections.Generic.Dictionary<string, StyleSheet>();
        private static VisualTreeAsset FindUxmlByName(params string[] names)
        {
            if (names == null || names.Length == 0) return null;
            for (int i = 0; i < names.Length; i++)
            {
                var n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                if (_uxmlCache.TryGetValue(n, out var cached) && cached != null) return cached;
                var guids = AssetDatabase.FindAssets($"t:VisualTreeAsset name:{n}");
                for (int g = 0; g < guids.Length; g++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[g]);
                    var v = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                    if (v != null)
                    {
                        _uxmlCache[n] = v;
                        return v;
                    }
                }
            }
            return null;
        }

        private static VisualTreeAsset FindUxmlByNamesOrHints(string[] names, string[] hints)
        {
            var byName = FindUxmlByName(names);
            if (byName != null) return byName;
            var guids = AssetDatabase.FindAssets("t:VisualTreeAsset");
            int bestScore = -1; VisualTreeAsset best = null;
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var file = System.IO.Path.GetFileNameWithoutExtension(path);
                int score = 0;
                if (names != null)
                {
                    for (int n = 0; n < names.Length; n++)
                    {
                        var nm = names[n];
                        if (!string.IsNullOrEmpty(nm) && file.Equals(nm)) score += 2;
                        else if (!string.IsNullOrEmpty(nm) && file.IndexOf(nm, System.StringComparison.OrdinalIgnoreCase) >= 0) score += 1;
                    }
                }
                if (hints != null)
                {
                    for (int h = 0; h < hints.Length; h++)
                    {
                        var ht = hints[h];
                        if (!string.IsNullOrEmpty(ht) && file.IndexOf(ht, System.StringComparison.OrdinalIgnoreCase) >= 0) score += 1;
                    }
                }
                if (score <= 0) continue;
                var v = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (v == null) continue;
                if (score > bestScore)
                {
                    bestScore = score; best = v;
                }
            }
            return best;
        }
        /// <summary>
        /// 查找或创建配置资源文件
        /// </summary>
        public static MrTerrainPainterConfig LoadOrCreateAsset()
        {
            // 1. 尝试通过类型查找已存在的资产
            // 使用 nameof(MrTerrainPainterConfig) 确保类型名变更时代码不会出错
            var guids = AssetDatabase.FindAssets($"t:{nameof(MrTerrainPainterConfig)}");
            var path = guids.Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => !string.IsNullOrEmpty(p));

            if (!string.IsNullOrEmpty(path))
            {
                // 如果找到了，尝试加载
                var loaded = AssetDatabase.LoadAssetAtPath<MrTerrainPainterConfig>(path);
                if (loaded != null) return loaded;
            }

            // 2. 如果未找到或加载失败，则创建新的资产
            EnsureFolder(MrTerrainPainterConfig.ConfigAssetDirectory); // 确保目录存在
            var cfg = ScriptableObject.CreateInstance<MrTerrainPainterConfig>();

            AssetDatabase.CreateAsset(cfg, MrTerrainPainterConfig.ConfigAssetPath);

            // 立即保存资产并刷新数据库
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return cfg;
        }

        public static VisualTreeAsset GetBrushOverlayUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.brushOverlayUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MTPBrushOverlay", "MrTerrainPainterBrushOverlay", "MTP.Brush.Overlay");
        }

        public static StyleSheet GetStylesUss(MrTerrainPainterConfig cfg)
        {
            var ss = cfg != null ? cfg.stylesUss : null;
            if (ss != null) return ss;
            var key = "MrTerrainPainterStyles";
            if (_styleCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var guids = AssetDatabase.FindAssets("t:StyleSheet name:" + key);
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var v = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (v != null)
                {
                    _styleCache[key] = v;
                    return v;
                }
            }
            return null;
        }

        public static VisualTreeAsset GetSettingsUxml()
        {
            return FindUxmlByNamesOrHints(
                new[] { "MrTerrainPainter.Settings", "MrTerrainPainterSettings", "MTPSettings" },
                new[] { "MrTerrainPainter", "Settings" }
            );
        }

        public static VisualTreeAsset GetSettingsMappingUxml()
        {
            return FindUxmlByNamesOrHints(
                new[] { "MrTerrainPainter.Settings.Mapping", "MTPTerrainPainterSettingsMappinger", "MrTerrainPainterSettingsMapping" },
                new[] { "MrTerrainPainter", "Settings", "Mapping" }
            );
        }

        public static VisualTreeAsset GetStartUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.startUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MrTerrainPainterWindow.Start", "MrTerrainPainterWindowStart", "MTPStart");
        }
        public static VisualTreeAsset GetControlUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.controlUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MrTerrainPainterWindow.Control", "MrTerrainPainterWindowControl", "MTPControl");
        }
        public static VisualTreeAsset GetPaintUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.paintUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MrTerrainPainterWindow.Paint", "MrTerrainPainterWindowPaintPage", "MTPPaint");
        }
        public static VisualTreeAsset GetGenerateUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.generateUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MrTerrainPainterWindow.Generate", "MrTerrainPainterWindowGenerate", "MTPGenerate");
        }
        public static VisualTreeAsset GetVegetationSharedUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.vegetationSharedUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MrTerrainPainterWindow.VegetationShared", "MrTerrainPainterVegetationShared", "VegetationShared");
        }
        public static VisualTreeAsset GetVegetationProfileRowUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.vegetationProfileRowUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MrTerrainPainterWindow.VegetationProfileRow", "VegetationProfileRow", "MrTerrainPainterVegetationProfileRow");
        }
        public static VisualTreeAsset GetPrefabIconUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.prefabIconUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MrTerrainPainterWindow.PrefabIcon", "VegetationProfilePrefabIcon", "PrefabIcon");
        }
        public static VisualTreeAsset GetDraggableAreaUxml(MrTerrainPainterConfig cfg)
        {
            var v = cfg != null ? cfg.draggableAreaUxml : null;
            if (v != null) return v;
            return FindUxmlByName("MrTerrainPainterWindow.VegetationProfileDraggableArea", "MrTerrainPainterWindowVegetationProfileDraggableArea", "VegetationProfileDraggableArea");
        }

        /// <summary>
        /// 标记配置对象为脏并保存
        /// </summary>
        public static void Save(MrTerrainPainterConfig cfg)
        {
            if (cfg == null) return;

            // 使用 EditorUtility.SetDirty(cfg) 标记对象已修改
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            // 优化：通常在 Editor 脚本中频繁 SetDirty 后只需 SaveAssets。
            // 除非是 CreateAsset 等操作，否则不需要立即 Refresh。
            // AssetDatabase.Refresh(); 
        }

        /// <summary>
        /// 检查配置是否完整（所有必需的资源引用是否已设置）
        /// </summary>
        /// <param name="cfg">要检查的配置对象</param>
        /// <param name="reason">如果不完整，返回失败原因</param>
        /// <returns>配置是否完整</returns>
        public static bool IsComplete(MrTerrainPainterConfig cfg, out string reason)
        {
            reason = string.Empty;
            if (cfg == null) { reason = "配置对象为空"; return false; }

            var reasons = new System.Collections.Generic.List<string>();
            if (cfg.startUxml == null) reasons.Add("StartUXML 未设置");
            if (cfg.controlUxml == null) reasons.Add("ControlUXML 未设置");
            if (cfg.paintUxml == null) reasons.Add("PaintUXML 未设置");
            if (cfg.generateUxml == null) reasons.Add("GenerateUXML 未设置");
            if (cfg.vegetationSharedUxml == null) reasons.Add("VegetationSharedUXML 未设置");
            if (cfg.stylesUss == null) reasons.Add("StylesUSS 未设置");
            if (cfg.vegetationProfileRowUxml == null) reasons.Add("VegetationProfileRowUXML 未设置");
            if (cfg.prefabIconUxml == null) reasons.Add("PrefabIconUXML 未设置");
            if (cfg.draggableAreaUxml == null) reasons.Add("DraggableAreaUXML 未设置");
            if (cfg.brushOverlayUxml == null) reasons.Add("BrushOverlayUXML 未设置");

            if (reasons.Count > 0)
            {
                reason = string.Join("\n", reasons);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 确保给定的 Assets 路径存在，如果不存在则创建所有必要的中间文件夹
        /// </summary>
        public static void EnsureFolder(string path)
        {
            // 移除路径开头的 "Assets/" 部分，转换为相对路径
            if (path.StartsWith("Assets/"))
            {
                path = path.Substring("Assets/".Length);
            }

            var pathParts = path.Split('/');
            var currentPath = "Assets";

            // 从 "Assets" 开始逐级创建文件夹
            foreach (var part in pathParts)
            {
                var newPath = Path.Combine(currentPath, part).Replace('\\', '/'); // 使用 Path.Combine 保证跨平台兼容性

                if (!AssetDatabase.IsValidFolder(newPath))
                {
                    AssetDatabase.CreateFolder(currentPath, part);
                }
                currentPath = newPath;
            }
        }

        public static System.Collections.Generic.Dictionary<MrTerrainPainter.Runtime.Profiles.PrefabType, Transform> BuildTypeMapping(MrTerrainPainterConfig cfg)
        {
            var map = new System.Collections.Generic.Dictionary<MrTerrainPainter.Runtime.Profiles.PrefabType, Transform>();
            if (cfg == null || cfg.mappingEntries == null) return map;
            for (int i = 0; i < cfg.mappingEntries.Count; i++)
            {
                var e = cfg.mappingEntries[i];
                if (e == null || e.node == null) continue;
                map[e.type] = e.node;
            }
            return map;
        }

        public static int GetLayerForType(MrTerrainPainter.Runtime.Profiles.PrefabType t)
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(MrTerrainPainterConfig)}");
            var path = guids.Length > 0 ? AssetDatabase.GUIDToAssetPath(guids[0]) : null;
            var cfg = !string.IsNullOrEmpty(path) ? AssetDatabase.LoadAssetAtPath<MrTerrainPainterConfig>(path) : null;
            if (cfg == null || cfg.mappingEntries == null) return -1;
            for (int i = 0; i < cfg.mappingEntries.Count; i++)
            {
                var e = cfg.mappingEntries[i];
                if (e == null) continue;
                if (e.type == t && e.layer >= 0) return e.layer;
            }
            return -1;
        }

        public static string ResolveRecipePath(MrTerrainPainterConfig cfg)
        {
            if (cfg == null) return "Assets";
            var p = cfg.recipeGenerationPath;
            if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p)) return p;
            var cfgPath = AssetDatabase.GetAssetPath(cfg);
            if (string.IsNullOrEmpty(cfgPath)) return "Assets/MrTerrainPainter/Data";
            var d1 = Path.GetDirectoryName(cfgPath);
            var d2 = string.IsNullOrEmpty(d1) ? null : Path.GetDirectoryName(d1);
            var d3 = string.IsNullOrEmpty(d2) ? null : Path.GetDirectoryName(d2);
            var root = string.IsNullOrEmpty(d3) ? "Assets" : d3.Replace('\\', '/');
            var def = root + "/Data";
            EnsureFolder(def);
            return def;
        }

        public static void ResetSearchCaches()
        {
            _uxmlCache.Clear();
            _styleCache.Clear();
        }
    }
#endif
}
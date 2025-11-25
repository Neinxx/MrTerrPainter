using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Config
{
    // 工具窗口持久化的配置
    public class MrTerrainPainterConfig : ScriptableObject
    {
        // --- 静态路径定义 ---
        private const string RootFolderName = "MrTerrPainterV1";
        private const string EditorFolderName = "Editor";
        private const string ConfigFolderName = "Config";

        // 动态构建路径，避免硬编码错误
        public static readonly string ConfigAssetDirectory = $"{RootFolderName}/{EditorFolderName}/{ConfigFolderName}";
        public const string ConfigAssetName = nameof(MrTerrainPainterConfig) + ".asset";
        public static readonly string ConfigAssetPath = $"{ConfigAssetDirectory}/{ConfigAssetName}";
        // ----------------------

        [Header("画笔设置 (Brush Settings)")]
        [Tooltip("是否在编辑器中显示预览网格")]
        public bool showPreview = true;
        [Range(0.1f, 100f)] public float defaultBrushSize = 5f;
        [Range(0f, 10f)] public float defaultBrushStrength = 1f;
        [Range(0f, 10f)] public float defaultBrushDensityScale = 1f;
        [Range(0f, 1f)] public float defaultBrushHardness = 1f;

        [Header("运行时/生成设置 (Runtime & Generation)")]
        [Tooltip("生成的对象池是否在 Hierarchy 中展开显示")]
        public bool showPoolInHierarchy = false;
        [Tooltip("生成的 Recipe 数据存放路径")]
        public string recipeGenerationPath = "Assets/MrTerrainPainter/Data";
        public Runtime.Profiles.PrefabType defaultGenerationType = Runtime.Profiles.PrefabType.Prop;
        [Tooltip("是否沿法线方向对齐（全局开关）")]
        public bool normalDirection = true;

        [Header("UI 资源绑定 (UI Assets)")]
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

        [Header("帮助 (Help)")]
        public string docsUrl;
        public string exampleScenePath;

        [System.Serializable]
        public class MappingEntry
        {
            public Transform node;
            public Runtime.Profiles.PrefabType type = Runtime.Profiles.PrefabType.Prop;
        }
        public List<MappingEntry> mappingEntries = new List<MappingEntry>();

        [Header("日志与提示")]
        public float missingMappingLogThrottleSeconds = 3f;
        public bool autoOpenSettingsOnMissingMapping = false;
        public string missingMappingLogTemplate = "未找到父节点映射的类型: {0}";

        [Header("撤销设置 (Undo Settings)")]
        [Tooltip("达到该数量阈值时，启用批量优化，绕过逐对象Undo记录以降低内存占用")]
        public int undoBulkThreshold = 5000;
        [Tooltip("是否启用大批量撤销优化（达到阈值时跳过逐对象Undo）")]
        public bool enableUndoBulkOptimization = true;

        [Header("虚拟立面全局参数 (Facade Global)")]
        public MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode facadeSmoothMode = MrTerrainPainter.Runtime.Profiles.FacadeSmoothingMode.Gaussian;
        [Tooltip("平滑窗口大小（奇数>=3）")]
        public int facadeSmoothWindow = 5;
        [Tooltip("高斯平滑Sigma（仅在高斯模式下生效）")]
        public float facadeSmoothSigma = 1f;
        [Tooltip("虚拟面最小高度（米），用于避免双轨重合")]
        public float minFacadeHeightMeters = 0.3f;
        [Tooltip("曲线偏移（米）：沿Right轴")]
        public float curveOffsetRightMeters = 0f;
        [Tooltip("曲线偏移（米）：沿面外Normal轴（正数外推）")]
        public float curveOffsetOutMeters = 0f;

        [Header("立面路径简化 (RDP)")]
        [Tooltip("Ramer-Douglas-Peucker 简化容差（米），用于消除地形锯齿影响")]
        public float facadeRdpEpsilon = 0.5f;

        [Header("等值线扫描 (Contour)")]
        [Tooltip("是否使用高度图等值线扫描（Marching Squares）替代射线扫描")]
        public bool useContourDetection = false;
        [Tooltip("等值线坡度阈值（度），例如 75 表示提取坡度>=75°的连通线")]
        public float contourSlopeDeg = 75f;

        [Header("预览样式 (Preview Style)")]
        [Tooltip("底轨（Bottom）预览颜色")]
        public Color facadePreviewBottomColor = new Color(0f, 1f, 0f, 0.8f);
        [Tooltip("顶轨（Top）预览颜色")]
        public Color facadePreviewTopColor = new Color(1f, 0.2f, 0.2f, 0.8f);
    }

#if UNITY_EDITOR

    /// <summary>
    /// 配置文件的加载、保存和验证工具
    /// </summary>
    public static class ConfigTools
    {
        private static MrTerrainPainterConfig s_cached;
        private static MrTerrainPainterConfig[] s_allCached;
        static ConfigTools()
        {
            EditorApplication.projectChanged -= InvalidateCache;
            EditorApplication.projectChanged += InvalidateCache;
        }
        private static void InvalidateCache()
        {
            s_cached = null;
            s_allCached = null;
        }
        // 事件定义
        public static event Action<bool> NormalDirectionChanged;
        public static event Action<bool> CompletenessChanged;
        public static event Action ConfigUpdated;


        // 默认资源路径常量
        private const string DefaultBaseDir = "Assets/MrTerrPainterV1/Editor";
        private static readonly string DefaultBrushOverlayPath = $"{DefaultBaseDir}/MTPBrushOverlay.uxml";
        private static readonly string DefaultSettingsUxmlPath = $"{DefaultBaseDir}/MrTerrainPainterSettings.uxml";
        private static readonly string DefaultSettingsMappingPath = $"{DefaultBaseDir}/MTPTerrainPainterSettingsMappinger.uxml";


        public static void NotifyConfigUpdated()
        {
            ConfigUpdated?.Invoke();

        }
        /// <summary>
        /// 查找或创建配置资源文件
        /// </summary>
        public static MrTerrainPainterConfig LoadOrCreateAsset()
        {
            // 1. 尝试查找现有资源
            var guids = AssetDatabase.FindAssets($"t:{nameof(MrTerrainPainterConfig)}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    var loaded = AssetDatabase.LoadAssetAtPath<MrTerrainPainterConfig>(path);
                    if (loaded != null) return loaded;
                }
            }

            // 2. 创建新资源
            EnsureFolder("Assets/" + MrTerrainPainterConfig.ConfigAssetDirectory);
            var cfg = ScriptableObject.CreateInstance<MrTerrainPainterConfig>();

            // 使用完整的 Assets 路径
            string fullPath = "Assets/" + MrTerrainPainterConfig.ConfigAssetPath;
            AssetDatabase.CreateAsset(cfg, fullPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MrTerrainPainter] Created new config at: {fullPath}");
            return cfg;
        }

        public static MrTerrainPainterConfig GetCachedConfig()
        {
            if (s_cached != null) return s_cached;
            var guids = AssetDatabase.FindAssets($"t:{nameof(MrTerrainPainterConfig)}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                var loaded = AssetDatabase.LoadAssetAtPath<MrTerrainPainterConfig>(path);
                if (loaded != null) { s_cached = loaded; break; }
            }
            return s_cached;
        }

        public static MrTerrainPainterConfig[] GetAllConfigsCached()
        {
            if (s_allCached != null) return s_allCached;
            var guids = AssetDatabase.FindAssets($"t:{nameof(MrTerrainPainterConfig)}");
            var list = new List<MrTerrainPainterConfig>();
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                var loaded = AssetDatabase.LoadAssetAtPath<MrTerrainPainterConfig>(path);
                if (loaded != null) list.Add(loaded);
            }
            s_allCached = list.ToArray();
            return s_allCached;
        }

        // --- 资源获取 (简化版) ---
        public static VisualTreeAsset GetSettingsUxml() => LoadDefault<VisualTreeAsset>(DefaultSettingsUxmlPath);
        public static VisualTreeAsset GetSettingsMappingUxml() => LoadDefault<VisualTreeAsset>(DefaultSettingsMappingPath);

        // 从 Config 获取，如果为空则返回 Null（或者你可以指定默认值）
        public static VisualTreeAsset GetStartUxml(MrTerrainPainterConfig cfg) => cfg?.startUxml;
        public static VisualTreeAsset GetControlUxml(MrTerrainPainterConfig cfg) => cfg?.controlUxml;
        public static VisualTreeAsset GetPaintUxml(MrTerrainPainterConfig cfg) => cfg?.paintUxml;
        public static VisualTreeAsset GetGenerateUxml(MrTerrainPainterConfig cfg) => cfg?.generateUxml;
        public static VisualTreeAsset GetVegetationSharedUxml(MrTerrainPainterConfig cfg) => cfg?.vegetationSharedUxml;
        public static VisualTreeAsset GetVegetationProfileRowUxml(MrTerrainPainterConfig cfg) => cfg?.vegetationProfileRowUxml;
        public static VisualTreeAsset GetPrefabIconUxml(MrTerrainPainterConfig cfg) => cfg?.prefabIconUxml;
        public static VisualTreeAsset GetDraggableAreaUxml(MrTerrainPainterConfig cfg) => cfg?.draggableAreaUxml;

        public static StyleSheet GetStylesUss(MrTerrainPainterConfig cfg) => cfg?.stylesUss;

        // 特殊处理：BrushOverlay 有默认回退路径
        public static VisualTreeAsset GetBrushOverlayUxml(MrTerrainPainterConfig cfg)
        {
            return (cfg != null && cfg.brushOverlayUxml != null)
                ? cfg.brushOverlayUxml
                : LoadDefault<VisualTreeAsset>(DefaultBrushOverlayPath);
        }

        private static T LoadDefault<T>(string path) where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary>
        /// 标记配置对象为脏并保存
        /// </summary>
        public static void Save(MrTerrainPainterConfig cfg)
        {
            if (cfg == null) return;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            // 检查完整性并触发事件
            bool complete = IsComplete(cfg, out _);
            CompletenessChanged?.Invoke(complete);
            ConfigUpdated?.Invoke();
        }

        public static void SetNormalDirection(MrTerrainPainterConfig cfg, bool value)
        {
            if (cfg == null) return;
            cfg.normalDirection = value;
            EditorUtility.SetDirty(cfg);
            NormalDirectionChanged?.Invoke(value);
            ConfigUpdated?.Invoke();
        }

        /// <summary>
        /// 检查配置是否完整
        /// </summary>
        public static bool IsComplete(MrTerrainPainterConfig cfg, out string reason)
        {
            reason = string.Empty;
            if (cfg == null) { reason = "配置对象为空 (Config is null)"; return false; }

            // 使用 StringBuilder 优化字符串拼接
            var sb = new StringBuilder();

            void Check(UnityEngine.Object obj, string name)
            {
                if (obj == null) sb.AppendLine($"{name} 未设置");
            }

            Check(cfg.startUxml, "StartUXML");
            Check(cfg.controlUxml, "ControlUXML");
            Check(cfg.paintUxml, "PaintUXML");
            Check(cfg.generateUxml, "GenerateUXML");
            Check(cfg.vegetationSharedUxml, "VegetationSharedUXML");
            Check(cfg.stylesUss, "StylesUSS");
            Check(cfg.vegetationProfileRowUxml, "VegetationProfileRowUXML");
            Check(cfg.prefabIconUxml, "PrefabIconUXML");
            Check(cfg.draggableAreaUxml, "DraggableAreaUXML");
            Check(cfg.brushOverlayUxml, "BrushOverlayUXML");

            if (string.IsNullOrEmpty(cfg.recipeGenerationPath))
                sb.AppendLine("RecipeGenerationPath 为空");
            else if (!AssetDatabase.IsValidFolder(cfg.recipeGenerationPath))
                sb.AppendLine($"路径无效: {cfg.recipeGenerationPath}");

            // Mapping 检查逻辑
            if (cfg.mappingEntries != null && cfg.mappingEntries.Count > 0)
            {
                int unbound = cfg.mappingEntries.Count(e => e == null || e.node == null);
                if (unbound > 0) sb.AppendLine($"Mapping 存在未绑定节点: {unbound} 个");

                bool hasPlantBound = cfg.mappingEntries.Any(e => e != null && e.type == Runtime.Profiles.PrefabType.Plant && e.node != null);
                if (!hasPlantBound) sb.AppendLine("Mapping 必须绑定至少一个 Plant 类型节点");
            }

            if (sb.Length > 0)
            {
                reason = sb.ToString().TrimEnd(); // 移除最后的换行符
                return false;
            }

            return true;
        }

        /// <summary>
        /// 确保给定的 Assets 路径存在（支持多级目录创建）
        /// </summary>
        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // 统一路径分隔符
            path = path.Replace('\\', '/');

            // 如果路径已存在，直接返回
            if (AssetDatabase.IsValidFolder(path)) return;

            // 确保路径以 Assets 开头，方便处理
            if (!path.StartsWith("Assets"))
            {
                if (path.StartsWith("/")) path = "Assets" + path;
                else path = "Assets/" + path;
            }

            string[] folders = path.Split('/');
            string currentPath = folders[0]; // "Assets"

            for (int i = 1; i < folders.Length; i++)
            {
                string parentPath = currentPath;
                string newFolder = folders[i];

                // 组合当前层级的完整路径
                currentPath = $"{parentPath}/{newFolder}";

                // 如果当前层级不存在，则创建
                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    AssetDatabase.CreateFolder(parentPath, newFolder);
                }
            }
        }

        /// <summary>
        /// 统一守卫：当配置不完整时，仅打开设置页，阻止其他页面打开
        /// </summary>
        public static bool GuardAndOpenSettingsOnlyIfIncomplete(MrTerrainPainterWindow window)
        {
            if (window == null) return false;

            // 确保 Config 已加载
            var cfg = window.config;
            if (cfg == null) cfg = LoadOrCreateAsset();

            if (!IsComplete(cfg, out var reason))
            {
                // 如果配置不完整，记录日志并打开设置窗口
                // Debug.LogWarning($"[MTP] Config incomplete: {reason}");
                MrTerrainPainterSettingsWindow.Open();
                return false;
            }
            return true;
        }
    }
#endif
}

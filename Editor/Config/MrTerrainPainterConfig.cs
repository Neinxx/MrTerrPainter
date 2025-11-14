using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Linq;
using System.IO;
using System.Collections.Generic;

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

        [Header("运行时/生成设置")]
        public bool showPoolInHierarchy = false;
        public string recipeGenerationPath = "Assets/MrTerrainPainter/Data";
        public Runtime.Profiles.PrefabType defaultGenerationType = Runtime.Profiles.PrefabType.Prop;

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
            public Runtime.Profiles.PrefabType type = Runtime.Profiles.PrefabType.Prop;
        }
        public List<MappingEntry> mappingEntries = new List<MappingEntry>();
    }

#if UNITY_EDITOR

    /// <summary>
    /// 配置文件的加载、保存和验证工具
    /// </summary>
    public static class ConfigTools
    {
        private static readonly string DefaultBrushOverlayUxmlPath = "Assets/MrTerrPainterV1/Editor/MTPBrushOverlay.uxml";
        private static readonly string DefaultSettingsUxmlPath = "Assets/MrTerrPainterV1/Editor/MrTerrainPainterSettings.uxml";
        private static readonly string DefaultSettingsMappingUxmlPath = "Assets/MrTerrPainterV1/Editor/MTPTerrainPainterSettingsMappinger.uxml";
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
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefaultBrushOverlayUxmlPath);
        }

        public static StyleSheet GetStylesUss(MrTerrainPainterConfig cfg)
        {
            return cfg != null ? cfg.stylesUss : null;
        }

        public static VisualTreeAsset GetSettingsUxml()
        {
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefaultSettingsUxmlPath);
        }

        public static VisualTreeAsset GetSettingsMappingUxml()
        {
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefaultSettingsMappingUxmlPath);
        }

        public static VisualTreeAsset GetStartUxml(MrTerrainPainterConfig cfg) => cfg != null ? cfg.startUxml : null;
        public static VisualTreeAsset GetControlUxml(MrTerrainPainterConfig cfg) => cfg != null ? cfg.controlUxml : null;
        public static VisualTreeAsset GetPaintUxml(MrTerrainPainterConfig cfg) => cfg != null ? cfg.paintUxml : null;
        public static VisualTreeAsset GetGenerateUxml(MrTerrainPainterConfig cfg) => cfg != null ? cfg.generateUxml : null;
        public static VisualTreeAsset GetVegetationSharedUxml(MrTerrainPainterConfig cfg) => cfg != null ? cfg.vegetationSharedUxml : null;
        public static VisualTreeAsset GetVegetationProfileRowUxml(MrTerrainPainterConfig cfg) => cfg != null ? cfg.vegetationProfileRowUxml : null;
        public static VisualTreeAsset GetPrefabIconUxml(MrTerrainPainterConfig cfg) => cfg != null ? cfg.prefabIconUxml : null;
        public static VisualTreeAsset GetDraggableAreaUxml(MrTerrainPainterConfig cfg) => cfg != null ? cfg.draggableAreaUxml : null;

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

            if (string.IsNullOrEmpty(cfg.recipeGenerationPath)) reasons.Add("RecipeGenerationPath 为空");
            else if (!AssetDatabase.IsValidFolder(cfg.recipeGenerationPath)) reasons.Add("RecipeGenerationPath 不是有效的项目文件夹");

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
    }
#endif
}

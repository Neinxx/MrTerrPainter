using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Linq;
using System.IO; // 引入 System.IO 命名空间

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

        [Tooltip("Generation Mapping：与设置页的 Object + Type 一一对应")]
        public GameObject[] objectList = new GameObject[0];
        public Runtime.Profiles.PrefabType[] objectTypeList = new Runtime.Profiles.PrefabType[0];

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
    }

#if UNITY_EDITOR

    /// <summary>
    /// 配置文件的加载、保存和验证工具
    /// </summary>
    public static class ConfigTools
    {
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

            // 优化：将所有必需的 UXML 和 USS 资源检查放在一个列表中进行迭代
            if (cfg.startUxml == null) { reason = "StartUXML 未设置"; return false; }
            if (cfg.controlUxml == null) { reason = "ControlUXML 未设置"; return false; }
            if (cfg.paintUxml == null) { reason = "PaintUXML 未设置"; return false; }
            if (cfg.generateUxml == null) { reason = "GenerateUXML 未设置"; return false; }
            if (cfg.vegetationSharedUxml == null) { reason = "VegetationSharedUXML 未设置"; return false; }
            if (cfg.stylesUss == null) { reason = "StylesUSS 未设置"; return false; }
            if (cfg.vegetationProfileRowUxml == null) { reason = "VegetationProfileRowUXML 未设置"; return false; }
            if (cfg.prefabIconUxml == null) { reason = "PrefabIconUXML 未设置"; return false; }
            if (cfg.draggableAreaUxml == null) { reason = "DraggableAreaUXML 未设置"; return false; }

            // 检查生成路径
            if (string.IsNullOrEmpty(cfg.recipeGenerationPath)) { reason = "RecipeGenerationPath 为空"; return false; }
            if (!AssetDatabase.IsValidFolder(cfg.recipeGenerationPath)) { reason = "RecipeGenerationPath 不是有效的项目文件夹"; return false; }

            // 优化：检查映射列表的长度是否一致，这是运行时配置健壮性的重要保障
            if (cfg.objectList.Length != cfg.objectTypeList.Length)
            {
                reason = $"ObjectList (长度: {cfg.objectList.Length}) 和 ObjectTypeList (长度: {cfg.objectTypeList.Length}) 长度不一致，请检查配置。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 确保给定的 Assets 路径存在，如果不存在则创建所有必要的中间文件夹
        /// </summary>
        private static void EnsureFolder(string path)
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
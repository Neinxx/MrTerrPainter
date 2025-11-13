using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Linq;
namespace MrTerrainPainter.Editor.Config
{
    // 工具窗口持久化的简单配置（可扩展）
    public class MrTerrainPainterConfig : ScriptableObject
    {
        public bool showPreview = true;
        public float defaultBrushSize = 5f;
        public float defaultBrushStrength = 1f;
        public float defaultBrushDensityScale = 1f;
        public float defaultBrushHardness = 1f;
        [Tooltip("窗口失去焦点时是否自动切换到Generate选项卡")] public bool switchToGenerateOnLostFocus = false;

        // 设置页相关持久化
        public bool showPoolInHierarchy = true;

        public string recipeGenerationPath = "Assets/MrTerrainPainter/Data";
        public Runtime.Profiles.PrefabType defaultGenerationType = Runtime.Profiles.PrefabType.Prop;
        // Generation Mapping：与设置页的 Object + Type 一一对应
        public GameObject[] objectList = new GameObject[0];
        public Runtime.Profiles.PrefabType[] objectTypeList = new Runtime.Profiles.PrefabType[0];

        public VisualTreeAsset startUxml;
        public VisualTreeAsset controlUxml;
        public VisualTreeAsset paintUxml;
        public VisualTreeAsset generateUxml;
        public VisualTreeAsset vegetationProfileRowUxml;
        public VisualTreeAsset prefabIconUxml;
        public VisualTreeAsset draggableAreaUxml;
        public StyleSheet stylesUss;
    }

#if UNITY_EDITOR


    public static class ConfigTools
    {
        private const string ConfigFolder = "Assets/MrTerrPainterV1/Editor/Config";
        private const string ConfigAssetPath = ConfigFolder + "/MrTerrainPainterConfig.asset";

        public static MrTerrainPainterConfig LoadOrCreateAsset()
        {
            var guid = AssetDatabase.FindAssets($"t:{nameof(MrTerrainPainterConfig)}").FirstOrDefault();
            if (!string.IsNullOrEmpty(guid))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var loaded = AssetDatabase.LoadAssetAtPath<MrTerrainPainterConfig>(path);
                if (loaded != null) return loaded;
            }
            EnsureFolder();
            var cfg = ScriptableObject.CreateInstance<MrTerrainPainterConfig>();
            AssetDatabase.CreateAsset(cfg, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return cfg;
        }

        public static void Save(MrTerrainPainterConfig cfg)
        {
            if (cfg == null) return;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static bool IsComplete(MrTerrainPainterConfig cfg, out string reason)
        {
            reason = string.Empty;
            if (cfg == null) { reason = "配置对象为空"; return false; }
            if (cfg.startUxml == null) { reason = "StartUXML 未设置"; return false; }
            if (cfg.controlUxml == null) { reason = "ControlUXML 未设置"; return false; }
            if (cfg.paintUxml == null) { reason = "PaintUXML 未设置"; return false; }
            if (cfg.generateUxml == null) { reason = "GenerateUXML 未设置"; return false; }
            if (cfg.stylesUss == null) { reason = "StylesUSS 未设置"; return false; }
            if (cfg.vegetationProfileRowUxml == null) { reason = "VegetationProfileUXML 未设置"; return false; }
            if (cfg.prefabIconUxml == null) { reason = "PrefabIconUXML 未设置"; return false; }
            if (cfg.draggableAreaUxml == null) { reason = "DraggableAreaUXML 未设置"; return false; }
            if (string.IsNullOrEmpty(cfg.recipeGenerationPath)) { reason = "RecipeGenerationPath 为空"; return false; }
            if (!AssetDatabase.IsValidFolder(cfg.recipeGenerationPath)) { reason = "RecipeGenerationPath 不是有效的项目文件夹"; return false; }
            if (cfg.objectList == null || cfg.objectTypeList == null) { reason = "生成映射为空"; return false; }
            if (cfg.objectList.Length != cfg.objectTypeList.Length) { reason = "生成映射长度不一致"; return false; }
            return true;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/MrTerrPainterV1"))
                AssetDatabase.CreateFolder("Assets", "MrTerrPainterV1");
            if (!AssetDatabase.IsValidFolder("Assets/MrTerrPainterV1/Editor"))
                AssetDatabase.CreateFolder("Assets/MrTerrPainterV1", "Editor");
            if (!AssetDatabase.IsValidFolder("Assets/MrTerrPainterV1/Editor/Config"))
                AssetDatabase.CreateFolder("Assets/MrTerrPainterV1/Editor", "Config");
        }
    }
#endif
}

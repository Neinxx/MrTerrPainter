using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.EditorTools;
using UnityEditor.Callbacks;
using UnityEngine;
using MrTerrainPainter.Editor;
using MrTerrainPainter.Editor.Tools;
using MrTerrainPainter.Runtime.Profiles;

namespace MrTerrainPainter.Editor
{
    /// <summary>
    /// 提供MrTerrainPainter的各种入口点：快捷键、上下文菜单和资源打开处理
    /// </summary>
    public static class MTPEntryPoints
    {

        private const string k_ShortcutBase = "MTP/";
        private const string k_OpenWindowShortcut = k_ShortcutBase + "Open Window";
        private const string k_ToggleBrushToolShortcut = k_ShortcutBase + "Toggle Brush Tool";

        /// <summary>
        /// 通过快捷键打开MrTerrainPainter窗口
        /// 快捷键: Alt + M
        /// </summary>
        [Shortcut(k_OpenWindowShortcut, KeyCode.M, ShortcutModifiers.Alt)]
        public static void ShortcutOpenWindow()
        {
            MrTerrainPainterWindow.GetOrOpen();
        }

        /// <summary>
        /// 通过快捷键切换笔刷工具状态
        /// 快捷键: Alt + B
        /// 仅在选中带有Terrain组件的对象时有效
        /// </summary>
        [Shortcut(k_ToggleBrushToolShortcut, KeyCode.B, ShortcutModifiers.Alt)]
        public static void ToggleBrushTool()
        {
            // 简化选择对象和地形组件的检查
            if (Selection.activeGameObject?.GetComponent<Terrain>() == null)
                return;

            // 使用类型别名简化代码
            var brushToolType = typeof(MTPBrushTool);
            bool isActive = ToolManager.activeToolType == brushToolType;

            // 切换工具状态
            if (isActive)
                ToolManager.RestorePreviousTool();
            else
                ToolManager.SetActiveTool(brushToolType);
        }

        /// <summary>
        /// 在Terrain组件的上下文菜单中添加"Open Window"选项
        /// </summary>
        [MenuItem("CONTEXT/Terrain/Mr Terrain Painter/Open Window")]
        private static void ContextOpenWindow(MenuCommand _)
        {
            MrTerrainPainterWindow.GetOrOpen();
        }

        /// <summary>
        /// 处理植被配置文件的打开行为，自动聚焦到对应窗口
        /// </summary>
        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int _)
        {
            // 检查是否为植被配置文件
            if (EditorUtility.InstanceIDToObject(instanceId) is not VegetationProfile)
                return false;

            // 获取或打开窗口并设置聚焦
            var window = MrTerrainPainterWindow.GetOrOpen();
            if (window == null)
                return true;

            window.Focus();

            // 延迟调用确保窗口初始化完成
            EditorApplication.delayCall += () =>
            {
                // 二次检查窗口有效性
                if (window != null)
                    window.OpenPaintingSettings();
            };

            return true;
        }
    }
}
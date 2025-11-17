## 现状诊断
- 主入口位于 `MrTerrainPainterWindow.cs:104` 的 `MenuItem("Tools/Mr Terrain Painter Main")`，不符合常见的窗口入口放在 `Window` 菜单的习惯。
- 另有 `EditorTool`（`Tools/MTPBrushTool.cs`）与 `SceneView Overlay`（`Tools/MTPBrushOverlay.cs`）能唤起窗口，但缺少统一的快捷方式与上下文入口，导致入口分散、不易发现。
- 缺少从 `Terrain` 的右键上下文以及从 `VegetationProfile` 资产的双击直达入口，降低了工作流连贯性。

## 改进目标
- 入口标准化：统一到 `Window/Mr Terrain Painter`，同时保留工具与叠加层的便捷入口。
- 多通道直达：快捷键、Terrain 上下文菜单、双击 Profile 资产直达窗口并定位到 Painting。
- 一致的命名与聚合：在一个集中入口类里管理所有入口，降低分散与维护成本。
- 不改变现有核心功能与 UI，只优化入口与导航流。

## 实施方案
### 1）标准化 Window 菜单入口（替换旧的 Tools 菜单）
- 在 `MrTerrainPainterWindow.cs` 增设标准窗口入口，并移除旧的 `Tools/...`：
```
[MenuItem("Window/Mr Terrain Painter", priority = 2000)]
public static void OpenWindow() { GetOrOpen(); }

[MenuItem("Window/Mr Terrain Painter/Open Painting Settings", priority = 2001)]
public static void OpenPaintingSettingsMenu() { var win = GetOrOpen(); EditorApplication.delayCall += () => { if (win != null) win.OpenPaintingSettings(); }; }
```

### 2）集中入口类：统一快捷键、上下文菜单、资产双击
- 新增 `Editor/MTPEntryPoints.cs`，聚合所有入口：
```
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.EditorTools;
using UnityEditor.Callbacks;
using UnityEngine;
using MrTerrainPainter.Editor;

public static class MTPEntryPoints
{
    [Shortcut("MTP/Open Window", KeyCode.M, ShortcutModifiers.Alt)]
    public static void ShortcutOpenWindow() { MrTerrainPainterWindow.GetOrOpen(); }

    [Shortcut("MTP/Toggle Brush Tool", KeyCode.B, ShortcutModifiers.Alt)]
    public static void ToggleBrushTool()
    {
        var isActive = UnityEditor.EditorTools.ToolManager.activeToolType == typeof(MrTerrainPainter.Editor.Tools.MTPBrushTool);
        if (isActive) UnityEditor.EditorTools.ToolManager.RestorePreviousTool();
        else UnityEditor.EditorTools.ToolManager.SetActiveTool<MrTerrainPainter.Editor.Tools.MTPBrushTool>();
    }

    [MenuItem("CONTEXT/Terrain/Mr Terrain Painter/Open Window")]
    private static void ContextOpenWindow(MenuCommand cmd) { MrTerrainPainterWindow.GetOrOpen(); }

    [OnOpenAsset]
    public static bool OnOpenAsset(int instanceId, int line)
    {
        var obj = EditorUtility.InstanceIDToObject(instanceId);
        if (obj is MrTerrainPainter.Runtime.Profiles.VegetationProfile)
        {
            var win = MrTerrainPainterWindow.GetOrOpen();
            if (win != null) { win.Focus(); EditorApplication.delayCall += () => { if (win != null) win.OpenPaintingSettings(); }; }
            return true;
        }
        return false;
    }
}
```

### 3）EditorTool 与 Overlay 保持一致的唤起与导航
- 维持现有 `MTPBrushTool` 在激活时确保窗口已打开的逻辑（`Tools/MTPBrushTool.cs:17-22`）。
- `MTPBrushOverlay` 的按钮继续跳转到 Painting（`Tools/MTPBrushOverlay.cs:57-68`），与新菜单行为保持一致。

### 4）可选：Project Settings 入口
- 为配置添加 Project Settings 入口，提升寻址一致性：
```
using UnityEditor;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor
{
    public class MTPSettingsProvider : SettingsProvider
    {
        public MTPSettingsProvider(string path, SettingsScope scopes) : base(path, scopes) {}
        public override void OnActivate(string searchContext, VisualElement root)
        {
            var vt = Config.ConfigTools.GetSettingsUxml();
            if (vt != null) root.Add(vt.Instantiate());
        }
        [SettingsProvider]
        public static SettingsProvider CreateProvider() => new MTPSettingsProvider("Project/Mr Terrain Painter", SettingsScope.Project);
    }
}
```

## 验证步骤
- 重新编译后检查：`Window/Mr Terrain Painter` 是否出现且可打开窗口并在二级菜单快速进入 Painting。
- 右键 `Terrain` 是否出现 `Mr Terrain Painter/Open Window`。
- 双击 `VegetationProfile` 资产是否直达窗口 Painting 并聚焦。
- 快捷键 `Alt+M` 是否能打开窗口，`Alt+B` 是否能切换刷子工具。
- 在 SceneView 激活刷子工具时，Overlay 是否正常显示且 “Open Settings” 跳转一致。

## 关键优化点说明
- 入口集中管理与统一命名，降低维护成本与认知负担。
- 符合 Unity 最佳实践：窗口入口归类 `Window` 菜单；上下文入口与资产入口完善工作流；快捷键提升效率。
- 保持提前返回与单一职责：每个入口方法都只做一件事，且对无效输入立即返回。
# 改造目标
- 使用 `EditorTool` 承载笔刷交互：启停优雅、可快捷键切换、与Unity工具栏一致。
- 在 `EditorTool.OnToolGUI` 按 Layout/Mouse/Repaint 分层：只在 Repaint 绘制预览，稳定美观。
- 将旧 Painting 改为 `PaintingSettings`：仅管理参数，不负责绘制开启。
- 预览为空心圆，使用抗锯齿加粗线条。
- 提供 SceneView Overlay（UXML）：显示基础参数与快速切换，高级设置保留在 `PaintingSettings`。
- 快捷键：`[` 缩小、 `]` 放大笔刷。

## 实施项与文件
1. 新增 `Editor/Tools/MTPBrushTool.cs`
- `class MTPBrushTool : EditorTool`
- `EditorToolAttribute(name="Mr Terrain Brush", icon="Editor/Icons/MTPBrush.png")`
- 字段：复用窗口的 `BrushSettings brush`；访问当前 `VegetationProfile`、`extraProfiles`；`System.Random rnd`
- 方法：
  - `OnActivated()/OnWillBeDeactivated()`：订阅/释放 `SceneView.duringSceneGui` 或直接使用 `OnToolGUI`
  - `OnToolGUI()`：
    - Layout：`HandleUtility.AddDefaultControl`
    - Mouse：更新笔触中心；左键绘制（调用 `BrushPainter.Paint` 或 `PaintMixed`）、右键擦除
    - Repaint：`BrushPainter.DrawPreview(center, normal, brush)`（空心圆，线条加粗）
  - 地形命中：复用 `TryGetTerrainHit`（抽取到工具内私有静态或公共 `TerrainUtils`）

2. 新增 `Editor/Tools/MTPBrushOverlay.cs`
- `class MTPBrushOverlay : Overlay`
- `CreatePanelContent()`：加载 `Assets/MrTerrPainterV1/Editor/MTPBrushOverlay.uxml`，绑定基础参数（Size/Strength/Density/Hardness/Distribution等）与“打开 PaintingSettings”按钮
- 使用配置的 `stylesUss` 保持风格统一

3. 新增 `Assets/MrTerrPainterV1/Editor/MTPBrushOverlay.uxml`
- 统一风格的基础参数面板：
  - `SliderInt Size/Strength/Density/Hardness`
  - `EnumField Distribution`
  - `Toggle MixExtraProfiles`
  - `Button OpenSettings` 跳转到旧窗口 `PaintingSettings` 页

4. 旧窗口调整（最小改动）
- 将“Painting”标签改名并语义为 `PaintingSettings`：仅展示并绑定参数，不再控制绘制开关
- 当 `EditorTool` 激活时，窗口的场景绘制逻辑不再生效（或在 `OnSceneGUI` 检测工具激活则提前返回）

5. 预览绘制为空心圆（加粗）
- 在 `BrushPainter.DrawPreview(center, normal, brush)` 中：
  - 使用 `Handles.DrawWireDisc`
  - 叠加 `Handles.DrawAAPolyLine(width: 4f, sampledCirclePoints)` 提升线条粗细与抗锯齿
  - `Handles.zTest = LessEqual`，颜色半透明

6. 快捷键
- 使用 `UnityEditor.ShortcutManagement.Shortcut`：
  - `Shortcut("MTP/Brush/Increase Size", KeyCode.RightBracket)` → `brush.size = Mathf.Min(max, brush.size + step)`
  - `Shortcut("MTP/Brush/Decrease Size", KeyCode.LeftBracket)` → `brush.size = Mathf.Max(min, brush.size - step)`
- 当工具激活时才响应，避免污染其他编辑状态

## 验证
- 工具栏按钮切换到“Mr Terrain Brush”，在场景中预览稳定（与地形法线对齐）、空心加粗圆环美观。
- 鼠标左键绘制、右键擦除；Shift 在 Generate 模式下区域生成保持一致。
- Overlay 参数调整即时生效；`[`/`]` 快捷键大小变更正确；高级设置在窗口 `PaintingSettings` 中保持完整。

## 兼容与回退
- 默认行为不变：未激活工具时，窗口仍可用于参数管理；绘制入口改由工具。
- 若遇到版本限制（2021.3.18f1支持 `EditorTool` 与 `Overlay`），可回退仅实现 `OnSceneGUI` 分层与自定义面板。
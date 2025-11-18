## 概览
- 已全面审查 Editor/Runtime 目录的核心脚本（工具、控制器、服务、配置、池、算法）。整体结构清晰，提前返回与单一职责普遍遵循，运行效率良好，符合 Unity 2021.3。
- 发现若干可改进点，涉及健壮性、性能微优化与一致性，以下给出可执行的优化计划。

## 问题摘要（含定位）
- 额外配方空项清理失效：在窗口清理空的 ExtraProfiles 时向上下文传入 null 导致无法移除（Editor/MrTerrainPainterWindow.cs:162–167）。
- 对象池回收键不稳定：回收时以 `go.name` 构建键，可能与实例化时的 `prefab.name` 不一致，导致对象未进入池而仅被禁用（Editor/Services/VegetationPool.cs:114–121）。
- 近地形搜索代码重复且使用距离而非平方距离：工具内自己做一版查找，与控制器中的实现分离，且使用 `Vector3.Distance` 开销更高（Editor/Tools/MTPBrushTool.cs:81–87；建议统一到 TerrainController）。
- 选中地形列表构造逻辑重复：MTPBrushTool 内两处从 `SelectedTerrains`/`Selection` 拼装列表的相似代码，宜收敛到控制器方法（Editor/Tools/MTPBrushTool.cs:44–58, 67–76）。
- `SceneInteractionService` 注入的 `getSelectedTerrains` 未使用：影响接口一致性与可读性（Editor/Services/SceneInteractionService.cs:15, 32），建议要么使用要么移除。
- 预览每帧完整性检查可能偏频繁：`BrushPainter.DrawPreview` 每次 Repaint 调用 `ConfigTools.IsComplete`，可改为缓存并由事件驱动更新（Editor/Services/BrushPainter.cs:233–239, 285–291）。
- 快捷键调整尺寸未触发场景重绘：在某些编辑器状态下，调整后预览可能滞后，建议主动请求重绘（Editor/Tools/MTPBrushTool.cs:108–121）。

## 计划改动与实现要点
- 修复 ExtraProfiles 空项清理
  - 增加上下文 API 支持清理 null（如 `PruneExtraNulls()` 或允许 `RemoveExtra(null)` 执行批量清理）（Editor/Tools/MTPBrushContext.cs）。
  - 窗口侧改为调用新 API 或直接调用 `ClearExtras()` 并重建有效条目（Editor/MrTerrainPainterWindow.cs）。
- 稳定对象池回收键
  - 在 `VegetationInstance` 增加 `sourcePrefabName` 字段或在创建时将原始 prefab 名写入组件；`Recycle` 构键时优先取组件字段而非 `go.name`（Editor/Runtime/Core/VegetationInstance.cs；Editor/Services/VegetationPool.cs）。
  - 统一 `BuildKey` 的入参来源，确保 Get/Release 一致。
- 统一近地形搜索与选中地形列表
  - 将“从上下文/Selection 拼装地形列表”和“最近地形查找”完全收敛到 `TerrainController`（Editor/Controllers/TerrainController.cs）。
  - MTPBrushTool 与 SceneInteractionService 均调用控制器方法，避免重复逻辑（Editor/Tools/MTPBrushTool.cs；Editor/Services/SceneInteractionService.cs）。
  - 使用 `Vector3.SqrMagnitude` 替换距离比较，减少开销。
- 使用 `getSelectedTerrains`
  - 在 `SceneInteractionService` 内部优先使用选中列表进行最近地形判定，空时再回退到 `Terrain.activeTerrains`（Editor/Services/SceneInteractionService.cs），提升一致性。
- 预览完整性检查改为事件驱动缓存
  - 增加缓存 `isConfigComplete`，由 `ConfigTools.CompletenessChanged` 事件驱动更新；`DrawPreview` 直接读取缓存而非每帧计算（Editor/Services/BrushPainter.cs；Editor/Config/MrTerrainPainterConfig.cs）。
- 快捷键后主动重绘
  - 在尺寸改变后请求 `SceneView.RepaintAll()`（或延迟调用），保证预览即时反馈（Editor/Tools/MTPBrushTool.cs）。

## 验证与兼容
- 环境：Unity 2021.3.18f1；UI Toolkit/Overlay/EditorTool API 均可用。
- 测试要点：
  - 切换/删除 Profile 资产后，ExtraProfiles 不残留 null；
  - 大批量绘制后清空，池中对象复用稳定、无多余销毁；
  - 选中多个地形时最近地形判定正确，性能稳定；
  - 配置完整性变化时预览色彩实时切换、无卡顿；
  - 快捷键调整尺寸预览即时更新。

## 风险与回滚
- 对象池键变更需与旧场景兼容：如未找到键则走现有回退逻辑；提供临时兼容分支。
- 控制器收敛涉及少量调用点重定向：逐文件改动，保持接口最小化变更，若出现问题可快速回退到原实现。

请确认以上计划，确认后我将按上述步骤逐项落实并提交具体代码改动与验证结果。
## 总览
- 项目结构清晰：Editor 下分 Controllers/Services/Tools/Views/Config，Runtime 持 Profile/实例类型。
- 入口统一：`MTPEntryPoints` 提供快捷键与上下文菜单；`MrTerrainPainterWindow` 作为主 UI；`MTPBrushTool` 作为场景刷子；`MTPBrushOverlay` 叠加控件联动窗口状态。

## 主流程链路（Paint）
- 场景输入 → `SceneInteractionService.OnSceneGUI`（EditorWindow 或 EditorTool 驱动）
- 命中检测 → `TerrainController.TryGetTerrainHit`（`Editor/Controllers/TerrainController.cs:56`）
- 预览绘制 → `BrushPainter.DrawPreview`（`Editor/Services/BrushPainter.cs:270`）
- 鼠标处理 → 左键绘制/右键擦除（严格无修饰键），笔触间距控制（`Editor/Services/SceneInteractionService.cs:108`）
- 植被落点采样 → `BrushEngine.*`（Poisson/Cluster/Jittered/Adaptive/Halton）（`Editor/Services/BrushEngine.cs`）
- 实例生成与复用 → `VegetationPool.Get/IndexRegister` + `VegetationInstance`（`Editor/Services/VegetationPool.cs`，`Runtime/Core/VegetationInstance.cs`）

## 合理性评估
- 职责拆分基本合理：窗口负责 UI/状态，服务负责场景交互，工具专注绘制；对象池与采样算法独立。
- 提前返回应用充分：控制器/服务/工具多处提前返回确保健壮与高效。
- 性能：采样使用列表池与（可选）Burst Poisson；对象池减少 GC；Terrain API 使用得当。
- 交互一致性：服务统一了输入规则与事件消费，Overlay/窗口通过事件联动状态。

## 发现的问题
- 重复与分叉：窗口保留的旧场景方法（如 `HandlePaintMouse` 等）与服务逻辑重复且行为不同；建议统一删减，避免维护分叉（`Editor/MrTerrainPainterWindow.cs` 和 `Editor/MrTerrainPainterWindow.Control.cs` 中相关方法）。
- 最近地形/选中策略不一致：工具侧使用 `Terrain.activeTerrains`，窗口侧使用显式选择集合；导致行为差异与可预期性降低（`Editor/Tools/MTPBrushTool.cs:56` vs `Editor/MrTerrainPainterWindow.cs:618`）。
- 随机数策略不统一：工具端 `System.Random()` 非确定性，窗口端按 `Profile.randomSeed`；影响可复现性（`Editor/Tools/MTPBrushTool.cs:67`）。
- 缺少映射时的用户体验：绘制路径中遇到缺映射直接弹窗并 `return` 终止整个绘制批次，体验不一致（生成路径仅日志并跳过），建议统一为“记录一次并跳过该条目”（`Editor/Services/BrushPainter.cs:414-419`）。
- `MarkSceneDirty` 重复实现：窗口与工具各自实现，建议抽到工具类统一调用。

## 优化方案
### 1. 场景交互统一
- 移除或私有化窗口中旧版 `HandleLayoutControl/RenderBrushPreview/HandlePaintMouse/TryGetTerrainHit/VegetationPainterOnTerrain`，仅保留 `sceneService.OnSceneGUI()` 驱动。
- 确保所有输入规则以 `SceneInteractionService` 为唯一事实来源。

### 2. 选中地形与最近地形策略一致化
- 工具端 `getSelectedTerrains` 改为优先 `MTPBrushContext.SelectedTerrains`，为空时后备 `Selection.activeGameObject` 单 Terrain。
- `nearestTerrain` 统一调用 `TerrainController.NearestTerrain(pos, MTPBrushContext.SelectedTerrains as List<Terrain>)`，为空集合时再回退 `Terrain.activeTerrains`。

### 3. 随机数统一可复现
- 工具端 `getRandom` 改为按当前 `VegetationProfile.randomSeed` 懒初始化，保证与窗口一致的可复现性。

### 4. 缺映射处理一致化
- `BrushPainter.Paint/PaintMixed` 中缺映射分支改为：仅记录一次错误（或在状态栏提示），跳过该条目，继续处理其余项；与 `VegetationGenerator.GenerateOnTerrain` 保持一致，不弹窗中断整批。

### 5. 公共脏标记工具
- 提取 `MarkSceneDirty()` 至 `Editor/Utils/SceneUtils.cs`（或复用现有工具类），窗口与工具共享调用，减少重复与遗漏。

### 6. 代码风格与提前返回审计
- 通盘检查 Controllers/Services/Tools 中的判空与边界，保持“最先返回”写法一致；统一命名与属性暴露方式（只读快照与方法式改造）。

## 验证计划
- 在 Unity 2021.3.18f1 下验证：
  - 切换工具/窗口模式下绘制一致性（笔触、间距、事件消费）。
  - 缺映射时绘制不中断且仅一次提示；生成路径与绘制路径提示一致。
  - 选中多地形与未选择时回退策略正确；Overlay 与窗口状态联动正常。
  - 随机种子驱动的可复现性测试（重复笔刷操作得到同分布）。

## 变更范围与影响
- 主要涉及：`MTPBrushTool.cs`、`SceneInteractionService.cs`（参数策略）、`BrushPainter.cs`（缺映射分支）、`MrTerrainPainterWindow.*.cs`（清理旧方法）、公共工具类新增（脏标记）。
- 对外 API 不变；用户交互更一致，绘制与生成体验统一；维护复杂度降低。
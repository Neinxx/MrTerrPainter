## 初始化与流程
- 我是资深Unity开发代码优化专家，将按“审查→拆分→优化→现代化→API替换”五步工作流执行。
- Unity版本：`2021.3.18f1`；UI采用UITK最佳实践；严格遵循提前返回与单一职责。

## 改造范围
- 目标模块：EdgeLine/LandScape封边石绘制与生成、VegetationProfile配置、Brush系统与预览。
- 涉及文件（核心）：
  - `Runtime/Profiles/VegetationProfile.cs`、`Runtime/Profiles/VegetationItem.cs`
  - `Editor/Services/BrushPainter.cs`、`Editor/Services/BrushEngine.cs`、`Editor/Services/VegetationGenerator.cs`
  - `Editor/Services/FacadeDetectionService.cs`、`Editor/Services/SceneInteractionService.cs`
  - `Editor/Tools/MTPBrushOverlay.cs`、`Editor/Views/BrushView.cs`、`Editor/MrTerrainPainterWindow.Control.cs`

## 参数系统重构（Offset/Scale/Spacing）
- 简化方案：统一引入 `EdgeStoneParams`（仅含 `Offset`、`Scale`、`Spacing`），与 `VegetationItem` 的其它旧参数解耦。
- 独立性保障：三参数互不影响；`Offset` 仅影响局部位移，`Scale` 仅影响局部缩放（保持比例），`Spacing` 仅影响采样间距与最小距离。
- 动态范围校验：
  - 定义 `Range<T>` 与 `Validate()`；在变更时执行提前返回：越界→钳制并警告；非法值（`<=0` 的Spacing/Scale）→回退默认。
  - UITK字段使用 `RegisterValueChangedCallback` 同步触发预览刷新（`BrushSettings.ChangedKey("EdgeStoneParams")`）。
- UI交互：Brush面板与Overlay仅暴露三核心参数，提供实时数值提示与单位标签（m/%）。

## VegetationProfile增强（多块封边石 + 权重分布）
- 结构：在 `VegetationProfile` 中新增 `EdgeStoneSet`（列表），元素包括 `PrefabRef`、`Weight`、可选 `SpacingOverride`（为空则继承全局）。
- 权重算法：
  - 归一化 `w_i' = w_i / Σw_i`；采样时以 `CDF` 做类别选择，`O(1)` 二分或Alias表支持大场景。
  - 与间距兼容：候选点按全局 `Spacing` 生成，再在放置阶段依据 `SpacingOverride` 做局部去重网格。
- 混合使用：单次采样决定类型，实例化时应用各自的 `Offset/Scale` 与贴合策略；支持不同材质与碰撞体。
- 提前返回：空集合或总权重=0→直接返回并提示；Prefab缺失→跳过该条目。

## 几何精度与自动校正
- 双轨绝对平行：
  - 基于基线向量 `t` 与法线 `n`，两轨为 `p ± d * n`；`d` 为半轨距，使用双精度计算后再落回 `Vector3`。
  - 放置前对每个候选点执行平行校验：若误差 `|n·(t×up)| > ε`，自动正交化 `n ⟂ t` 并重建候选。
- 立面严格垂直：
  - 实例旋转使 `localUp` 对齐世界 `Vector3.up`；若模型有倾斜，应用额外校正四元数；容差 `ε=1e-4`。
- 自动校正机制：
  - 预放置阶段 `GeometryConstraints.Apply(...)`：并行Job修正 `parallel` 与 `vertical`，钳制 `Offset` 投影到合法方向（Right/Up）。
  - 失败策略：超过最大校正步数或法线不可用→提前返回并标记候选无效。

## 笔刷系统改进（LandScape专用形状库）
- 新库：`BrushShapeLibrary`（Line、Ribbon、Arc、SplineStrip、Step、Chevron）。
- 参数化：通用 `size/strength/hardness` 外，新增 `width/railDistance/curvature/stepHeight` 等；所有形状提供采样函数 `Sample(u)`。
- 融合逻辑：
  - `BrushEngine` 的分布模块增加 `ShapeSampler` 接口；EdgeLine模式从形状轨迹产生候选点，与 `Spacing` 一致。
  - 预览：`BrushPainter.DrawPreview` 增加形状路径渲染；与立面切片预览无缝拼接。

## 预览联动与UITK最佳实践
- 事件驱动：所有核心参数变更→触发 `BrushSettings.ChangedKey`；`BrushView.Bind` 响应刷新；`SceneInteractionService.RenderBrushPreview` 在 `Repaint` 执行。
- 校验反馈：数值越界在UITK中以 `HelpBox` 或 `InlineWarning` 提示；提供实时单位换算与钳制显示。
- 性能防抖：UI侧 50–100ms 防抖，避免频繁重绘；大场景预览采用简化几何线段。

## 单元测试与压力测试
- EditMode测试：
  - 参数验证：`Offset/Scale/Spacing` 越界钳制与提前返回。
  - 权重分布：归一化正确性、CDF采样一致性（卡方检验）。
  - 几何精度：双轨平行度（误差<`1e-6`）、立面垂直度（夹角<`1e-6` rad）。
- PlayMode测试：
  - 复杂地形（高坡度/断崖）：生成稳定性与自动校正成功率；大规模场景（>100k实例）。
- 压力测试：
  - 性能计时与内存峰值；分块生成与对象池命中率；UI预览帧率≥60FPS。

## 性能优化策略
- 分布采样：Alias采样加速权重选择；Poisson候选批次化；`NativeArray` + Burst Job 并行过滤。
- 去重网格：分层多Grid（全局Spacing与局部SpacingOverride）避免互相污染；哈希网格减少内存。
- 实例化：对象池与批处理变换（`TransformAccessArray`）；减少GC分配。
- 预览：LOD简化线段与点集，降低绘制开销。

## 交付物
- 逻辑流程图：EdgeLine→候选生成→几何校正→权重选择→实例化→预览刷新。
- 技术文档：参数说明、形状库接口、权重算法与几何校正规范、性能与测试报告。
- 单元测试：覆盖参数、分布、几何与性能要点；自动化脚本与基准数据。

## 实施步骤
- 第1步：在Profile/Item中引入 `EdgeStoneParams` 与 `EdgeStoneSet`，完成UI绑定与校验。
- 第2步：实现权重分布与Spacing兼容；加入提前返回路径。
- 第3步：几何约束Job与自动校正；更新生成与预览调用链。
- 第4步：形状库与BrushEngine对接；预览渲染优化。
- 第5步：编写与运行测试；优化性能并生成报告。

请确认以上方案，我将开始具体实现。
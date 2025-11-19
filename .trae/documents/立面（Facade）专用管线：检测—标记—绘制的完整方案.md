## 变更目标
- 检测不到立面时直接提示并阻止绘制（不回退）
- 定义新类型专用于立面封边石（与 Landscape 区分开）
- 清理过时/不正确方法，统一用“立面检测→标记→绘制”管线
- 在场景中用 Gizmo/Handles 标记立面（成功后才能绘制）

## 数据与类型
- PrefabType 扩展：新增 `FacadeStone`（仅走立面管线）。Mapping 中可绑定父节点。
- VegetationItem 扩展（FacadeStone 生效）：
  - `edgeSlopeEnter`（进入阈值，默认 30°）、`edgeSlopeExit`（退出阈值，默认 25°）
  - `probeStep`（水平探测步长，默认 0.3m）、`probeMaxDist`（最大探测距离，默认 6m）
  - `referenceHeightMeters`（资产参考高度，默认 1m）、`offsets`（XYZ 偏移，本地条带坐标系）

## 立面检测服务（新）
- 新增 `FacadeDetectionService`（Editor/Services/）：
  - `TryDetectFacade(Terrain t, Vector3 foot, out FacadeInfo info)`：
    - 计算 `forward = Normalize(ProjectOnPlane(nFoot, up))`、`right = Cross(up, forward)`
    - 双向扫描：
      - 自 `foot` 沿 `-forward` 找到“上边界 topPos”（从陡峭→平缓，slope < exit）
      - 自 `foot` 沿 `+forward` 找到“下边界 bottomPos”（从陡峭→平缓，slope < exit）
    - 两阶段搜索：粗步进扫描 + 区间二分精化（步长压至 0.05m）
    - 微分降噪：每采样点取 `pos±right*ε` 与 `pos` 的坡度均值（ε=0.2m）
    - 返回 `FacadeInfo { topPos, bottomPos, heightMeters, forward, right }`
  - 无法定位边界则返回 false

## 场景标记（Gizmo/Handles）
- 在 `SceneInteractionService.RenderBrushPreview` 中，当选择 `FacadeStone` 且 EdgeLine 模式：
  - 调用 `TryDetectFacade`
  - 成功：用 Handles 画：
    - 立面线段（top/bottom 红/绿）、法线箭头、条带横向刻度
  - 失败：在 Scene GUI 用 `Handles.Label` 提示“当前笔刷下未检测到立面（坡度不足或探测范围不足）”并阻止绘制

## 绘制与缩放（仅在检测成功时执行）
- 刷涂/生成路径（BrushPainter/VegetationGenerator）：
  - 候选点：基于 `FacadeInfo.right` 按 `minSpacing` 在条带长度（笔刷直径或固定）等距采样，Pivot 的 y 设为 bottomPos 的高度
  - 旋转：`LookRotation(forward=FacadeInfo.forward, up=Vector3.up)`（+Z 指向墙面法线外向，+Y 朝上），yaw=0
  - 插入：仅水平沿 `-forward` 偏移（不改 y），再应用 `offsets`（X沿right、Y沿up、Z沿 -forward）
  - 自动高度：`localScale.y = heightMeters / referenceHeightMeters`，X/Z 保持条目原始缩放或条带需要

## 清理与统一
- 移除/停用过时方法：
  - `VegetationGenerator.SampleEdgeLine`（旧 marching squares 线段采样）→ 统一用 FacadeDetectionService
  - 刷涂中的“单向外找顶”插入逻辑 → 替换为上下边界双向探测
  - EdgeLine 下禁用 `jitter/adaptive/cluster` 路径（保持条带规则）

## 验证与提示
- 检测成功：标记显示，允许绘制；撤销与对象池正常
- 检测失败：场景提示，不绘制；日志节流与 UI 显示一致（Window/Overlay）
- 参数可调：enter/exit阈值、步长与最大距离、参考高度与偏移

## 交付步骤
1) 增加 `PrefabType.FacadeStone` 与 VegetationItem 扩展字段
2) 新增 `FacadeDetectionService` 并在 SceneInteractionService 预览中调用与标记
3) 调整 BrushPainter/VegetationGenerator：EdgeLine+FacadeStone 走“检测→标记→绘制”
4) 移除旧 EdgeLine 采样函数与不正确插入逻辑，保证单一管线
5) 验证场景交互与性能，微调默认参数
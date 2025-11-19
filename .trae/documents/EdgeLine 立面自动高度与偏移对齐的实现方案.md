## 目标
- 自动获取立面（悬崖）高度，并将封边石的局部 Y 尺寸缩放到该高度
- 提供 XYZ 偏移（offset），确保：Pivot 贴合地面、-Z（本地）贴合墙面、+Z 指向法线、+Y 朝上
- EdgeLine 模式开启时禁用抖动/自适应等，完全由条带与立面规则驱动

## 数据扩展
- VegetationItem：
  - edgeAutoHeight: bool（默认 true）
  - edgeReferenceHeightMeters: float（资产参考高度，默认 1）
  - edgeOffsets: Vector3（XYZ 额外偏移，默认 (0,0,0)）
  - edgeLookAheadStep: float（沿 -forward 的水平探测步长，默认 0.5m）
  - edgeMaxLookAhead: float（探测最大距离，默认 5m）

## 立面高度探测（EdgeLine 专用）
- 以条带中每个候选点的脚点（foot）为基准：
  - 获取脚点高度与法线：hFoot, nFoot；forward = ProjectOnPlane(nFoot, up).normalized
  - 沿水平 -forward 方向迭代：for d = step .. maxLookAhead
    - 取测试点 pTop = foot + (-forward) * d
    - 采样高度与法线（TryGetHeightAndNormal）并计算 slopeTop
    - 当 slopeTop < edgeSlopeThreshold（或法线与 forward 的夹角达到阈值）判定“崖顶”
  - 若找到崖顶：heightMeters = hTop - hFoot；否则回退为 2*brush.size 或固定长度

## 缩放与对齐
- 缩放：localScale.y = heightMeters / edgeReferenceHeightMeters（X/Z 保持条目或条带规则）
- 位置：
  - Pivot 贴地：p.y = hFoot
  - -Z 贴墙：在设置旋转后，沿水平 -forward 偏移 embedDepth + edgeOffsets.z
  - 额外偏移：在本地空间（转换到世界）应用 edgeOffsets.xyz（X 沿 right，Y 沿 up，Z 沿 -forward）
- 旋转：
  - baseRot = LookRotation(forward, up)
  - yaw 绕 forward（条带 yaw 或 0）
  - 保持 +Z 指向 forward，+Y 朝上

## 代码改造点
- BrushPainter/VegetationGenerator（EdgeLine 分支）：
  - 在条带候选生成后，放置每个点时执行“探测高度→缩放 Y→对齐与偏移”逻辑
  - 简化/忽略抖动与自适应参数
- CreateInstance：
  - 支持在 PlacementOverrides 中传入 scaleRange（Y 专用或统一），或在实例创建后直接设置 localScale.y
  - 位置与旋转按前述顺序：先贴地→设置旋转→应用水平 -forward 插入与 offsets

## 验证
- 在悬崖处 EdgeLine 刷涂：
  - 不同立面高度自动拉伸封边石 Y 尺寸
  - Pivot 紧贴地面，-Z 紧贴墙面，+Z 指向法线，+Y 朝上
  - 条带横向间距整齐，禁用抖动后整体平直

## 性能与稳健性
- 探测步长与最大距离控制迭代次数；找不到崖顶时回退长度
- 所有采样使用 TryGetHeightAndNormal（避免物理开销）

如确认，我将按上述方案扩展数据结构与 EdgeLine 放置逻辑，并在刷涂与批量生成路径中实现自动高度与偏移对齐。
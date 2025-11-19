## 目标
- 在 EdgeLine+FacadeStone 模式下：先检测立面→在场景标记→仅在检测成功后绘制。
- 检测不到立面时提示“不生成”，不做回退放置。
- 清理旧的 EdgeLine 采样与不正确插入逻辑，统一到立面管线。

## 新服务与数据
- 新增文件：Editor/Services/FacadeDetectionService.cs（仅编辑器端）
  - 提供：
    - struct FacadeInfo { Vector3 topPos; Vector3 bottomPos; float heightMeters; Vector3 forward; Vector3 right }
    - static bool TryDetectFacade(Terrain t, Vector3 foot, float enterSlope, float exitSlope, float step, float maxDist, out FacadeInfo info)
  - 算法：
    - 获取脚点法线 nFoot，forward=Normalize(ProjectOnPlane(nFoot, up))，right=Normalize(Cross(up, forward))
    - 双向扫描：沿 -forward 找上边界，沿 +forward 找下边界（粗步进）
    - 在每个候选区间做二分精化到步长≈0.05m
    - 微分降噪：对每次采样取 pos±right*ε 与 pos 三点坡度均值（ε≈0.2m）
    - 滞后阈值：enterSlope（进入陡峭）与 exitSlope（退出到平缓）分离
    - 成功返回 FacadeInfo，否则 false。

## 场景预览接入
- Editor/Services/SceneInteractionService.cs：RenderBrushPreview（或 OnSceneGUI 预览段）
  - 当当前条目 `PrefabType.FacadeStone` 且 `BrushSettings.distribution==EdgeLine`：
    - 以笔刷中心为脚点调用 TryDetectFacade
    - 成功：用 Handles 标记 top/bottom（红/绿）、forward 箭头（白）、条带刻度（青）；允许绘制
    - 失败：Handles.Label(center, "未检测到立面（坡度不足或探测范围不足）")，禁止绘制

## 绘制路径调整
- Editor/Services/BrushPainter.cs：
  - 在 Paint/ PaintMixed 中，若 `FacadeStone+EdgeLine`：
    - 先调用 TryDetectFacade；失败则 `continue`（不放置）
    - 生成候选点：沿 info.right 按 `item.minSpacing` 在条带长度等距采样，Pivot y=bottomPos.y
    - 旋转：LookRotation(info.forward, up)，yaw=0
    - 自动高度：localScale.y=info.heightMeters/item.edgeReferenceHeightMeters（X/Z 保持条目缩放）
    - 贴墙与偏移：水平沿 -forward 偏移 embedDepth+offset.z，X/Y 按 offsets
- Editor/Services/VegetationGenerator.cs：
  - 在 GenerateOnTerrain 的 EdgeLine 分支对 `FacadeStone` 使用相同逻辑（区域中心为脚点）

## 清理与统一
- 删除或停用：Editor/Services/VegetationGenerator.cs 中旧 `SampleEdgeLine` marching squares；
- 刷涂中的“单向外找顶”插入逻辑已替换为上下边界双向探测；
- EdgeLine 下禁用抖动/Adaptive/Cluster。

## 参数来源与默认值
- from VegetationItem（FacadeStone条目）：
  - enterSlope=30°、exitSlope=25°、probeStep=0.3m、probeMaxDist=6m、referenceHeight=1m、offsets=(0,0,0)

## 验证
- 检测成功：标记可视，封边石高度、朝向与贴合正确；撤销与池正常。
- 检测失败：提示，不生成。
- 性能：步长/最大距离可调，候选数量受条带步长与长度控制。
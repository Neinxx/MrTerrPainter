## 目标与约束
- 在 EdgeLine+FacadeStone 模式下：先检测立面→在场景标记→仅在检测成功后绘制；检测失败直接提示并阻止绘制。
- 清理旧 EdgeLine 采样与不正确插入逻辑，统一用立面管线；保持对象池、父节点映射与日志节流稳定。

## 新类型与数据
- PrefabType：新增 `FacadeStone`（Runtime/Profiles/PrefabType.cs）。
- VegetationItem（FacadeStone 专用字段）：
  - `edgeSlopeEnter`（默认 30°）、`edgeSlopeExit`（默认 25°）
  - `probeStep`（0.3–0.5m）、`probeMaxDist`（6–8m）
  - `referenceHeightMeters`（1m）、`offsets`（XYZ）

## 立面检测服务
- 新增 `Editor/Services/FacadeDetectionService.cs`：
  - `struct FacadeInfo { Vector3 topPos; Vector3 bottomPos; float heightMeters; Vector3 forward; Vector3 right }`
  - `static bool TryDetectFacade(Terrain t, Vector3 foot, float enterSlope, float exitSlope, float step, float maxDist, out FacadeInfo info)`
  - 算法：双向扫描（沿水平 ±forward）→ 找到陡峭→平缓过渡 → 区间二分精化到 0.05m；微分降噪（pos±right*ε 与 pos 的坡度均值）；滞后阈值避免抖动。

## Scene 预览接入
- `SceneInteractionService` 预览：
  - 当 `FacadeStone+EdgeLine`：调用 TryDetectFacade
  - 成功：Handles 标记 top/bottom（红/绿）、forward 箭头（白）、条带刻度（青）；允许绘制
  - 失败：`Handles.Label` 提示“未检测到立面（坡度不足或探测范围不足）”，并阻止绘制

## 绘制管线接入
- `BrushPainter.Paint / PaintMixed`：
  - `FacadeStone+EdgeLine`：
    - 先 TryDetectFacade；失败 `continue`
    - 候选点：沿 `info.right` 按 `item.minSpacing` 在条带长度等距采样；Pivot y=bottomPos.y
    - 旋转：`LookRotation(info.forward, Vector3.up)`；yaw=0（稳定对齐）
    - 自动高度：`localScale.y = info.heightMeters / item.edgeReferenceHeightMeters`；X/Z 保持条目缩放或条带规则
    - 贴墙与偏移：水平 `-forward` 偏移 `embedDepth + offsets.z`；再按 `X/Y` 偏移（X沿right、Y沿up）
- `VegetationGenerator.GenerateOnTerrain`：区域中心作为脚点执行同样逻辑

## 清理与统一
- 移除/停用：`VegetationGenerator.SampleEdgeLine` marching squares；刷涂中的单向外找顶逻辑改为双向检测；EdgeLine 下禁用抖动/Adaptive/Cluster。

## UI 与提示
- Mapping 类型枚举支持 `FacadeStone`；属性面板展示并绑定上述探测与偏移字段；检测失败时仅提示，不生成。

## 验证与默认值
- 验证：立面标记正确、封边石高度与对齐准确、撤销与池稳定；性能由步长与最大距离控制。
- 默认值：`enter=30°/exit=25°/step=0.3m/max=6m/referenceHeight=1m/offsets=(0,0,0)`。

如确认，我将按此计划提交代码：新增检测服务与类型、集成预览与绘制、清理旧实现，并完成场景验证与参数微调。
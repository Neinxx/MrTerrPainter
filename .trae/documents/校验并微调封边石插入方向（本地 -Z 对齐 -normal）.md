## 结论与现状
- 现有实现已满足：本地 +Z 与悬崖法线 `normal` 对齐，插入沿世界 `-normal`（即本地 -Z）进行。
- 代码位置：
  - 刷涂插入：Editor/Services/BrushPainter.cs:421–425、655–659（`p -= n.normalized * depth`）
  - 刷涂旋转：Editor/Services/BrushPainter.cs:759–767（`LookRotation(forward=normal, upOnPlane)`，再 `AngleAxis(yRot, forward)`）
  - 生成插入：Editor/Services/VegetationGenerator.cs:301–308（`sample -= n.normalized * depth`）
  - 生成旋转：Editor/Services/VegetationGenerator.cs:441–449（同旋转框架）

## 校验与微调计划
1) 校验插入方向
- 逻辑应为：`forward = normal`，则本地 -Z = `-forward`，插入偏移 `pos += (-forward) * depth` 等价当前 `pos -= normal * depth`。
- 验证在悬崖面上封边石外露比例与方向是否符合预期（本地 +Z 朝外，本地 +Y 朝上）。

2) 若需更强对齐
- 保证插入在旋转前执行（已满足），旋转后 `go.transform.forward ≈ normal`，`go.transform.up ≈ upOnPlane`。
- 如资产本地轴定义特殊导致偏差，可增加资产级 `yRot` 调整范围（绕 forward 轴），或在导入统一本地轴。

3) 可选增强
- 在封边石项启用调试可视化（仅编辑器预览）：绘制 forward/up 方向小箭头，便于快速核验。
- 增加 `embedDepthRange` 的上限提示（随资产尺寸调节）。

## 验证清单
- 在高坡刷涂并生成 Landscape 条目：
  - 本地 +Z 指向外（法线方向）；插入沿本地 -Z（世界 -normal）。
  - `yRot` 绕 forward 轴旋转副朝向；撤销与对象池正常。

如确认，我将执行核验并视需要加上轻量调试箭头或参数提示，不改动核心逻辑。
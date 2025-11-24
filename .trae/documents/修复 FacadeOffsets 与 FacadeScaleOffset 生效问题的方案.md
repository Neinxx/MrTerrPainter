## 问题定位
- Facade放置路径中部分位置使用了统一的 `CoreOffset` 与几何缩放，仅在 `PlaceFacadeSlices(...)` 路径应用了 `facadeScaleOffset`，但在 `VegetationGenerator.GenerateOnTerrain` 的 Facade分支未应用；
- 预览与生成的偏移在个别处使用了 `CoreOffset`（edgeOffsets优先），造成“Facade专用偏移（offsets）”被替代。

## 修复策略
- 明确区分：
  - Facade路径的偏移一律使用 `item.offsets`（Facade专用），不再走 `CoreOffset`。
  - Facade路径的缩放在两处都应用 `facadeScaleOffset`（非堆叠与堆叠）。
- 代码改动：
  1) `Editor/Services/BrushPainter.cs`：
     - 在 `PlaceFacadeSlices(...)` 中将 `offsConf/offsConf2` 改为 `item.offsets`；
     - 最终缩放 `finalScale = max(ε, uni + item.facadeScaleOffset)` 保持（已存在），确认两分支一致。
  2) `Editor/Services/VegetationGenerator.cs`（Facade检测分支）：
     - 非堆叠：`go.transform.localScale = Vector3.one * max(ε, uni + item.facadeScaleOffset)`；偏移使用 `item.offsets`；
     - 堆叠：每层 `finalScale2 = max(ε, uni2 + item.facadeScaleOffset)`；偏移使用 `item.offsets`。
  3) `BrushPainter.PaintMixed(...)` 的 Facade混合分支：
     - 偏移使用 `item.offsets`；缩放保持 `uni`，叠加 `item.facadeScaleOffset`。
- 兼容性：仅更改Facade路径的偏移与缩放来源，不影响非Facade/EdgeLine普通路径；无需迁移数据。

## 验证
- 在Editor测试中新增断言：
  - 读取 `VegetationItem.offsets` 与 `facadeScaleOffset` 后，实例位移与缩放与预期一致（包含堆叠与非堆叠）。
  - 混合与生成两条路径均生效。

请确认，确认后我将立即进行代码修改与测试。
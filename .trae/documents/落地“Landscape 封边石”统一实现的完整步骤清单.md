## 变更概览
- 数据类型：PrefabType 扩展 Landscape；VegetationItem 增加 edgeSlopeThreshold 与 embedDepthRange
- 放置逻辑：刷涂与批量生成路径基于 Landscape 触发“坡度筛选 + 插入偏移 + 强制法线对齐”
- UI绑定：Mapping 支持 Landscape；属性面板为 Landscape 条目显示与绑定新字段；（可选）Overlay 显示临时覆盖控件
- 验证：高坡刷涂/批量生成、对象池与撤销、父节点映射与节流日志、性能稳健

## 文件级实施步骤
### Phase 1：数据与逻辑
1) Runtime/Profiles/PrefabType.cs
- 新增枚举值：Landscape（用于 Mapping 类型选择）

2) Runtime/Profiles/VegetationItem.cs
- 新增序列化字段：
  - public float edgeSlopeThreshold = 75f
  - public Vector2 embedDepthRange = new Vector2(0.1f, 0.3f)
- 校验（IsValid 不变）；采样函数沿用已有 SampleScale/SampleYRotation；嵌入深度使用局部采样：Mathf.Lerp(range.x, range.y, (float)rnd.NextDouble())

3) Editor/Services/BrushPainter.cs
- 在 Paint 的放置循环（单 Profile）中，高度/法线与 slope 计算后：
  - if (item.prefabType == PrefabType.Landscape && slope < item.edgeSlopeThreshold) continue
  - 若 Landscape：embedDepth = Sample(range)；pos -= normal.normalized * embedDepth
  - 旋转：强制法线对齐（忽略全局 normalDirection），采用 LookRotation( Cross(right, n), n ) * YRot
- 在 PaintMixed 的放置循环对选中的 item 同样应用上述逻辑
- 其他逻辑（falloff、spacing、父节点解析、对象池、日志节流）保持不变

4) Editor/Services/VegetationGenerator.cs
- 在 GenerateOnTerrain 的放置循环（每个 item）：
  - 若 Landscape：按 slope 阈值筛选；pos 插入；强制法线对齐
- 候选采样/网格与父节点解析沿用现有实现

### Phase 2：UI绑定
5) Editor/Views/PropertyPanelView.cs
- 当当前条目 prefabType == Landscape：
  - 显示并绑定两个控件：
    - 滑条/FloatField：edgeSlopeThreshold（范围 0..90）
    - Vector2Field：embedDepthRange（范围 0..1 或按项目需要）
- 改动仅在 UI 层，使用 SetValueWithoutNotify + RegisterValueChangedCallback 更新 VegetationItem 字段并 SetDirty Profile

6) Editor/Views/Tabs/SettingsTabView.cs
- Mapping 类型枚举列表包含 Landscape（枚举扩展即可生效）
- 无需其他逻辑变更；保持 Save/Confirm 流程

7) （可选）Editor/Tools/MTPBrushOverlay.cs
- 当当前条目为 Landscape 时显示临时控件：
  - “插入深度”与“坡度阈值”滑条；写入到 Brush 的临时覆盖或直接到条目字段（推荐条目级优先）
- 保持其他控件与可见性逻辑一致

## 验证清单与方法
- 刷涂验证：
  - 在近垂直崖壁处，Landscape 条目出现；在缓坡/平地处不出现
  - 调节 edgeSlopeThreshold 与 embedDepthRange，封边石外露比例与出现范围随之变化
  - 撤销/重做与对象池复用正常（VegetationPool）
- 批量生成验证：
  - 选中地形批量生成 Landscape 条目，仅在高坡区域出现；父节点归档正确；缺失映射日志节流
- 性能验证：
  - 大半径与高密度下，ComputeDesiredCandidateCount 动态上限有效；帧率与交互流畅

## 交付与默认参数建议
- 默认 edgeSlopeThreshold：75 度（近垂直）
- 默认 embedDepthRange：0.1..0.3 米（按资产比例可调）
- 若资产尺寸较大，建议将 embedDepthRange 设为 0.2..0.5 米以保证“插入”观感

## 后续增强（可选）
- 新增 DistributionType.EdgeLine：在笔刷半径内对高度图做高坡边界提取（Marching Squares），沿折线等距采样，形成连续封边石带；保持统一候选上限与网格约束

请确认，我将按此步骤依次落地并在编辑器中验证每一项功能与 UI 行为。
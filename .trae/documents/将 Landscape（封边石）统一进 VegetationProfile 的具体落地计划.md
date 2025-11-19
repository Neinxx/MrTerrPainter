## 修改范围
- Runtime/Profiles/PrefabType.cs：新增枚举值 `Landscape`
- Runtime/Profiles/VegetationItem.cs：新增字段 `edgeSlopeThreshold`（默认75f）、`embedDepthRange`（默认0.1..0.3）
- Editor/Services/BrushPainter.cs：在 `Paint` 与 `PaintMixed` 的放置循环中对 `Landscape`：
  - 若 `slope < item.edgeSlopeThreshold` → continue
  - 采样 `embedDepth` 并 `pos -= normal.normalized * embedDepth`
  - 强制法线对齐（忽略全局 normalDirection）
- Editor/Services/VegetationGenerator.cs：在 `GenerateOnTerrain` 的放置循环应用同样规则
- Editor/Views/PropertyPanelView.cs：为 `Landscape` 条目显示并绑定 `edgeSlopeThreshold` 与 `embedDepthRange` 控件
- Editor/Views/Tabs/SettingsTabView.cs：Mapping 类型枚举支持 `Landscape`（UI侧无逻辑变更，仅枚举扩展）

## 实现要点
- 统一判断入口：以 `item.prefabType == PrefabType.Landscape` 触发规则
- 统一方式：刷涂与生成均复用现有的高度/法线获取、候选上限、spacing 网格、父节点解析与对象池复用
- 安全性：所有新增字段使用提前返回与范围夹取；日志沿用已有节流入口（缺失映射时）

## 验证清单
- 在高坡面刷涂 Landscape：仅高坡出现，插入深度与法线对齐正确
- 批量生成：选中地形内的近垂直面批量生成封边石，撤销/重做与对象池正常
- UI：当选中 Landscape 条目时能编辑插入深度与坡度阈值，Overlay 可选全局覆盖（保留条目级优先）

## 交付顺序
1) 枚举与数据字段扩展
2) Painter/Generator 行为修改
3) PropertyPanel UI 绑定
4) 逐项验证并微调默认参数
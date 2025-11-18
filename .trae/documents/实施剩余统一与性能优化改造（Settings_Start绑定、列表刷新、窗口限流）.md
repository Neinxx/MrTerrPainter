## 改造目标
- Settings/Start 页统一使用按 name 的 SetClickHandler，并在重建时解绑旧回调
- UI 局部刷新：减少 ListView.Rebuild()，选择变化仅更新样式，使用 UIThrottle.schedule 节流
- Window 请求重绘加入最小间隔限流（与 Overlay 一致）

## 具体修改
1) Settings/Start 页按钮绑定
- 在 MrTerrainPainterWindow.Start.cs 与 Views/Tabs/SettingsTabView.cs：
  - 按 name 查询控件（如 "Painting"、"Generate"、"Settings"）
  - 用 UIElementExtensions.SetClickHandler 绑定点击，替换直接 +=
  - 重建页面时统一解绑旧回调（SetClickHandler 已内置）

2) Vegetation/Profile 列表刷新
- 在 MrTerrainPainterWindow.Control.cs:
  - RefreshVegetationListUI：移除 ListView.Rebuild()，仅设置 itemsSource（UI Toolkit会刷新）；必要时添加轻量 schedule 以更新选中样式
- 在 PreviewGridView.Render：
  - 保留首次构建；后续只更新 itemsSource，不调用 Rebuild
  - 选择变化与菜单操作后，调用 UpdateSelectionVisuals（已 schedule）

3) Window 重绘限流
- 在 MrTerrainPainterWindow.RequestSceneRepaint：
  - 增加 _lastRepaintTime 字段，限制 16ms 最小间隔
  - 与已有 sceneRepaintQueued 标志配合，避免过密队列

## 验证
- Settings/Start 按钮在多次重建后不重复触发；切换 Painting/Generate 正常
- Vegetation/Profile 列表在增删条目时刷新，选择切换无闪烁
- 快速操作下窗口与 Overlay 重绘无抖动，交互顺畅

## 变更范围
- Editor/MrTerrainPainterWindow.Start.cs
- Editor/Views/Tabs/SettingsTabView.cs
- Editor/MrTerrainPainterWindow.Control.cs
- Editor/Views/PreviewGridView.cs
- Editor/MrTerrainPainterWindow.cs（重绘限流）

如确认，我将按上述文件逐项提交改动并在编辑器内逐个验证。
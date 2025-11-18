## 目标
- 高性能渲染与刷新：大数据量下保持流畅（虚拟化、节流、增量刷新）
- 单一职责与统一数据源：数据检索、选择状态、UI绑定分离
- 完善交互：拖拽、对象选择器、右键菜单、批量操作可用且稳健

## 最佳实践要点
- 数据源快照：窗口维护 `availableProfiles` 与 `currentProfile.Items` 的只读快照，任何改动后重建快照（避免引用共享产生UI不同步）。
- UI Toolkit ListView 虚拟化：
  - Profile 列表与预制体预览列表均启用 `FixedHeight` 虚拟化；仅在“数据数量变化或分页变化”时 `Rebuild`，其余场景使用 `RefreshItems` 与局部样式更新。
- 选择状态管理：
  - 使用独立的选择管理器：单选、多选（Ctrl/Shift）、右键选中修正；将“选中索引集合”与“当前索引”存储在状态对象中（已存在 `EditorState`）。
- 事件绑定模式：
  - 统一使用 `SetClickHandler` 防重复绑定；对象选择器统一在 `EditorWindow.OnGUI` 处理 `ObjectSelectorClosed` 并路由到控制器。
- 预览缓存：
  - AssetPreview 缓存按 `prefab.GetInstanceID()` 建立哈希；当返回 null 时使用 `delayCall/schedule` 异步重试；在 `Render` 结尾调用 `UpdateSelectionVisuals`。
- 拖拽与空槽：
  - 新增入口支持拖拽 GameObject 数组；空槽点击打开对象选择器；新增空槽时立即刷新列表并设置选中。
- 刷新策略：
  - 数据变更 → 重建快照 → ListView.itemsSource 替换 → 若数量变化则 `Rebuild`，否则 `RefreshItems` → 选择样式通过局部更新；
  - 选择变更 → 仅局部样式更新（不重建数据）。

## 技术实现方案
### 1) 列表适配器与增量刷新
- 新建轻量 `ListAdapter`（内部工具类，不对外公开）：
  - 方法：`Bind(ListView lv, IList data, Func<VisualElement> make, Action<VisualElement,int> bind)`；
  - 刷新：`SetData(IList data)`（只替换数据源）；`RebuildIfCountChanged(int old,int now)`；`RefreshItems()`；
  - 使用场所：PreviewGridView、Profile List。

### 2) 选择管理器
- 管理选择集合与当前索引，提供：`SetSingle`, `Toggle`, `Range`, `Clear`，并触发 UI 更新回调；用在 ThumbList 与 PreviewGrid。

### 3) 对象选择器统一入口
- 在窗口 `OnGUI` 内处理 `ObjectSelectorClosed` 并调用 `prefabPicker.HandleObjectPickerClosed()`（已实现）；确保 “空槽点击/缩略图点击/新增区域点击” 都走这一入口。

### 4) 预览缓存服务
- 抽出到 PreviewGridView 内部私有服务：`GetPreview(Texture2D cachedOrNull)`；当返回 null 进行异步刷新，将 `RefreshItems()` 入队后再调用一次 `UpdateSelectionVisuals`。

### 5) 数据快照重建与刷新点
- Profile/Items 改动后：调用 `ReloadAvailableProfiles()` 与 `RefreshAllUI()`；
- 新增/删除/赋值：控制器调用刷新方法后统一用上述策略；
- 避免在选择变更时 `Rebuild`，仅更新样式。

## 落地改造文件（最小化改动）
- `Editor/MrTerrainPainterWindow.Control.cs`：
  - Profile 列表：用“数量变化才 Rebuild”的策略，选择变更仅样式更新（已部分实现）。
- `Editor/Views/PreviewGridView.cs`：
  - 仅分页/数量变化时 Rebuild，其余调用 `RefreshItems`；维护 `lastItemsCount/lastPageIndex`；缓存 `AssetPreview` 并在 null 时安排重试（已部分实现，需要补充重试）。
- `Editor/Views/ThumbListView.cs`：
  - 选择管理分离与空防御（已增强空防御）；右键菜单批量操作与类型设置保留。
- `Editor/MrTerrainPainterWindow.Start.cs`、`SettingsTabView.cs`：
  - 统一 `SetClickHandler`，避免重复绑定（已完成）。
- `Editor/MrTerrainPainterWindow.cs` 与 `Editor/Tools/MTPBrushOverlay.cs`：
  - 重绘最小间隔限流（已完成）。

## 验证清单
- 新增/删除预制体：预览列表即时刷新且无闪烁；分页标签正确。
- 空槽点击选择：选择器关闭后新增条目并选中；缩略图与类型标签立即更新。
- 拖拽多个预制体：逐条新增，列表刷新准确，选择样式不丢失。
- 快速切换 Profile：Profile 列表与预览列表数据正确，重建仅在数量变化时发生。

如确认，我将补齐 PreviewGrid 的预览异步重试逻辑与列表适配器轻量封装，同时复查 Profile 列表选择样式的局部刷新路径，确保在你的项目中统一一致的行为与性能。
## 缺陷清单（含证据）
- Overlay 下拉回调初始化时序问题：Editor/Tools/MTPBrushOverlay.cs:280–287，若回调未初始化就更新交互性，后续不会注册成功。
- CreateGUI 失败后的UI状态未复位：Editor/MrTerrainPainterWindow.cs:321–326，仅return，可能造成界面与内部状态不一致。
- 场景交互残留状态风险：Editor/Services/SceneInteractionService.cs:88–92，拖拽过程中切工具导致 _hasLastPaintPos 残留。
- VegetationPool 空间索引无清理入口：Editor/Services/VegetationPool.cs:13–15，场景切换可能累积。
- SettingsTabView 按钮点击无防重复绑定：Editor/Views/SettingsTabView.cs:228–229、312–313，未来改动易引入重复绑定。
- Preview 与 Profile 列表频繁 Rebuild：Editor/Views/PreviewGridView.cs:147–154；Editor/MrTerrainPainterWindow.Control.cs:569–575，UI重建成本较高。
- UI查询按文本匹配：Editor/Tools/UIElementExtensions.cs:19–28，易受文本变更影响。
- ThumbListView 右键操作未做空防御：Editor/Views/ThumbListView.cs:175–217，极端情况下可能空引用。

## 修复策略
### 1. UI回调初始化与解绑统一
- Overlay：在 SetupProfilesLogic 中先初始化 _onProfilesChanged，再调用 UpdateProfilesDropdownInteractivity；为 null 时显式返回。
- SettingsTabView/Start 页：统一使用 SetClickHandler（按 name 查询），并在重建时解绑旧回调，避免重复绑定。

### 2. UI重建优化
- PreviewGridView/VegetationList：仅在数据源变化时 Rebuild；选择变化用局部样式刷新；使用 UIThrottle.schedule 节流。

### 3. 场景交互稳健性
- SceneInteractionService：在 OnSceneGUI 开始处根据工具与事件类型（MouseLeaveWindow、工具变更）重置 _hasLastPaintPos。

### 4. VegetationPool 索引生命周期
- 增加 ClearTerrainIndex(Terrain) 与 ClearAllIndexes()；在窗口 OnDisable/OnDestroy 或场景清理入口调用。

### 5. API防御与查询改进
- UIElementExtensions：优先按 name 查询，文本匹配为回退。
- ThumbListView：右键菜单前增加 profile/索引空检查与提前返回。

## 性能增强
- VegetationPool 注册移除使用 HashSet 代替 List，提升存在性检查与移除速度；查询仍返回 List，必要时去重。
- delayCall 防抖加入最小间隔（如16ms）限流，减少UI闪烁与堆积。

## 交付步骤
1) 修复 Overlay 下拉回调初始化时序与统一SetClickHandler（小改，低风险）。
2) 引入 VegetationPool 清理API，并在窗口生命周期调用（中等风险）。
3) 优化 Preview/VegetationList 的重建策略与局部刷新（中等风险）。
4) SceneInteractionService 增强事件稳健性（小改）。
5) UIElementExtensions 改为优先name查询，ThumbListView加空防御（小改）。

## 验证
- Overlay Profile 切换正确响应；Settings页按钮行为无重复触发。
- 添加/删除 Profile 与条目时列表不闪烁、选择样式稳定。
- 切工具或移出窗口后再次绘制，笔刷间距正常。
- 清空/切换场景后对象池索引不残留，查询正常。

如同意，我将按上述顺序提交改动并逐项验证。
## 总览
- 目标：在不改变业务的前提下，优化 EditorWindow + UITK + Overlay 的UI框架，提升结构清晰度、交互响应和可维护性。
- 范围：Window页面装配、Views层数据绑定、Overlay面板、样式与资源管理、列表虚拟化与缓存策略、事件订阅生命周期。

## 框架现状
- 页面装配：窗口使用 `PageAssembler` 统一装配UXML与样式，并校验配置完整性（Editor/Utils/PageAssembler.cs:7-24, 26-41）。
- 视图层：`ControlView` 负责列表构建与绑定，支持拖放、批量选择、行模板（Editor/Views/ControlView.cs:42-83, 105-154, 156-290）。
- Overlay：`MTPBrushOverlay` 缓存控件引用、节流刷新、从Session与AssetDatabase加载Profile列表（Editor/Tools/MTPBrushOverlay.cs:93-117, 136-176, 286-346）。
- UI调度：统一使用 `root.schedule` 与 `EditorApplication.delayCall` 的封装（Editor/Utils/UIThrottle.cs:9-22）。

## 主要问题
- 资源加载不一致：Overlay与Window对Profile来源存在双路径（Session与AssetDatabase），易造成频繁扫描与状态不一致（Editor/Tools/MTPBrushOverlay.cs:300-314）。
- 列表虚拟化策略保守：`ListView` 使用 `DynamicHeight`，如行高一致可切换为 `Fixed` 提升性能（Editor/Views/ControlView.cs:69）。
- 事件订阅生命周期复杂：多处订阅回调，需系统化解绑与去重（Overlay已做，但Views与Window需统一策略）。
- 多处Q查询与回调注册分散：虽然Overlay做了缓存，但Views的行内绑定存在重复查找与重复注册风险（Editor/Views/ControlView.cs:166-173, 186-195, 216-232, 243-263）。
- UI更新一致性：部分UI更新通过 `RefreshAllUI`，缺少粒度更小的局部更新与批处理路径。

## 优化方案
### 1. 资源与数据来源统一
- Overlay的Profile加载优先依赖 `Session.AvailableProfiles`，仅在Session缺失时临时扫描；加入显式“数据来源状态”标识避免不一致。
- 在窗口初始化时广播可用Profile事件，Overlay监听更新，移除Overlay中的主动扫描路径（Editor/Tools/MTPBrushOverlay.cs:300-314）。

### 2. 列表与虚拟化优化
- 若行模板高度恒定，切换 `ListView.virtualizationMethod=Fixed` 并设置 `itemHeight`，减少布局计算成本（Editor/Views/ControlView.cs:69）。
- 缩略图区域采用对象池或增量刷新：对 `thumbs.Clear()` 改为复用元素并仅更新差异（Editor/Views/ControlView.cs:266-289）。

### 3. 事件订阅生命周期守卫
- 建立统一的订阅/解绑工具：在Views层引入 `SubscriptionGuard`（轻量包装），确保 `RegisterCallback` 与 `clicked` 在重绑定前统一解绑，避免重复回调（参考已有 `userData`存储策略，Editor/Views/ControlView.cs:92-103, 200-213, 221-232, 243-263）。
- 将窗口内的 `WindowStateChanged` 与配置事件转为单一分发点，Views与Overlay仅订阅其派生事件，降低耦合（Editor/MrTerrainPainterWindow.cs:52）。

### 4. UITK绑定抽象
- 为 `BrushSettings` 建立 `IBrushBinder` 适配器，将滑块/开关的绑定集中管理，减少在Overlay中的散布绑定（Editor/Tools/MTPBrushOverlay.cs:212-251, 265-280）。
- 引入轻量的“字段名映射+统一更新入口”复用 `BrushSettings.ChangedKey`，避免多处switch分发。

### 5. 懒加载与页面级缓存
- Window的Tab切换采用“按需装配与销毁”：首次进入某Tab时装配UXML，离开时仅隐藏并保留缓存以减少GC与布局抖动（Editor/MrTerrainPainterWindow.cs:106-122）。
- 对大型视图（如控制列表）做首屏骨架加载，然后异步补全缩略图与预览（结合 `UIThrottle.RunOnPanel`，Editor/Utils/UIThrottle.cs:14-22）。

### 6. 可见性与刷新批处理
- Overlay的 `UpdateOverlayDisplayState` 保留，增加批处理标志避免连续三处事件导致重复刷新（Editor/Tools/MTPBrushOverlay.cs:376-393）。
- 建立 `UIUpdateBatch`（简单队列+合并）在Window中集中触发 `RefreshProfileListUI/RefreshPreviewUI/UpdatePropertyPanel`，削减多次刷新串行（Editor/MrTerrainPainterWindow.cs:132-137）。

### 7. 样式与命名统一
- 在 `PageAssembler` 校验阶段增加缺失UXML的明确提示与引导按钮，减少“未找到UXML文件”静态文本（Editor/Utils/PageAssembler.cs:26-41）。
- 统一控件命名约定与ClassList前缀（mt-），减少选择器命名漂移；建立小型文档注释集合到代码顶部（不改运行时）。

### 8. 性能与缓存
- 继续使用 `AssetPreviewCache`，对缩略图加载引入失败重试与延迟刷新，避免主线程阻塞（Editor/Services/AssetPreviewCache.cs）。
- 在Overlay中对 `_profilesDropdown.choices` 的更新做“内容比对+仅增量更新”，避免每次重建列表（Editor/Tools/MTPBrushOverlay.cs:318-345）。

### 9. 验证与回归
- 增加Editor测试：
  - Profile列表虚拟化固定高度渲染与选择交互一致性；
  - Overlay中Profile来源统一的脏标记切换；
  - BrushBinder的字段更新正确驱动UI值变化。

## 实施步骤
1. 统一Profile来源与事件分发（Overlay移除主动扫描路径，改事件驱动）。
2. `ControlView` 切换虚拟化策略并引入缩略图增量刷新与元素复用。
3. 实现 `SubscriptionGuard` 与 `IBrushBinder`，替换散布的绑定回调。
4. Window Tab懒加载与页面缓存，批处理刷新接口抽象。
5. 增强 `PageAssembler` 的错误提示与引导控件。
6. 完成测试用例与基本性能对比（首屏渲染耗时与重绘频率）。

## 预期收益
- 列表渲染与重绘开销显著下降（固定高度虚拟化+增量刷新）。
- 事件订阅管理规范，避免重复绑定带来的隐性Bug。
- 预览/Overlay状态与窗口保持一致，减少“资源来源不一致”导致的UI问题。
- 代码可读性与可维护性提升，便于后续功能扩展。
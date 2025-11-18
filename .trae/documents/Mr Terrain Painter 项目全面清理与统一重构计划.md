## 目标与原则
- 按提前返回与单一职责原则清理冗余与不合理实现
- 统一“地形匹配 / 实例创建 / 采样管线”实现以减少重复与维护成本
- 优化擦除与生成性能，降低 UI 查询与大规模分布采样的开销
- 保持与 Unity 2021.3.18f1 API 兼容

## 架构巡检结论（摘要）
- 重复实现：地形匹配与实例创建在两处重复（BrushPainter 与 VegetationGenerator）
- 未使用方法：BrushPainter.RandomPointInBrush、BrushPainter.BuildWeightedList、BrushPainter.GetOrCreateContainer、UIElementExtensions.FindButtonByText
- 大方法职责混杂：BrushPainter.Paint、VegetationGenerator.GenerateOnTerrain 既做采样又做放置与日志
- 采样管线重复：Poisson/Cluster/Jittered/Natural/Halton/Adaptive 的调用在两处各自维护
- UI 与交互：部分事件多次 Query/注册，SceneInteractionService 同步渲染与输入处理

## 清理与统一改造
### 1. 移除未使用与冗余方法
- 删除未被引用的方法以降低代码噪音：
  - Editor/Services/BrushPainter.cs:793-804 RandomPointInBrush
  - Editor/Services/BrushPainter.cs:806-817 BuildWeightedList
  - Editor/Services/BrushPainter.cs:829-838 GetOrCreateContainer
  - Editor/Tools/UIElementExtensions.cs:19-29 FindButtonByText

### 2. 统一“地形匹配”逻辑
- 保留 VegetationGenerator.MatchTerrain（Editor/Services/VegetationGenerator.cs:343-351）作为唯一入口
- 在 BrushPainter 中移除本地重复实现（Editor/Services/BrushPainter.cs:819-827），并将所有匹配调用改为 VegetationGenerator.MatchTerrain

示例（替换调用）：
```csharp
// 旧：BrushPainter.cs:419
if (!MatchTerrain(item, h - terrain.transform.position.y, slope, ov)) continue;

// 新：统一调用
if (!VegetationGenerator.MatchTerrain(item, h - terrain.transform.position.y, slope, ov)) continue;
```

### 3. 统一“实例创建”流程
- 以 VegetationGenerator.ResolveTargetParent（Editor/Services/VegetationGenerator.cs:355-373）为唯一父节点解析策略
- 统一创建流程：用 VegetationPool.Get + 标注 VegetationInstance，避免双实现偏差
- 删除 BrushPainter 中未使用/回退型容器逻辑（GetOrCreateContainer）

示例（保持一致的旋转与法线对齐）：
```csharp
// BrushPainter.cs:433 后续创建前
var targetParent = VegetationGenerator.ResolveTargetParent(terrain, item);
if (targetParent == null) { missingTypesLogged.Add(item.prefabType); continue; }
CreateInstance(item, p, n, terrain, it, targetParent, rnd, ov);
```

### 4. 抽取并重用“采样管线”
- 将分布采样的选择统一为调用 BrushEngine（已具备完整族）：
  - Paint/Generate 两处均使用一致的分支与参数（PoissonBurst 优先，非 Burst 回退）
- 抽取候选点构建为局部私有函数，减少冗余 switch 内容（不新建文件，分别在两个调用点本地私有化，签名一致）

示例（私有统一方法签名）：
```csharp
private static List<Vector2> BuildCandidates(Vector2 centerXZ, float radius, BrushShape shape,
    int desired, float spacing, float jitter, int seed, DistributionType type, bool useBurst,
    ClusterSettings cluster, float minF, float maxF, float noiseW, System.Random rnd)
```

### 5. 场景交互与工具入口优化
- MTPBrushTool 初始化逻辑前移到 OnActivated（Editor/Tools/MTPBrushTool.cs:20-23），OnToolGUI 仅驱动 sceneService.OnSceneGUI
- SceneInteractionService 保持预览与输入拆分（已分拆：RenderBrushPreview 与 HandlePaintMouse），强化提前返回与模式判断

示例（工具激活初始化）：
```csharp
public override void OnActivated()
{
    // 构建 sceneService（与 OnToolGUI 内重复代码合并）
}
public override void OnToolGUI(EditorWindow window)
{
    var sceneView = window as SceneView; if (sceneView == null) return;
    sceneService.OnSceneGUI();
}
```

### 6. UI 与配置守卫
- Overlay：缓存控件引用后统一注册，避免重复查询（已具备缓存；仅清理重复调用 UpdatePanelFeatureVisibility）
- 保持 ConfigTools.GuardAndOpenSettingsOnlyIfIncomplete 作为唯一入口守卫（Editor/Config/MrTerrainPainterConfig.cs:299-318），已在 Overlay/Window 两处使用

### 7. 空引用与事件清理
- MTPBrushContext 删除别名 PruneExtraNulls（Editor/Tools/MTPBrushContext.cs:112），保留 PruneExtrasNulls 并更新外部引用（Window 已使用 PruneExtrasNulls，Editor/MrTerrainPainterWindow.cs:159）
- 检查所有事件订阅/退订路径，避免重复注册（Overlay 与 Window 已有 _subscribed/handler 保护）

## 性能优化要点
- 候选点上限策略：根据半径与当前密度动态估计 desired，上限受 bs.maxPoints / filter.maxPoints 控制
- 擦除路径：优先 VegetationPool.QueryInRadius，尽量避免 Physics.OverlapSphere 回退（BrushPainter.cs:666-674）
- 父节点映射缓存：在批量生成/绘制循环前构建 type->Transform 缓存，减少 ResolveTargetParent 开销与日志刷屏

## 验证与回归
- 手动用例：
  - 单地形/多地形笔刷绘制与擦除（间距与硬度，法线对齐指示线）
  - Overlay 选择 Profile、尺寸/密度滑条联动、窗口模式切换
  - 生成模式：笔刷区域生成 + 噪声过滤 + 放置覆盖范围
- 回归关注：Undo 记录与对象池显示切换（VegetationPool.ApplyShowInHierarchyAll），日志仅去重输出缺失映射类型

## 交付方式
- 保持现有命名空间与文件结构，不新增文件
- 变更集中在以下文件：
  - Editor/Services/BrushPainter.cs（清理、统一匹配与创建、候选私有化）
  - Editor/Services/VegetationGenerator.cs（候选私有化、缓存与日志去重）
  - Editor/Tools/MTPBrushTool.cs（初始化前移）
  - Editor/Tools/MTPBrushContext.cs（删除别名）
  - Editor/Tools/MTPBrushOverlay.cs（微调重复调用）

如同意上述计划，我将按模块逐个提交改动，每一步完成后进行可视化与功能验证，并确保不引入新问题。
# 重构目标
- 提升植被布局的自然度与专业度：支持蓝噪声/泊松盘、簇集分布、硬度曲线、阈值噪声等现代特性。
- 保持可维护与高性能：提前返回、单一职责、减少GC、可复用的空间索引。
- UI 与交互现代化：便捷调整分布参数、预览更直观。

## 架构改造
### 模块拆分
1. BrushEngine（新）：负责一次笔刷绘制的全流程（采样→过滤→放置）。
2. Samplers：
- PoissonDiskSampler（蓝噪声，Bridson 算法，最自然间距）
- ClusterSampler（Neyman-Scott 簇集分布：先采样簇中心，再采样簇内点）
- JitteredGridSampler（抖动网格：整齐但略带随机，适合人工造景）
3. Evaluators：
- HeightSlopeEvaluator（高度/坡度过滤）
- NoiseMaskEvaluator（按噪声灰度与阈值进行门限/概率接受）
- EdgeFalloffEvaluator（根据硬度曲线计算边缘接受度）
4. Placement：
- SpatialHash（统一空间网格，XZ 平面近邻约束）
- ParentResolver（复用 `ResolveTargetParent`）
- InstancePlacer（缩放/旋转/法线对齐/对象池）

### 数据扩展
- BrushSettings 扩展：
  - `AnimationCurve falloffCurve`（默认 linear；越靠边缘越弱）
  - `float minSpacingJitter`（对 `minSpacing` 轻微抖动，避免机械感）
  - `DistributionType distribution`（PoissonDisk/Cluster/JitteredGrid/Uniform）
  - `int strokeSeed`（每次笔触种子，默认取 `profile.randomSeed` 衍生）
  - `int maxPoints`（每笔最大候选数，防止过载）
- ClusterSettings（新）：
  - `int clusterCount`、`int childPerCluster`、`float clusterRadius`、`float childJitter`
- NoiseSettings 强化：`threshold` 生效（门限过滤）；支持 `strength` 将噪声参与接受概率权重（可保持现有 UI，不新增字段时默认 `strength=1`）。

## 算法设计
### 绘制流程（BrushEngine）
1. 候选采样：按 `distribution` 生成笔刷内候选点（圆/方按 `BrushShape`）。
   - PoissonDisk：半径取条目 `minSpacing`（加入 `minSpacingJitter`）；自然分布。
   - Cluster：簇中心基于 PoissonDisk；每簇 `childPerCluster` 在 `clusterRadius` 内随机。
   - JitteredGrid：规则网格 + 随机偏移，适合行列式种植。
2. 边缘硬度：使用 `falloffCurve.Evaluate(rNormalized)` 计算接受概率，中心=1，边缘趋近0。
3. 噪声与门限：
   - 获取 `nv=FractalNoise`；若 `invert` 则 `nv=1-nv`。
   - 门限模式：`if (nv < threshold) continue;`
   - 概率模式：`if (rnd.NextDouble() > nv) continue;`（可用 `nv*strength`）
4. 高度/坡度过滤：复用 `MatchTerrain`（条目/覆盖），提前返回。
5. 间距约束：统一 `SpatialHash` 检查近邻，跨条目复用数据结构减少 GC。
6. 放置：
   - 解析父节点，缺失仅一次报错后跳过。
   - 条目级缩放/旋转采样；按需法线对齐。
   - 对象池放置，填充 `VegetationInstance`。

### 生成流程改进（VegetationGenerator）
- 生效 `threshold`：当前 [VegetationGenerator.cs:230–238] 仅按灰度概率接受，应增加门限过滤。
- 可选采样器统一：在 `GenerateOnTerrain` 内可选使用 PoissonDisk 对 `minSpacing` 控制，减少后续剔除。
- 噪声参与高度/坡度联合过滤：支持“启用噪声时先门限，再高度过滤”。

## 代码改动点
- `Editor/Services/BrushPainter.cs`：
  - 重构 `Paint(...)`（74–131）：改为调用 `BrushEngine.PaintStroke(...)`，内部使用所选采样器与评估器。
  - 保留 `DrawPreview(...)`（59–71），新增“候选点预览”（小点阵，随 `preview` 开关）。
  - 替换 `RandomPointInBrush(...)`（255–266）为采样器实现；保留作 Uniform 备用。
  - 继续使用对象池 `VegetationPool.Get/Recycle` 与 `MatchTerrain(...)`（281–289）。
- `Editor/Services/VegetationGenerator.cs`：
  - 在 `GenerateOnTerrain(...)`（175–272）加入：
    - 门限过滤：`if (noiseEnabled && nv < ns.threshold) continue;`
    - 可选采样器：当 `item.minSpacing>0` 时使用 PoissonDisk 采样候选；或保留现有均匀采样 + Grid 剔除（通过参数选择）。
- `Editor/Views/BrushView.cs` 与 Paint 页 UXML：
  - 添加并绑定 `falloffCurve`（`CurveField`）、`minSpacingJitter`（`Slider`）、`distribution`（`EnumField`）、`strokeSeed`（`IntegerField`）、`maxPoints`（`IntegerField`）。
  - Cluster 参数：在“高级”折叠下提供 3–4 个字段。
- 无需第三方库；若后续允许，引入 `Unity.Mathematics` + `Jobs` 可进一步加速采样与过滤。

## 兼容与默认行为
- 默认 `distribution=Uniform`，`falloffCurve=linear`，`threshold=0.5`；保持与当前感觉接近，避免突变。
- 启用 PoissonDisk/Cluster 才获得更自然分布；参数默认为保守值。

## 验证方案
- 统计最近邻距离分布（KNN 直方图）对比 Uniform 与 PoissonDisk（预期泊松盘更均匀）。
- 可视预览：开启“候选点预览”，观察边缘衰减与噪声门限效果。
- 性能压测：1000–5000 点采样在编辑器下耗时与内存；确保无明显 GC 峰值。

## 交付与步骤
1. 添加 BrushEngine 与采样器/评估器基础类；实现 PoissonDiskSampler 与 EdgeFalloffEvaluator。
2. 重写 BrushPainter.Paint，接入 BrushEngine；保持擦除逻辑不变。
3. VegetationGenerator 应用 threshold/可选采样器；兼容旧逻辑。
4. 更新 BrushView 与 Paint UXML，新增高级参数控件；保持 UI 风格一致。
5. 添加单元/可视测试，并在示例地形上验证布局自然度与性能。

## 关键参考（定位）
- 绘制入口：`Editor/MrTerrainPainterWindow.cs:682–694`（调用 `BrushPainter.Paint`）
- 现有绘制实现：`Editor/Services/BrushPainter.cs:74–131`、预览 `59–71`、随机点 `255–266`
- 生成实现：`Editor/Services/VegetationGenerator.cs:175–272`、噪声 `275–294`、当前噪声接受 `230–238`

如确认上述方案，我将开始分步骤实施并提供验证结果。
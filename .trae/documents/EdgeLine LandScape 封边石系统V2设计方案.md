## 目标
- 立面识别：多尺度坡度+Canny+连通段+高度阈值+二分精化+平滑，稳定抗噪。
- 生成与预览：Strip/Ribbon/Arc/SplineStrip 形状；双轨并行与刻度美观、性能≥60FPS。
- 参数极简：Spacing/Offset/Scale/RailWidth 四核心；高度阈值与形状参数直观可控。

## 技术实施
### 立面识别管线（FacadeDetectorV2）
- 输入：地形补丁（现用 `FetchHeightsBlock`），笔刷中心、半径、enter/exit阈值、`minFacadeHeightMeters`。
- 步骤：
  - 1) 计算高度图梯度（Sobel/Scharr），得到 `|∇H|` 与方向；
  - 2) 非极大值抑制，细化边缘；
  - 3) 双阈值滞后（high/low，自适应分位数），连通跟踪主边与从边；
  - 4) 沿“水平Forward”对连通边做法向扫描，构建切片（Bottom/Top）；
  - 5) 多尺度坡度度量与二分精化（~0.05m）；
  - 6) 平滑（Mean/Gaussian/Median）并回采高度；
  - 7) 过滤：`Height >= minFacadeHeightMeters` 与最小段长 `Lmin`。
- 输出：`List<CliffSliceV2>`；接口保持 `TraceVirtualFacade/TryDetectFacade` 不变，内部切换至V2。
- 并行：梯度与抑制 `IJobParallelFor`；连通跟踪分块合并；避免GC。

### 参数与配置
- `VegetationItem`：新增 `CoreRailWidth`（或从 `edgeReferenceWidthMeters` 映射）；`ValidateCore()` 钳制。
- `MrTerrainPainterConfig`：增加 `cannyHigh/Low`（可选自动估计）、`minSegmentLengthMeters`；保留 `minFacadeHeightMeters`。

### 笔刷形状库与预览
- 形状：`Strip/Ribbon/Arc/SplineStrip`；接口 `IShapeSampler{ Sample(u), Tangent(u) }`。
- 预览：
  - 双轨并行：`Bottom ± Normal*(RailWidth/2)` 多段线；
  - 间距刻度：沿法线短线段（长度=Rail*0.3）；HUD显示 `Size/Spacing/Rail`；
  - LOD：轨迹下采样、50–100ms防抖、缓存上次结果。
- UI：UITK增加形状选择与轨迹参数（轨距、曲率、段数），绑定 `BrushSettings.ChangedKey`。

### 生成融合
- 候选：形状轨迹按 `CoreSpacing` 采样；EdgeLine用切片底线或形状路径；
- 放置：`LookRotation(normal, up)`；偏移应用在 `right/Direction/(-Normal)`；
- 去重：局部项网格 + 全局网格（可选）；权重混合 Alias 保持。

## 文件改动
- `Editor/Services/FacadeDetectionService.cs`：新增V2实现，`TraceVirtualFacade/TryDetectFacade`接入；
- `Editor/Services/BrushPainter.cs`：
  - `BrushShape.Strip` 与预览绘制（双轨+刻度+LOD）；
  - 预览标签扩展；
- `Editor/Services/VegetationGenerator.cs`：形状采样器对接，使用 `CoreRailWidth/CoreSpacing/CoreOffset/CoreScale`；
- `Runtime/Profiles/VegetationItem.cs`：`CoreRailWidth` 与校验；
- `Editor/Views/BrushView.cs`：UITK形状与参数绑定。

## 测试
- EditMode：
  - 参数钳制与事件联动；
  - Canny连通与分段正确性（包含最小高度/长度过滤）；
  - 双轨平行度与刻度数据正确；
- PlayMode：
  - 复杂地形稳定识别；
  - 放置垂直度/并行度验证；
  - 权重分布统计（近似卡方）。

## 性能目标
- 检测一次（半径≤20m）：≤5ms（Burst/Jobs）；
- 预览绘制：≤1ms；
- 大场景实例化：对象池与网格去重维持帧率。

## 实施顺序
1) `FacadeDetectorV2` 核心实现与接入；
2) `Strip` 形状与预览渲染；
3) 生成融合与参数面板；
4) 测试与性能优化；
5) 文档与流程图输出。

确认后开始实施。
# 改造目标
- 让新画笔在一次笔触中“按 VegetationProfile 的条目权重与约束”混合布局，而不是逐条目独立刷点，从而更符合生态组合与比例。
- 保持条目级约束（最小间距、高度/坡度、缩放/旋转、法线对齐）与父节点映射有效。

## 设计要点
- 候选点一次性采样：笔触级使用选定分布（Poisson/Cluster/Jittered/Uniform）生成候选点列表，避免每条目独立采样造成分层与不均匀叠加。
- 条目加权选择：
  - 使用 `VegetationItem.weight` 构建加权表（现有 `BuildWeightedList` 可用），对每个候选点随机挑选“将要铺设的条目”。
  - 可选“按条目密度限制”与“完全权重混合”两种模式：
    - 模式A（默认）：每条目最多放置 `ceil(baseDensity * strength * densityScale * K)`，超过则回退到次高权重条目（防止某条目超量）。
    - 模式B：完全按权重，不做条目最大数限制（适合自由混合）。
- 空间约束复用：
  - 条目级 `minSpacing`：每条目维护 `Grid` 或 `SpatialHash`。
  - 可选“全局间距因子”：跨条目约束，避免不同物种相互挤压（使用统一 `SpatialHash` 乘以系数，如 0.5–1.0）。
- 条目级过滤与放置：
  - 过滤：高度/坡度（`MatchTerrain`），噪声阈值与概率（沿用 `VegetationGenerator` 的阈值逻辑）。
  - 放置：`SampleScale`/`SampleYRotation`、法线对齐、对象池复用，父节点解析 `ResolveTargetParent`。缺映射仅报一次错。
- 随机性：
  - 使用 `profile.randomSeed + strokeSeed` 构造 `System.Random`，保证笔触可复现。簇集分布再加条目索引扰动，保证跨条目多样性。
- 额外 Profile：
  - 提供“混合额外Profile”选项：候选点集合一次生成后，在加权列表中合并 `currentProfile + extraProfiles` 的条目进行统一选择；或保留现有“逐Profile循环叠加”模式作为备用。

## 改动范围
- `Editor/Services/BrushPainter.cs`
  - 新增：一次采样候选点 → 加权选条目 → 统一空间约束 → 条目过滤后放置。
  - 保留：`DrawPreview` 与现有参数；硬度曲线与分布仍生效。
  - 复用：`BuildWeightedList` 改为对当前可用条目（含 extraProfiles）构建一次性权重池。
- `Editor/MrTerrainPainterWindow.cs`
  - 在 `VegetationPainterOnTerrain` 增加开关以选择“混合额外Profile”。
- UI（可选增强）
  - Paint 页新增：
    - `混合条目(按权重)` 切换（默认开）
    - `全局间距因子`（0–1，默认0关闭）
    - `混合额外Profile` 切换（默认关）
    - `条目最大数限制` 切换（选择模式A/B）

## 验证
- 在包含 3–5 种条目（不同密度/间距/权重）的 Profile 上，比较“逐条目采样”与“权重混合”效果：
  - 最近邻分布更均匀、不同物种比例更符合设定权重。
- 噪声阈值：在复杂噪声设置下观察物种分布门限与概率叠加是否稳定。
- 性能：候选一次采样 + 统一网格复用，应比逐条目独立采样更少剔除与更平滑。

## 交付步骤
1. 实现权重混合管线（候选一次采样 + 加权选择 + 统一空间约束）。
2. 可选开关与参数注入到 BrushSettings 与 UI（最小化变更，默认兼容旧行为）。
3. 接入 `extraProfiles` 合并逻辑（保持开关，默认关闭）。
4. 回归测试：擦除、对象池、父节点映射与噪声阈值均正常。
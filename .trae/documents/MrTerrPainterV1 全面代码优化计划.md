## 目标与范围
根据您的新增要求，在保持提前返回、单一职责与性能优化的前提下，实施以下定向修复：
1) ExtraProfiles 空项清理与上下文 API 扩展；2) 稳定对象池回收键；3) 统一近地形搜索与选中地形列表；4) 距离计算统一使用平方比较；5) 预览完整性事件驱动缓存；6) 快捷键后的预览重绘。

## 具体改动
### 1. ExtraProfiles 空项清理（上下文 API）
- 文件：`Editor/Tools/MTPBrushContext.cs`
- 新增/扩展 API：
```csharp
public void PruneExtraNulls()
{
    for (int i = _extraProfiles.Count - 1; i >= 0; i--)
        if (_extraProfiles[i] == null) _extraProfiles.RemoveAt(i);
}

public bool RemoveExtra(VegetationProfile profile)
{
    if (profile == null)
    {
        PruneExtraNulls();
        return true;
    }
    return _extraProfiles.Remove(profile);
}

public void ClearExtras()
{
    _extraProfiles.Clear();
}
```
- 窗口层调用：`Editor/MrTerrainPainterWindow.cs` 在相关清理/切换逻辑处调用 `PruneExtraNulls()` 或 `ClearExtras()` 并重建有效条目。

### 2. 稳定对象池回收键（统一 BuildKey 来源）
- 文件：`Editor/Runtime/Core/VegetationInstance.cs`
- 增加字段：
```csharp
public class VegetationInstance : MonoBehaviour
{
    public string sourcePrefabName;
}
```
- 文件：`Editor/Services/VegetationPool.cs`
- 回收时优先取组件字段而非 `go.name`，并统一 `BuildKey` 入参来源：
```csharp
string nameForKey = inst != null && !string.IsNullOrEmpty(inst.sourcePrefabName) ? inst.sourcePrefabName : prefab.name;
var key = BuildKey(terrainId, itemIndex, nameForKey);
```
- 确保 `Get` 与 `Release` 使用一致的 `BuildKey` 规则，避免键不一致导致泄漏或误归还。

### 3. 统一近地形搜索与选中地形列表
- 文件：`Editor/Controllers/TerrainController.cs`
- 将“从上下文/Selection 拼装地形列表”和“最近地形查找”收敛到控制器：
```csharp
public IReadOnlyList<Terrain> GetSelectedTerrains()
{
    _cacheSelectedTerrains.Clear();
    // 从上下文与 Selection 组装
    // 去重与有效性过滤
    return _cacheSelectedTerrains;
}

public bool TryFindNearestTerrain(Vector3 worldPos, out Terrain nearest)
{
    var list = GetSelectedTerrains();
    if (list.Count == 0) list = Terrain.activeTerrains;
    // 遍历用平方距离比较
    nearest = null;
    float best = float.MaxValue;
    foreach (var t in list)
    {
        var p = t.transform.position;
        float d = (worldPos - p).sqrMagnitude;
        if (d < best) { best = d; nearest = t; }
    }
    return nearest != null;
}
```
- 调用方改造：`Editor/Tools/MTPBrushTool.cs`、`Editor/Services/SceneInteractionService.cs` 均通过控制器方法获取选中列表与最近地形，去除重复逻辑。

### 4. 距离计算统一使用平方比较
- 文件：`Editor/Services/SceneInteractionService.cs`
- 拖拽间距判定：
```csharp
float threshold = Mathf.Max(0.01f, spacing);
if ((hitPos - _lastPaintPos).sqrMagnitude >= threshold * threshold)
{
    VegetationPainterOnTerrain(terrain, hitPos);
    _lastPaintPos = hitPos;
}
```
- 文件：`Editor/Services/VegetationPool.cs`
- 半径查询：
```csharp
if ((new Vector3(p.x - center.x, 0f, p.z - center.z)).sqrMagnitude <= radius * radius)
    outList.Add(go);
```

### 5. 预览完整性检查事件驱动缓存
- 文件：`Editor/Config/MrTerrainPainterConfig.cs`
- 增加事件：
```csharp
public static event Action<bool> CompletenessChanged;
```
- 文件：`Editor/Services/BrushPainter.cs`
- 增加缓存并由事件驱动更新，绘制时直接读取缓存：
```csharp
private bool isConfigComplete;

private void OnEnable()
{
    MrTerrainPainterConfig.CompletenessChanged += OnConfigChanged;
}

private void OnDisable()
{
    MrTerrainPainterConfig.CompletenessChanged -= OnConfigChanged;
}

private void OnConfigChanged(bool complete)
{
    isConfigComplete = complete;
}

private void DrawPreview()
{
    if (!isConfigComplete) return;
    // 绘制逻辑
}
```

### 6. 快捷键后主动重绘
- 文件：`Editor/Tools/MTPBrushTool.cs`
- 在尺寸改变或快捷键调整完成后请求重绘：
```csharp
SceneView.RepaintAll();
```
- 如需避免频繁调用，可在内部节流或延迟调度。

## 验证与收益
- 验证：
  - 额外配置为空时的清理行为、窗口条目重建；
  - Pool 键一致性与 Recycle 正确归还；
  - 选中地形与最近地形的一致性；
  - 预览在配置变更后的即时响应与性能；
  - 快捷键尺寸调整后的预览刷新。
- 预期收益：GC 降低、热路径性能提升、键一致性消除误归还、地形列表与最近查找逻辑统一、预览响应更流畅。

请确认以上定向计划。确认后我将逐项实施并在每一步完成后进行验证与说明。
您好，我是资深Unity开发代码优化专家。接下来我会按照以下工作流程开展：

## 工作流程
1. 仔细审查代码，标注未遵循提前返回原则的片段，确定合理的提前返回条件。
2. 检查各模块是否职责单一，必要时拆分与重组函数职责。
3. 分析数据结构与算法，替换低效实现，选择合适集合类型。
4. 调整整体代码风格为现代化写法，规范命名与语法糖。
5. 审视并正确使用Unity API，用官方API替代自定义低效实现。

---

## 目标
- 打开主窗口前先“校验配置完整性”，不完整则直接弹出“MTP Settings”而不是主窗口。
- 实现“保存配置(SaveConfiguration)”按钮：持久化配置；成功后自动打开主窗口并关闭自身。
- 在设置窗口中增加并绑定更多配置项：UXML与USS引用等。

## 变更概览（遵循提前返回/单一职责）
- 在 `Editor/Config/MrTerrainPainterConfig.cs` 中：
  - 新增对 UI 资产的字段（UXML、USS）。
  - 增加静态工具方法：加载或创建配置资产、保存、校验完整性、确保文件夹存在。
- 在 `Editor/MrTerrainPainterSettingsWindow.cs` 中：
  - 绑定新增的 `ObjectField`（Uxmls & Usss）。
  - 实现 `SaveConfiguration` 按钮：校验→保存→打开主窗→关闭设置窗（提前返回处理失败场景）。
- 在 `Editor/MrTerrainPainterWindow.cs` 中：
  - 修改菜单入口 `Open()`：打开前校验配置；不完整则提前返回并打开设置窗口。
  - 在 `OnEnable()/CreateGUI()` 使用配置中引用的 UXML/USS，避免硬编码路径；若缺失则提前返回并提示。

## 关键实现（代码片段）

### 1) 配置资产与完整性校验
文件：`Assets/MrTerrPainterV1/Editor/Config/MrTerrainPainterConfig.cs`

```csharp
// 新增字段（UI 资产引用）
public UnityEngine.UIElements.VisualTreeAsset startUxml;
public UnityEngine.UIElements.VisualTreeAsset controlUxml;
public UnityEngine.UIElements.VisualTreeAsset paintUxml;
public UnityEngine.UIElements.VisualTreeAsset generateUxml;
public UnityEngine.UIElements.VisualTreeAsset vegetationProfileRowUxml;
public UnityEngine.UIElements.VisualTreeAsset prefabIconUxml;
public UnityEngine.UIElements.VisualTreeAsset draggableAreaUxml;
public UnityEngine.UIElements.StyleSheet stylesUss;

#if UNITY_EDITOR
using UnityEditor;
using System.Linq;

public static class ConfigTools
{
    private const string ConfigFolder = "Assets/MrTerrPainterV1/Editor/Config";
    private const string ConfigAssetPath = ConfigFolder + "/MrTerrainPainterConfig.asset";

    public static MrTerrainPainterConfig LoadOrCreateAsset()
    {
        var guid = AssetDatabase.FindAssets("t:MrTerrainPainter.Editor.Config.MrTerrainPainterConfig").FirstOrDefault();
        if (!string.IsNullOrEmpty(guid))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var loaded = AssetDatabase.LoadAssetAtPath<MrTerrainPainterConfig>(path);
            if (loaded != null) return loaded; // 提前返回：找到现有资产
        }
        EnsureFolder();
        var cfg = ScriptableObject.CreateInstance<MrTerrainPainterConfig>();
        AssetDatabase.CreateAsset(cfg, ConfigAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return cfg;
    }

    public static void Save(MrTerrainPainterConfig cfg)
    {
        if (cfg == null) return; // 提前返回
        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static bool IsComplete(MrTerrainPainterConfig cfg, out string reason)
    {
        reason = string.Empty;
        if (cfg == null) { reason = "配置对象为空"; return false; }
        // 必需：核心UI与样式
        if (cfg.startUxml == null) { reason = "StartUXML 未设置"; return false; }
        if (cfg.controlUxml == null) { reason = "ControlUXML 未设置"; return false; }
        if (cfg.paintUxml == null) { reason = "PaintUXML 未设置"; return false; }
        if (cfg.generateUxml == null) { reason = "GenerateUXML 未设置"; return false; }
        if (cfg.stylesUss == null) { reason = "StylesUSS 未设置"; return false; }
        // 可选但推荐：列表行、图标、拖拽区
        if (cfg.vegetationProfileRowUxml == null) { reason = "VegetationProfileUXML 未设置"; return false; }
        if (cfg.prefabIconUxml == null) { reason = "PrefabIconUXML 未设置"; return false; }
        if (cfg.draggableAreaUxml == null) { reason = "DraggableAreaUXML 未设置"; return false; }
        // 路径合法性
        if (string.IsNullOrEmpty(cfg.recipeGenerationPath)) { reason = "RecipeGenerationPath 为空"; return false; }
        if (!AssetDatabase.IsValidFolder(cfg.recipeGenerationPath)) { reason = "RecipeGenerationPath 不是有效的项目文件夹"; return false; }
        // 数组长度对齐
        if (cfg.objectList == null || cfg.objectTypeList == null) { reason = "生成映射为空"; return false; }
        if (cfg.objectList.Length != cfg.objectTypeList.Length) { reason = "生成映射长度不一致"; return false; }
        return true;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/MrTerrPainterV1"))
            AssetDatabase.CreateFolder("Assets", "MrTerrPainterV1");
        if (!AssetDatabase.IsValidFolder("Assets/MrTerrPainterV1/Editor"))
            AssetDatabase.CreateFolder("Assets/MrTerrPainterV1", "Editor");
        if (!AssetDatabase.IsValidFolder("Assets/MrTerrPainterV1/Editor/Config"))
            AssetDatabase.CreateFolder("Assets/MrTerrPainterV1/Editor", "Config");
    }
}
#endif
```

说明：
- 单一职责：`ConfigTools` 专注“查找/创建/保存/校验”，主窗与设置窗只调用它。
- 提前返回：校验与存储过程中均以最短路径返回，避免嵌套。

### 2) 修改主窗口打开逻辑（先配后开）
文件：`Assets/MrTerrPainterV1/Editor/MrTerrainPainterWindow.cs:97`

```csharp
[MenuItem("Tools/Mr Terrain Painter Main")]
private static void Open()
{
    var cfg = MrTerrainPainter.Editor.Config.MrTerrainPainterConfig.ConfigTools.LoadOrCreateAsset();
    if (!MrTerrainPainter.Editor.Config.MrTerrainPainterConfig.ConfigTools.IsComplete(cfg, out var reason))
    {
        // 配置不完整：转到设置窗口
        MrTerrainPainterSettingsWindow.Open();
        EditorUtility.DisplayDialog("配置不完整", reason + "\n请在设置窗口补齐配置。", "确定");
        return; // 提前返回：不打开主窗
    }
    var win = GetWindow<MrTerrainPainterWindow>(false, "Mr Terrain Painter");
    win.Show();
}
```

说明：
- 完全遵循“打开前校验”的流程；失败即提前返回并引导到设置窗口。

### 3) 主窗口使用配置引用的 UXML/USS
文件：`Assets/MrTerrPainterV1/Editor/MrTerrainPainterWindow.cs:147` 与 `CreateGUI():220`

```csharp
// OnEnable 中替换硬编码加载：
if (config == null)
{
    config = MrTerrainPainter.Editor.Config.MrTerrainPainterConfig.ConfigTools.LoadOrCreateAsset();
}

uxmlStart = config.startUxml;
uxmlContral = config.controlUxml;
uxmlGenerate = config.generateUxml;
uxmlPaint = config.paintUxml;
uxmlVegetationProfileRow = config.vegetationProfileRowUxml;
uxmlVegetationProfilePrefabIcon = config.prefabIconUxml;
uxmlVegetationProfileDraggableArea = config.draggableAreaUxml;

// CreateGUI 中样式应用：
var styleSheet = config.stylesUss;
if (styleSheet == null)
{
    // 提前返回：缺少样式时给出可见提示并不继续构建UI
    root.Add(new Label("样式未配置：请在 Settings 中设置 StylesUSS"));
    return;
}
root.styleSheets.Add(styleSheet);
```

说明：
- 移除路径硬编码，统一用配置对象。
- UI 构建严格按提前返回原则处理缺失资源。

### 4) 设置窗口：绑定 UXML/USS，与“保存配置”
文件：`Assets/MrTerrPainterV1/Editor/MrTerrainPainterSettingsWindow.cs:40`

```csharp
public void CreateGUI()
{
    rootVisualElement.Clear();
    VisualElement root = settingsUxml != null ? settingsUxml.Instantiate() : new VisualElement();
    rootVisualElement.Add(root);

    // —— 绑定基础字段（保持原有逻辑） ——
    var tfRecipePath = root.Q<TextField>("RecipeGenerationPath");
    if (tfRecipePath != null)
    {
        tfRecipePath.SetValueWithoutNotify(config.recipeGenerationPath);
        tfRecipePath.RegisterValueChangedCallback(e => { config.recipeGenerationPath = e.newValue; EditorUtility.SetDirty(config); });
    }
    var toggleShowPool = root.Q<Toggle>("ShowPool");
    if (toggleShowPool != null)
    {
        toggleShowPool.SetValueWithoutNotify(VegetationPool.ShowInHierarchy);
        toggleShowPool.RegisterValueChangedCallback(e =>
        {
            VegetationPool.ShowInHierarchy = e.newValue;
            config.showPoolInHierarchy = e.newValue;
            EditorUtility.SetDirty(config);
        });
    }

    // —— 绑定 UXML 引用 ——
    BindUxml(root, "StartUXML", v => config.startUxml = v);
    BindUxml(root, "ControlUXML", v => config.controlUxml = v);
    BindUxml(root, "PaintUXML", v => config.paintUxml = v);
    BindUxml(root, "GenerateUXML", v => config.generateUxml = v);
    BindUxml(root, "VegetationProfileUXML", v => config.vegetationProfileRowUxml = v);
    BindUxml(root, "PrefabIconUXML", v => config.prefabIconUxml = v);
    BindUxml(root, "DraggableAreaUXML", v => config.draggableAreaUxml = v);

    // —— 绑定 USS 引用 ——
    var ss = root.Q<ObjectField>("StylesUSS");
    if (ss != null)
    {
        ss.objectType = typeof(UnityEngine.UIElements.StyleSheet);
        ss.allowSceneObjects = false;
        ss.SetValueWithoutNotify(config.stylesUss);
        ss.RegisterValueChangedCallback(e => { config.stylesUss = e.newValue as UnityEngine.UIElements.StyleSheet; EditorUtility.SetDirty(config); });
    }

    // —— 保存配置按钮 ——
    var btnSave = root.Q<Button>("SaveConfiguration");
    if (btnSave != null)
    {
        btnSave.clicked += () =>
        {
            var cfg = MrTerrainPainter.Editor.Config.MrTerrainPainterConfig.ConfigTools.LoadOrCreateAsset();
            // 将当前内存值写入资产（同一实例时只需保存；不同实例可用 EditorUtility.CopySerialized）
            EditorUtility.CopySerialized(config, cfg);

            if (!MrTerrainPainter.Editor.Config.MrTerrainPainterConfig.ConfigTools.IsComplete(cfg, out var reason))
            {
                EditorUtility.DisplayDialog("配置不完整", reason, "确定");
                return; // 提前返回：不打开主窗
            }
            MrTerrainPainter.Editor.Config.MrTerrainPainterConfig.ConfigTools.Save(cfg);
            MrTerrainPainterWindow.Open();
            Close();
        };
    }
}

private void BindUxml(VisualElement root, string name, System.Action<UnityEngine.UIElements.VisualTreeAsset> apply)
{
    var of = root.Q<ObjectField>(name);
    if (of == null) return; // 提前返回
    of.objectType = typeof(UnityEngine.UIElements.VisualTreeAsset);
    of.allowSceneObjects = false;
    of.SetValueWithoutNotify(apply == null ? null : null);
    of.RegisterValueChangedCallback(e =>
    {
        apply?.Invoke(e.newValue as UnityEngine.UIElements.VisualTreeAsset);
        EditorUtility.SetDirty(config);
    });
}
```

说明：
- `BindUxml` 复用：保证单一职责，所有 UXML 字段同构绑定逻辑避免重复。
- 保存流程采用“提前返回”：不完整立即提示、阻止主窗打开。
- 使用 `EditorUtility.CopySerialized` 将当前设置窗口中的临时 `config` 值复制到持久化资产，避免实例差异导致值丢失。

## 验证方案
- 初次点击菜单 `Tools/Mr Terrain Painter Main`：若未配或缺少 UXML/USS，直接弹出 Settings 并提示原因。
- 在 Settings 中设置所有必填项与有效的 `RecipeGenerationPath`，点击“保存配置”：
  - 创建/更新 `MrTerrainPainterConfig.asset`。
  - 自动打开主窗口；Settings 自动关闭。
- 主窗口 UI 生效：`CreateGUI()` 成功应用 `stylesUss`，页面实例化来自配置的 UXML 引用。

## 注意事项
- 严格提前返回：所有校验/空对象/路径异常立即返回，避免嵌套与后续错误。
- 单一职责：配置的“加载/保存/校验”集中在 `ConfigTools`，主窗/设置窗仅调用。
- 正确使用Unity API：`AssetDatabase`/`EditorUtility`/`ObjectField`/`StyleSheet` 等全用官方API。
- 版本兼容：适配 Unity 2021.3.18f1 的 UI Toolkit 与 Editor API。

如确认以上方案，我将按上述片段分别在对应文件中完成代码修改与验证。
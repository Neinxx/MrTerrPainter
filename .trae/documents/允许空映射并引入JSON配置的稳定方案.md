# 目标
- 允许 `config.objectList/objectTypeList` 为 null 或空，不阻碍后续配置与使用。
- 保持设置面板与主工具在数组为空时正常工作（不抛异常、不强制填写）。
- 引入“JSON导入/导出”作为稳定的配置备份与迁移方式（SO ↔ JSON），在编辑器下可一键保存与恢复。

## 原因与取舍
- ScriptableObject 优点：序列化Unity对象引用（`VisualTreeAsset/StyleSheet/GameObject`）、域重载安全、与编辑器API深度集成。
- JSON 优点：文本可版本化、跨项目迁移便捷、无需依赖资产数据库；缺点：不能直接存储Unity对象引用（需要转换为 GUID/路径/名称）。
- 结论：保留SO作为运行时与UI绑定的主配置，新增JSON导入/导出作为稳定备份与迁移方式；对于 `GameObject` 引用，JSON使用“层级路径/名称标识”描述，导入时再尝试解析到场景对象。

## 改动范围
1. 允许空数组与宽松校验
- `ConfigTools.IsComplete(...)`：移除对 `objectList/objectTypeList` 非空与长度一致的强制检查，仅提醒非必填。
- `MrTerrainPainterSettingsWindow.CreateGUI()`：
  - `BindRow` 与初始化部分，对 `config.objectList/objectTypeList` 均采用空安全判断；若为空则从0开始，允许用户新增行。
  - 所有 `.ToList()` 或索引访问前做 null 检查与空数组创建。

2. 设置面板按钮与JSON
- 在 `MTP Settings` 面板新增按钮：
  - `导出JSON`：将当前 SO 配置序列化为DTO（仅包含可JSON化字段：数值、字符串、UXML/USS路径、枚举；`GameObject` 映射转为“场景路径/名称 + PrefabType”）。
  - `导入JSON`：从 JSON 反序列化DTO回SO；对于 `GameObject` 映射，按“场景路径/名称”尝试查找并填充（找不到则留空以便后续人工指定）。
- JSON位置建议：`Assets/MrTerrPainterV1/Editor/Config/mrterr_config.json`，支持选择其他路径。
- 使用 `JsonUtility` 实现，DTO中避免 UnityEngine.Object 引用；UXML/USS通过资产路径或GUID存储并恢复。

3. 依赖注入与默认回退
- Overlay与主窗口的UXML/USS加载逻辑：优先使用 SO 注入；缺失时自动回退默认路径（已完成）。
- AutoDiscover按钮保留并与 SO 字段同步；导出JSON时也带出路径，导入时按路径恢复。

4. 验证与回退
- 在空数组状态下打开 Settings 与主窗口不再抛异常；用户可以自由新增/删除映射行。
- 导入/导出验证：导出后删除SO资产或清空字段，再导入JSON能正确恢复主要参数与资源路径；场景对象引用按名称/路径尽可能恢复，不存在时保留为空。

## 实施步骤
1. 宽松校验：更新 `ConfigTools.IsComplete` 移除数组强校验，保留必填项为UXML/USS与路径合法性。
2. 空安全绑定：更新 `MrTerrainPainterSettingsWindow.CreateGUI` 的所有数组访问与绑定逻辑。
3. JSON支持：
- 新增 `MrTerrainPainterConfigDto`（纯数据结构）与 `ConfigJson` 工具类：`ExportToJson(cfg, path)`、`ImportFromJson(cfg, path)`。
- 设置面板新增两个按钮并绑定到上述方法，完成序列化/反序列化与字段赋值。
4. 验证与提示：导入后弹窗提示恢复结果（成功/失败项数），并引导“保存配置”。

确认后我将按上述步骤落地更改，并提供导入/导出功能与空安全的设置面板行为。
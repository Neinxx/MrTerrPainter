## 目标
- 当条目为 Landscape（封边石）时，实例的本地 +Z（forward）指向地形法线外向，使“Z轴外向吸附到悬崖面”。
- 保留插入深度与法线对齐，但旋转框架从“水平切线”改为“forward=法线”。

## 技术调整
- 旋转框架：
  - forward = 法线 `n`（本地 +Z 指向外）
  - up = 将世界 `Vector3.up` 投影到法线的垂直平面：`upOnPlane = ProjectOnPlane(Vector3.up, n)`；如退化（近零），fallback 到 `up = Vector3.Cross(n, Vector3.right).normalized`
  - 基础旋转：`Quaternion.LookRotation(forward, up)`
  - Landscape 的二次旋转改为绕 forward 轴的旋转：`Quaternion.AngleAxis(yRot, forward)`，而非全局 Y 轴

- 变更位置：
  - Editor/Services/BrushPainter.cs → `CreateInstance`：为 Landscape 使用上述旋转框架；保持其他类型原逻辑
  - Editor/Services/VegetationGenerator.cs → `CreateInstance`：同样在 Landscape 分支应用上述旋转框架（现已强制法线对齐，需改 forward=法线与up投影，yaw绕forward）

- 插入深度：保持 `pos -= n.normalized * embedDepth`（已实现）；在旋转前完成位置插入

## 验证
- 在近垂直面刷涂 Landscape：观察本地 +Z 指向外（可在资产上添加调试箭头或使用延迟预览核验）
- 调整 `edgeSlopeThreshold` 与 `embedDepthRange` 不影响朝向逻辑；`yRot` 绕 forward 轴旋转侧向朝向
- 批量生成路径一致；撤销与对象池正常

## 交付
- 小范围代码修改，不新增文件；遵循现有 API 与提前返回原则
- 完成后进行交互验证并回报结果

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor
{
    // Start 页相关逻辑（窗口只做装配与绑定）
    public partial class MrTerrainPainterWindow
    {
        // 使用 const 或 readonly，如果值在初始化后不会改变
        private const string HelloThereMessage = "Good Luck!";
        private const string LogoElementName = "LOGO";
        private const float DoubleClickInterval = 0.6f; // 双击间隔时间（秒）

        private VisualElement _logoElement; // 缓存 logo 元素，方便在其他方法中使用

        /// <summary>
        /// 注册 Start 页面的 UI 事件
        /// </summary>
        private void SetupStartPageEvents()
        {
            // 早期退出检查
            if (startRoot == null) return;

            // 1. 初始化空列表的 UI 容器与绑定，确保首次显示正确
            // 假设 Refresh(null) 也能安全处理
            startTerrainListView?.Refresh(terrainListUIData);

            // 2. 获取并缓存 LOGO 元素
            _logoElement = startRoot.Q<Label>(LogoElementName);

            // 3. 注册 LOGO 元素的双击和动画效果
            if (_logoElement != null)
            {
                // 初始化双击计数器
                int clickCount = 0;
                double lastClickTime = 0;

                _logoElement.RegisterCallback<PointerDownEvent>(evt =>
                {
                    // 使用更清晰的方法处理双击逻辑
                    if (IsDoubleClick(ref lastClickTime, ref clickCount))
                    {
                        // 触发双击逻辑
                        HandleLogoDoubleClick(_logoElement as Label);

                        // 阻止事件传播，避免双击影响其他 UI
                        evt.StopPropagation();
                    }

                    // 无论是否双击，都播放点击动画
                    PlayClickAnimation(_logoElement);
                });
            }
        }

        /// <summary>
        /// 检查是否为双击
        /// </summary>
        /// <param name="lastTime">上次点击的时间</param>
        /// <param name="count">点击计数</param>
        /// <returns>如果是双击则返回 true</returns>
        private bool IsDoubleClick(ref double lastTime, ref int count)
        {
            var now = EditorApplication.timeSinceStartup;

            // 如果两次点击间隔超过设定值，则重置计数器
            if (now - lastTime > DoubleClickInterval)
            {
                count = 0;
            }

            lastTime = now;
            count++;

            return count >= 2;
        }

        /// <summary>
        /// 处理 LOGO 元素双击事件的逻辑
        /// </summary>
        /// <param name="logoLabel">LOGO 标签</param>
        private void HandleLogoDoubleClick(Label logoLabel)
        {
            if (logoLabel == null) return;
            OpenSettingsTab();
        }

        /// <summary>
        /// 为元素播放一个简单的点击动画 (例如：变色/缩小后恢复)
        /// </summary>
        /// <param name="element">要播放动画的元素</param>
        private void PlayClickAnimation(VisualElement element)
        {
            if (element == null) return;

            // 1. 定义动画参数
            var originalColor = element.style.color.value;
            var pressedColor = new StyleColor(new Color(0.8f, 0.6f, 0.1f)); // 偏黄的按压色
            var animationDuration = 100; // 动画时长 (毫秒)

            // 2. 播放动画：按下效果
            element.style.color = pressedColor; // 改变颜色
            element.style.scale = new StyleScale(new Scale(new Vector3(0.95f, 0.95f, 0.95f))); // 缩小一点

            // 3. 使用 UIElements 的 `schedule.Execute` 延迟恢复
            element.schedule.Execute(() =>
            {
                // 恢复到原始状态
                element.style.color = originalColor;
                element.style.scale = new StyleScale(new Scale(new Vector3(1f, 1f, 1f)));
            }).StartingIn(animationDuration); // 在指定毫秒后执行
        }
    }
}

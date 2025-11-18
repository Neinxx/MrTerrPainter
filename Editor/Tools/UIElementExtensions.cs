using System;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Tools
{
    public static class UIElementExtensions
    {
        // 为按钮设置点击处理器（带防重复绑定）
        public static void SetClickHandler(this Button btn, Action handler)
        {
            if (btn == null) return; // 提前返回
            var old = btn.userData as Action;
            if (old != null) btn.clicked -= old; // 移除旧回调
            btn.userData = handler;
            if (handler != null) btn.clicked += handler;
        }

        // 在指定根下按文本查找按钮（返回首个匹配）
        public static Button FindButtonByText(this VisualElement root, string text)
        {
            if (root == null || string.IsNullOrEmpty(text)) return null; // 提前返回
            Button found = null;
            root.Query<Button>().ForEach(btn =>
            {
                if (found != null) return; // 已找到
                if (btn != null && btn.text == text) found = btn;
            });
            return found;
        }
        public static T FindByNameOrText<T>(this VisualElement root, string nameOrText) where T : VisualElement
        {
            if (root == null || string.IsNullOrEmpty(nameOrText)) return null;
            var byName = root.Q<T>(nameOrText);
            if (byName != null) return byName;
            T found = null;
            root.Query<T>().ForEach(el =>
            {
                if (found != null) return;
                if (el is Button b && b.text == nameOrText) found = (T)(VisualElement)b;
            });
            return found;
        }
        public static void SetDisplay(this VisualElement element, bool show)
        {
            if (element != null) element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

}

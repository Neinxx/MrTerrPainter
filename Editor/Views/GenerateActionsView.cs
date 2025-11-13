using System;
using UnityEngine.UIElements;
using MrTerrainPainter.Editor.Tools;

namespace MrTerrainPainter.Editor.Views
{
    // 生成动作视图：封装 Generate 页的“生成/清除”按钮绑定
    public class GenerateActionsView
    {
        private readonly VisualElement root;

        public GenerateActionsView(VisualElement root)
        {
            this.root = root;
        }

        public void Bind(Action onGenerate, Action onClear)
        {
            if (root == null) return; // 提前返回

            // 仅对明确命名的两个按钮进行绑定，防止 Query 全量导致误绑定
            var btnGenerate = root.FindButtonByText("生成");
            var btnClear = root.FindButtonByText("清除");

            // 生成按钮：防重复绑定，使用 userData 存储处理器
            if (btnGenerate != null)
            {
                Action genHandler = () => { onGenerate?.Invoke(); };
                btnGenerate.SetClickHandler(genHandler);
            }

            // 清除按钮：同样防重复绑定
            if (btnClear != null)
            {
                Action clearHandler = () => { onClear?.Invoke(); };
                btnClear.SetClickHandler(clearHandler);
            }
        }
    }
}
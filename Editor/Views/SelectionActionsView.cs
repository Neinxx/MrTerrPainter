using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MrTerrainPainter.Editor.Tools;

namespace MrTerrainPainter.Editor.Views
{
    // 选择动作视图：封装“添加选中地形”按钮绑定（Start/Contral 页通用）
    public class SelectionActionsView
    {
        private readonly VisualElement root;

        public SelectionActionsView(VisualElement root)
        {
            this.root = root;
        }

        public struct SelectionActionsCallbacks
        {
            public Func<IEnumerable<GameObject>> GetSelectionObjects;
            public Action ClearTerrainLists; // 清空 selectedTerrains / terrainListUIData / scannedTerrainNames
            public Action<Terrain> AddTerrainToLists; // 添加到 selectedTerrains / terrainListUIData / scannedTerrainNames
            public Action RefreshStartListUI;
            public Action RefreshControlListUI;
            public Action BuildControlSection;
        }

        public void Bind(SelectionActionsCallbacks cb)
        {
            if (root == null) return; // 提前返回
            var btn = root.Q<Button>("AddSelectedTerrain");
            if (btn == null) return; // 提前返回
            btn.AddToClassList("mt-button");

            // 防重复：移除旧回调（使用 userData 记录）
            Action handler = () =>
            {
                // 保护性检查
                if (cb.ClearTerrainLists == null || cb.AddTerrainToLists == null || cb.GetSelectionObjects == null)
                    return; // 提前返回

                cb.ClearTerrainLists();

                var selection = cb.GetSelectionObjects?.Invoke();
                if (selection != null)
                {
                    foreach (var obj in selection)
                    {
                        var t = obj?.GetComponent<Terrain>();
                        if (t == null) continue;
                        cb.AddTerrainToLists(t);
                    }
                }

                // 刷新两个页面的列表（若存在）
                cb.RefreshStartListUI?.Invoke();
                cb.RefreshControlListUI?.Invoke();

                // 构建控制区（Start 页行为保持一致）
                cb.BuildControlSection?.Invoke();
            };

            btn.SetClickHandler(handler);
        }


    }
}
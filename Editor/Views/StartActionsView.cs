using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MrTerrainPainter.Editor.Tools;

namespace MrTerrainPainter.Editor.Views
{
    // 起始页动作视图：统一绑定“扫描/添加选中地形/清空列表”按钮
    // 关键优化：
    // - 提前返回：所有绑定均做空引用保护，避免 NRE。
    // - 单一职责：Start 页仅负责 Scan/Add/Clear 的绑定；Add 委托给 SelectionActionsView，避免重复实现。
    // - 防重复绑定：统一使用 UIElementExtensions.SetClickHandler。
    public class StartActionsView
    {
        private readonly VisualElement root;

        public StartActionsView(VisualElement root)
        {
            this.root = root;
        }

        public struct StartActionsCallbacks
        {
            // Scan：扫描场景地形，填充 UI 数据
            public Action ScanSceneTerrains;
            // Clear：仅清空 UI 地形列表（不动已选集合）
            public Action ClearTerrainUIList;

            // Add：添加选中对象中的 Terrain
            public Func<IEnumerable<GameObject>> GetSelectionObjects;
            public Action ClearTerrainLists;
            public Action<Terrain> AddTerrainToLists;

            // 刷新与构建
            public Action RefreshStartListUI;
            public Action RefreshContralListUI;
            public Action BuildContralSection;
        }

        // 统一绑定三个按钮
        public void BindAll(StartActionsCallbacks cb)
        {
            if (root == null) return; // 提前返回
            BindScan(cb);
            BindClear(cb);
            BindAddSelected(cb);
        }

        // 绑定扫描按钮（Start 页）
        public void BindScan(StartActionsCallbacks cb)
        {
            if (root == null) return; // 提前返回
            var btnScan = root.Q<Button>("ScanTerrains");
            if (btnScan == null) return; // 提前返回

            Action handler = () =>
            {
                if (cb.ScanSceneTerrains == null) return; // 提前返回
                cb.ScanSceneTerrains();
                cb.RefreshStartListUI?.Invoke();
                cb.RefreshContralListUI?.Invoke();
                cb.BuildContralSection?.Invoke();
            };
            btnScan.AddToClassList("mt-button");
            btnScan.SetClickHandler(handler);
        }

        // 绑定清空按钮（Start 页）
        public void BindClear(StartActionsCallbacks cb)
        {
            if (root == null) return; // 提前返回
            var btnClear = root.Q<Button>("ClearTerrainList");
            if (btnClear == null) return; // 提前返回

            Action handler = () =>
            {
                // 按需清空所有地形相关数据与 UI 列表
                if (cb.ClearTerrainLists != null)
                {
                    cb.ClearTerrainLists();
                }
                else
                {
                    cb.ClearTerrainUIList?.Invoke();
                }
                cb.RefreshStartListUI?.Invoke();
                cb.RefreshContralListUI?.Invoke();
            };
            btnClear.SetClickHandler(handler);
        }

        // 绑定添加按钮（Start 页）
        public void BindAddSelected(StartActionsCallbacks cb)
        {
            if (root == null) return; // 提前返回
            // 复用 SelectionActionsView 的“添加选中地形”绑定，保持单一职责
            var selectionView = new SelectionActionsView(root);
            selectionView.Bind(new SelectionActionsView.SelectionActionsCallbacks
            {
                GetSelectionObjects = cb.GetSelectionObjects,
                ClearTerrainLists = cb.ClearTerrainLists,
                AddTerrainToLists = cb.AddTerrainToLists,
                RefreshStartListUI = cb.RefreshStartListUI,
                RefreshContralListUI = cb.RefreshContralListUI,
                BuildContralSection = cb.BuildContralSection
            });
        }
    }
}

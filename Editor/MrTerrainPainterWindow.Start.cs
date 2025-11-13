using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MrTerrainPainter.Editor.Views;
using MrTerrainPainter.Editor.Controllers;

namespace MrTerrainPainter.Editor
{
    // Start 页相关逻辑（窗口只做装配与绑定）
    public partial class MrTerrainPainterWindow
    {
        private void SetupStartPageEvents()
        {
            if (startRoot == null) return;
            // 初始化 Start 页地形列表视图
            startTerrainListView ??= new TerrainListView(startRoot);
            // 初始化空列表的 UI 容器与绑定，避免首次不显示
            startTerrainListView.Refresh(terrainListUIData);

            // 三击 LOGO 打开设置页（独立窗口）
            var logo = startRoot.Q<Label>("LOGO");
            if (logo != null)
            {
                double last = 0;
                int count = 0;
                logo.RegisterCallback<PointerDownEvent>(evt =>
                {
                    var now = EditorApplication.timeSinceStartup;
                    if (now - last > 0.6) { count = 0; }
                    last = now;
                    count++;
                    if (count >= 3)
                    {
                        MrTerrainPainterSettingsWindow.Open();
                        count = 0;
                        evt.StopPropagation();
                    }
                });
            }

            // Start 页按钮统一绑定：Scan / Add / Clear（保持单一职责）
            var startActions = new StartActionsView(startRoot);
            startActions.BindAll(new StartActionsView.StartActionsCallbacks
            {
                // Scan：扫描场景地形
                ScanSceneTerrains = () => terrainController.ScanSceneTerrains(terrainListUIData, scannedTerrainNames),
                // Clear：仅清空 UI 列表
                ClearTerrainUIList = () => terrainController.ClearTerrainUIList(terrainListUIData),
                // Add：添加选中对象中的 Terrain
                GetSelectionObjects = () => Selection.gameObjects,
                ClearTerrainLists = () => terrainController.ClearTerrainLists(selectedTerrains, terrainListUIData, scannedTerrainNames),
                AddTerrainToLists = t => terrainController.AddTerrainToLists(t, selectedTerrains, terrainListUIData, scannedTerrainNames),
                // 刷新与构建
                RefreshStartListUI = () => { if (startTerrainListView != null) startTerrainListView.Refresh(terrainListUIData); },
                RefreshContralListUI = () =>
                {
                    if (contralRoot != null)
                    {
                        if (contralTerrainListView != null) contralTerrainListView.Refresh(terrainListUIData);
                        else { contralTerrainListView = new TerrainListView(contralRoot); contralTerrainListView.Refresh(terrainListUIData); }
                        // 根据当前地形列表数量切换控制页可见性
                        ToggleContralPageVisibility();
                    }
                },
                BuildContralSection = () => BuildContralSection()
            });
        }
    }
}
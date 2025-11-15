using UnityEngine.UIElements;
using UnityEditor;

namespace MrTerrainPainter.Editor.Views.Tabs
{
    public class GenerateTabView
    {
        private readonly MrTerrainPainterWindow window;
        private readonly VisualElement genRoot;

        public GenerateTabView(MrTerrainPainterWindow window, VisualElement genRoot)
        {
            this.genRoot = genRoot;
            this.window = window;
        }

        public void Setup()
        {
            var genParam = genRoot;
            window.BindGenerateFilterControls(genParam);
            // 绑定生成/清除按钮（直接绑定，移除 GenerateActionsView 依赖）
            var btnGenerate = genParam.Q<Button>("GenerateTerrainObject");
            var btnClear = genParam.Q<Button>("ClearTerrainObject");
            if (btnGenerate != null)
            {
                btnGenerate.clicked += () => window.HandleGenerateAction();
            }
            if (btnClear != null)
            {
                btnClear.clicked += () => window.HandleClearAction();
            }
            // 地形列表最多显示10项，其余滚动
            window.PopulateTerrianListUI(genParam);
            var startActions = new StartActionsView(genParam);
            startActions.BindAll(new StartActionsView.StartActionsCallbacks
            {
                ScanSceneTerrains = () => window.terrainController.ScanSceneTerrains(window.terrainListUIData, window.scannedTerrainNames),
                ClearTerrainUIList = () => window.terrainController.ClearTerrainUIList(window.terrainListUIData),
                GetSelectionObjects = () => Selection.gameObjects,
                ClearTerrainLists = () => window.terrainController.ClearTerrainLists(window.selectedTerrains, window.terrainListUIData, window.scannedTerrainNames),
                AddTerrainToLists = t => window.terrainController.AddTerrainToLists(t, window.selectedTerrains, window.terrainListUIData, window.scannedTerrainNames),
                RefreshStartListUI = () => { },
                RefreshControlListUI = () =>
                {
                    window.PopulateTerrianListUI(genParam);
                    window.UpdateGenerateActionsVisibility(genParam);
                },
                BuildControlSection = null
            });
            window.UpdateGenerateActionsVisibility(genParam);
        }
    }
}

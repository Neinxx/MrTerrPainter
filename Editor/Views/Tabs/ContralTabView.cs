using UnityEngine.UIElements;
using UnityEditor;

namespace MrTerrainPainter.Editor.Views.Tabs
{
    public class ContralTabView
    {
        private readonly MrTerrainPainterWindow window;
        private readonly VisualElement root;

        public ContralTabView(MrTerrainPainterWindow window, VisualElement contralRoot)
        {
            this.window = window;
            this.root = contralRoot;
        }

        public void SetupTabEvents()
        {
            if (root == null) return;
            var btnPainting = root.Q<Button>("Painting");
            var btnGenerate = root.Q<Button>("Generate");
            var btnSettings = root.Q<Button>("Settings");
            if (btnPainting != null)
            {
                btnPainting.clicked += () =>
                {
                    window.SetTabActive(btnPainting, btnGenerate);
                    btnSettings?.RemoveFromClassList("mt-tabbutton--active");
                    window.LoadPaintingTab();
                };
            }
            if (btnGenerate != null)
            {
                btnGenerate.clicked += () =>
                {
                    window.SetTabActive(btnGenerate, btnPainting);
                    btnSettings?.RemoveFromClassList("mt-tabbutton--active");
                    window.LoadGenerateTab();
                };
            }
            if (btnSettings != null)
            {
                btnSettings.clicked += () =>
                {
                    btnSettings.AddToClassList("mt-tabbutton--active");
                    btnPainting?.RemoveFromClassList("mt-tabbutton--active");
                    btnGenerate?.RemoveFromClassList("mt-tabbutton--active");
                    window.LoadSettingsTab();
                };
            }

            var btnCreate = root.Q<Button>("CreateNewVegetation");
            if (btnCreate != null)
            {
                btnCreate.clicked += () =>
                {
                    window.CreateNewVegetationAndRefresh();
                };
            }

            var selectionActionsContral = new SelectionActionsView(root);
            selectionActionsContral.Bind(new SelectionActionsView.SelectionActionsCallbacks
            {
                GetSelectionObjects = () => Selection.gameObjects,
                ClearTerrainLists = () => window.terrainController.ClearTerrainLists(window.selectedTerrains, window.terrainListUIData, window.scannedTerrainNames),
                AddTerrainToLists = t => window.terrainController.AddTerrainToLists(t, window.selectedTerrains, window.terrainListUIData, window.scannedTerrainNames),
                RefreshStartListUI = () => { },
                RefreshContralListUI = () => { window.PopulateTerrianListUI(root); },
                BuildContralSection = null
            });
        }

        public void SetupNamedControls()
        {
            if (root == null) return;
            var prefabRange = root.Q<VisualElement>("PrefabRange");
            var queryRoot = prefabRange ?? root;
            var preview = root.Q<VisualElement>("PreviewPrefabList");
            window.SetPreviewListContainer(preview);
            window.SetupVegetationProfileListPublic(queryRoot);
            window.BindPropertyPanelViewFromRoot(queryRoot);
            var pv = new MrTerrainPainter.Editor.Views.PreviewListView(preview,
                gos => window.AddPrefabsToCurrentProfile(gos),
                () => window.GetCurrentProfile() != null);
            window.RefreshPreviewListUIPublic();
        }
    }
}

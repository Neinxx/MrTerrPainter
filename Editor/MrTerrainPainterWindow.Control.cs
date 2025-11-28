using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Editor.Config;
using MrTerrainPainter.Editor.Utils;
using MrTerrainPainter.Editor.Views;
using MrTerrainPainter.Editor.Views.Tabs;
using MrTerrainPainter.Editor.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace MrTerrainPainter.Editor
{
    public partial class MrTerrainPainterWindow
    {
        // UI 引用缓存
        private VisualElement controlRoot;
        private Button btnTabPainting;
        private Button btnTabGenerate;
        private Button btnTabSettings;

        private PropertyPanelView propertyPanelView;

        private ListView uiVegetationList;
        private ControlView controlView;

        private void BuildControlLayout()
        {
            if (controlRoot != null) return;

            var uxmlControl = ConfigTools.GetControlUxml(config);
            controlRoot = PageAssembler.Assemble(pageContainer, uxmlControl);
            controlRoot.AddToClassList("mt-frame");

            btnTabPainting = controlRoot.Q<Button>("Painting");
            btnTabGenerate = controlRoot.Q<Button>("Generate");
            btnTabSettings = controlRoot.Q<Button>("Settings");
            controlTabContent = controlRoot.Q<VisualElement>("TabContent");

            if (btnTabPainting != null) btnTabPainting.clicked += () => SwitchToTab(TabType.Paint);
            if (btnTabGenerate != null) btnTabGenerate.clicked += () => SwitchToTab(TabType.Generate);
            if (btnTabSettings != null) btnTabSettings.clicked += () => SwitchToTab(TabType.Settings);

            if (!ConfigTools.IsComplete(config, out _)) SwitchToTab(TabType.Settings);
            else SwitchToTab(TabType.Paint);
        }

        public void SwitchToTab(TabType type)
        {
            if (controlRoot == null) BuildControlLayout();

            if (type != TabType.Settings && !ConfigTools.IsComplete(config, out _)) type = TabType.Settings;

            currentTab = type;
            controlTabContent.Clear();
            UpdateTabButtonState(type);

            switch (type)
            {
                case TabType.Settings:
                    LoadSettingsContent();
                    break;
                case TabType.Paint:
                case TabType.Generate:
                    LoadOperationContent(type);
                    break;
            }
            NotifyWindowStateChanged();
        }

        private void UpdateTabButtonState(TabType activeType)
        {
            void SetActive(Button btn, bool active)
            {
                if (btn == null) return;
                if (active) btn.AddToClassList("mt-tabbutton--active");
                else btn.RemoveFromClassList("mt-tabbutton--active");

                // 仅当在 Settings 页且配置不完整时禁用其他 Tab
                if (activeType == TabType.Settings && !ConfigTools.IsComplete(config, out _))
                {
                    if (btn != btnTabSettings) btn.SetEnabled(false);
                }
                else btn.SetEnabled(true);
            }

            SetActive(btnTabPainting, activeType == TabType.Paint);
            SetActive(btnTabGenerate, activeType == TabType.Generate);
            SetActive(btnTabSettings, activeType == TabType.Settings);
        }

        private void NotifyWindowStateChanged()
        {
            WindowStateChanged?.Invoke(true, currentTab == TabType.Settings, currentTab == TabType.Paint);
        }

        private void LoadSettingsContent()
        {
            var uxml = ConfigTools.GetSettingsUxml();
            var page = PageAssembler.Assemble(controlTabContent, uxml);
            var view = new SettingsTabView(this, page);
            view.Setup();
        }

        private void LoadOperationContent(TabType type)
        {
            var scroll = new ScrollView { mode = ScrollViewMode.Vertical };
            scroll.AddToClassList("mt-scroll");
            controlTabContent.Add(scroll);

            // 共享部分
            var uxmlShared = ConfigTools.GetVegetationSharedUxml(config);
            var sharedRoot = PageAssembler.Assemble(scroll, uxmlShared);

            session.ReloadAvailableProfiles();
            SetupVegetationList(sharedRoot);
            BindPropertyPanel(sharedRoot);

            // 特定部分
            VisualTreeAsset specificUxml = (type == TabType.Paint)
                ? ConfigTools.GetPaintUxml(config)
                : ConfigTools.GetGenerateUxml(config);

            var specificRoot = PageAssembler.Assemble(scroll, specificUxml);

            if (type == TabType.Paint)
            {
                var view = new PaintingTabView(this, specificRoot);
                view.Setup();
            }
            else
            {
                var view = new GenerateTabView(this, specificRoot);
                view.Setup();
            }
        }

        // --- List & Binding Logic ---

        private void SetupVegetationList(VisualElement root)
        {
            var rowUxml = ConfigTools.GetVegetationProfileRowUxml(config);
            controlView = new ControlView(root, rowUxml);

            var thumbView = new ThumbListView(
                ConfigTools.GetPrefabIconUxml(config),
                CreateThumbCallbacks()
            );

            var dragView = new DraggableAddSlotView(
                ConfigTools.GetDraggableAreaUxml(config),
                new DraggableAddSlotView.DraggableAddSlotViewCallbacks
                {
                    OpenPrefabPickerForNewItem = (p) => session.PrefabPicker?.OpenForNew(p),
                    AddPrefabAsNewItem = (p, go) => session.PrefabAssignment?.AddPrefabAsNewItem(p, go)
                }
            );

            // 修复 cast 错误: 使用 ToList() 或显式转换
            var extraProfiles = Tools.MTPBrushContext.ExtraProfiles as IEnumerable<Runtime.Profiles.VegetationProfile>;

            controlView.SetupVegetationProfileList(
                session.AvailableProfiles,
                extraProfiles?.ToList() ?? new List<Runtime.Profiles.VegetationProfile>(), // 修复 CS1503
                CreateControlViewCallbacks(),
                dragView.MakeDraggableArea,
                thumbView.MakeThumb,
                CalculateThumbRows
            );

            uiVegetationList = controlView.ListView;
        }

        // --- Generate Tab 需要的辅助方法 (修复 missing method 错误) ---

        public void PopulateTerrainListUI(VisualElement root)
        {
            if (root == null) return;
            // 委托给 TerrainController 处理 UI 列表构建，或者在这里实现
            // 简单起见，我们直接在这里实现列表构建逻辑
            var container = root.Q<VisualElement>("TerrainList");
            if (container == null) return;

            var uiData = session.TerrainListUIData;

            if (container is Foldout fold)
                fold.style.display = uiData.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            var listView = container.Q<ListView>("TerrainListLV");
            if (listView == null)
            {
                container.Clear();
                listView = new ListView
                {
                    name = "TerrainListLV",
                    selectionType = SelectionType.None,
                    virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                    fixedItemHeight = 24
                };
                listView.AddToClassList("mt-terrain-list");
                container.Add(listView);
            }

            listView.itemsSource = uiData;

            // 样式调整
            if (uiData.Count <= 10)
            {
                listView.RemoveFromClassList("mt-terrain-list--max10");
                listView.AddToClassList("mt-terrain-list--auto");
            }
            else
            {
                listView.RemoveFromClassList("mt-terrain-list--auto");
                listView.AddToClassList("mt-terrain-list--max10");
            }

            listView.makeItem = () =>
            {
                var of = new ObjectField { objectType = typeof(Terrain), allowSceneObjects = true, label = "" };
                of.AddToClassList("mt-terrain-list__item");
                return of;
            };
            listView.bindItem = (e, i) =>
            {
                if (e is ObjectField of && i >= 0 && i < uiData.Count) of.SetValueWithoutNotify(uiData[i]);
            };
        }

        public void UpdateGenerateActionsVisibility(VisualElement root)
        {
            if (root == null) return;
            bool hasTerrains = session.TerrainListUIData.Count > 0;
            var btnGen = root.Q<VisualElement>("GenerateTerrainObject");
            var btnClr = root.Q<VisualElement>("ClearTerrainObject");
            if (btnGen != null) btnGen.style.display = hasTerrains ? DisplayStyle.Flex : DisplayStyle.None;
            if (btnClr != null) btnClr.style.display = hasTerrains ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // --- View Binding Wrappers ---

        public void BindBrushControls(VisualElement root)
        {
            new BrushView(root).Bind(session.Brush);
        }

        public void BindGenerateFilterControls(VisualElement root)
        {
            new GenerateFilterView(root).Bind(session.GenFilter);
            // 修复 VegetationGenerator 引用
            var toggle = root.Q<Toggle>("UseBurstPoissonGen");
            if (toggle != null)
            {
                toggle.SetValueWithoutNotify(VegetationGenerator.UseBurstPoisson);
                toggle.RegisterValueChangedCallback(e => VegetationGenerator.UseBurstPoisson = e.newValue);
            }
        }

        public void SetPreviewListContainer(VisualElement ve)
        {
            uiPreviewPrefabList = ve;
            RefreshPreviewUI();
        }

        // --- Callbacks Construction ---

        private ControlViewCallbacks CreateControlViewCallbacks()
        {
            // 简化路径获取
            string GetPath() => !string.IsNullOrEmpty(config?.recipeGenerationPath) ? config.recipeGenerationPath : "Assets/MrTerrainPainter/Data";

            return new ControlViewCallbacks
            {
                CreateNewVegetationProfileAsset = () =>
                {
                    var p = session.ProfileController.CreateNewVegetationProfileAsset(GetPath());
                    session.SetCurrentProfile(p);
                    session.ReloadAvailableProfiles();
                },
                ReloadAvailableProfiles = session.ReloadAvailableProfiles,
                RefreshAllUI = RefreshAllUI,
                SetListSelectionToCurrentProfile = () => uiVegetationList?.ClearSelection(),
                DeleteVegetationProfileAsset = (p) =>
                {
                    if (EditorUtility.DisplayDialog("删除", $"确定删除 {p.name}?", "是", "否"))
                    {
                        session.ProfileController.DeleteVegetationProfileAsset(p);
                        session.ReloadAvailableProfiles();
                    }
                },
                SetCurrentProfile = (p) => { session.SetCurrentProfile(p); },
                ResetSelectionForProfileChange = () => session.UIState.ClearSelection(),
                GetCurrentProfile = () => session.CurrentProfile,
                OnListContentWidthMeasured = w => session.UIState.ListContentWidth = w
            };
        }

        private ThumbListView.ThumbListViewCallbacks CreateThumbCallbacks()
        {
            return new ThumbListView.ThumbListViewCallbacks
            {
                GetCurrentProfile = () => session.CurrentProfile,
                SetCurrentProfile = session.SetCurrentProfile,
                GetSelectedItemIndex = () => session.UIState.SelectedItemIndex,
                SetSelectedItemIndex = (i) =>
                {
                    session.UIState.SelectedItemIndex = i;
                    UpdatePropertyPanel();
                    RefreshPreviewUI();
                },
                IsIndexSelected = session.UIState.IsThumbSelected,
                AddSelectedIndex = session.UIState.AddThumbSelection,
                RemoveSelectedIndex = session.UIState.RemoveThumbSelection,
                ClearSelectedIndices = session.UIState.ClearThumbSelection,
                GetSelectedIndices = session.UIState.GetSelectedThumbIndices,
                UpdatePropertyPanelFromSelectedItem = UpdatePropertyPanel,
                RefreshVegetationListUI = RefreshProfileListUI,
                RefreshPreviewListUI = RefreshPreviewUI,
                RemoveItemAtFromProfile = session.PrefabAssignment.RemoveItemAtFromProfile,
                RemoveItemsAtFromProfile = session.PrefabAssignment.RemoveItemsAtFromProfile,
                SetItemType = session.PrefabAssignment.SetItemType,
                OpenPrefabPickerForItem = (p, i) => session.PrefabPicker.OpenForItem(p, i),
                GetAvailableTypes = () => config.mappingEntries.Select(e => e.type).Distinct().ToList(),
                OnItemSelected = session.PrefabAssignment.OnItemSelected  // [新增] 选中项时自动切换分布模式
            };
        }

        // --- Common Refresh Logic ---

        private void RefreshAllUI() { RefreshProfileListUI(); RefreshPreviewUI(); UpdatePropertyPanel(); }

        private void RefreshProfileListUI()
        {
            if (controlView != null)
            {
                controlView.Refresh();
            }
            // 回退兼容（如果还保留了旧逻辑）
            else if (uiVegetationList != null)
            {
                uiVegetationList.itemsSource = session.AvailableProfiles;
                uiVegetationList.Rebuild();
            }
        }

        private void RefreshPreviewUI()
        {
            if (uiPreviewPrefabList == null) return;
            uiPreviewPrefabList.Clear();
            var profile = session.CurrentProfile;
            if (profile == null || profile.Items == null) return;
            var dragView = new Views.DraggableAddSlotView(
                ConfigTools.GetDraggableAreaUxml(config),
                new Views.DraggableAddSlotView.DraggableAddSlotViewCallbacks
                {
                    OpenPrefabPickerForNewItem = (p) => session.PrefabPicker?.OpenForNew(p),
                    AddPrefabAsNewItem = (p, go) => session.PrefabAssignment?.AddPrefabAsNewItem(p, go)
                }
            );
            var addArea = dragView.MakeDraggableArea(profile);
            if (addArea != null) uiPreviewPrefabList.Add(addArea);
            var thumbView = new Views.ThumbListView(
                ConfigTools.GetPrefabIconUxml(config),
                CreateThumbCallbacks()
            );
            int count = Mathf.Min(9, profile.Items.Count);
            for (int i = 0; i < count; i++)
            {
                var item = profile.Items[i];
                if (item != null) item.Index = i;
                var ve = thumbView.MakeThumb(profile, item, i);
                if (ve != null) uiPreviewPrefabList.Add(ve);
            }
        }

        private void UpdatePropertyPanel() => propertyPanelView?.UpdateFromSelectedItem();

        private void BindPropertyPanel(VisualElement root)
        {
            propertyPanelView = new PropertyPanelView(root);
            propertyPanelView.Bind(new PropertyPanelView.PropertyPanelCallbacks
            {
                GetSelectedItem = GetSelectedItem,
                GetCurrentProfile = () => session.CurrentProfile,
                GetSelectedItemIndex = () => session.UIState.SelectedItemIndex,
                RemoveItemAt = idx => session.PrefabAssignment?.RemoveItemAt(idx),
                AssignPrefabToItem = (p, i, go) => session.PrefabAssignment?.AssignPrefabToItem(p, i, go),
                RefreshPreviewListUI = RefreshPreviewUI,
                RefreshVegetationListUI = RefreshProfileListUI,
                UpdatePropertyPanelFromSelectedItem = UpdatePropertyPanel,
                MarkCurrentProfileDirty = () => { if (session.CurrentProfile) EditorUtility.SetDirty(session.CurrentProfile); },
                ScanSelectedTerrainsForFacades = () => session.ScanSelectedTerrainsForFacades(),
                BakeCachedFacades = () => session.BakeCachedFacades(),
                BatchSetMinRadius = (val) =>
                {
                    var profile = session.CurrentProfile;
                    if (profile == null || profile.Items == null) return;
                    var indices = session.UIState.GetSelectedThumbIndices()?.ToList();
                    if (indices == null || indices.Count == 0)
                    {
                        for (int i = 0; i < profile.Items.Count; i++)
                        {
                            var it = profile.Items[i];
                            if (it != null) it.minRadius = Mathf.Max(0f, val);
                        }
                    }
                    else
                    {
                        for (int k = 0; k < indices.Count; k++)
                        {
                            int idx = indices[k];
                            if (idx < 0 || idx >= profile.Items.Count) continue;
                            var it = profile.Items[idx];
                            if (it != null) it.minRadius = Mathf.Max(0f, val);
                        }
                    }
                    if (profile != null) EditorUtility.SetDirty(profile);
                    RefreshPreviewUI();
                    UpdatePropertyPanel();
                }
            });
        }



        private Runtime.Profiles.VegetationItem GetSelectedItem()
        {
            var items = session?.CurrentProfile?.Items;
            int idx = session?.UIState.SelectedItemIndex ?? -1;
            if (items == null || idx < 0 || idx >= items.Count) return null;
            return items[idx];
        }

        private int CalculateThumbRows(int count)
        {
            // 【修复】给一个安全的最小宽度默认值 (例如 300)，防止除以 0 或极小值
            // 当窗口刚打开时，ListContentWidth 可能是 0
            float width = session?.UIState.ListContentWidth ?? 0f;
            if (width < 50f) width = uiVegetationList?.resolvedStyle.width ?? 350f;
            if (width < 50f) width = 350f; // 最后的保底

            // 减去一些 Padding (左右各约 20px)
            float usableWidth = width - 40f;

            const float itemWidth = 64f + 8f; // ThumbSize + Gap

            // 计算一行能放几个
            int perRow = Mathf.FloorToInt(Mathf.Max(1, usableWidth / itemWidth));

            // 计算行数
            return Mathf.CeilToInt(count / (float)perRow);
        }

        private VisualElement uiPreviewPrefabList;
    }
}

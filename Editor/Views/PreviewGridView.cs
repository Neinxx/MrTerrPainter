using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using PrefabType = MrTerrainPainter.Runtime.Profiles.PrefabType;

namespace MrTerrainPainter.Editor.Views
{
    public class PreviewGridView
    {
        private readonly VisualElement container;
        private VisualElement pager;
        private readonly System.Func<List<VegetationItem>> getItems;
        private readonly System.Func<int> getSelectedIndex;
        private readonly System.Action<int> setSelectedIndex;
        private readonly System.Func<VegetationProfile> getCurrentProfile;
        private readonly System.Action<int> removeItemAt;
        private readonly System.Action<int, PrefabType> setItemType;
        private readonly System.Action refreshVegetationListUI;
        private readonly System.Action refreshPreviewListUI;

        private readonly Dictionary<int, Texture2D> previewTexCache = new();
        public ListView ListView { get; private set; }
        private int pageIndex;
        private int pageSize = 30;

        public PreviewGridView(
            VisualElement container,
            System.Func<List<VegetationItem>> getItems,
            System.Func<int> getSelectedIndex,
            System.Action<int> setSelectedIndex,
            System.Func<VegetationProfile> getCurrentProfile,
            System.Action<int> removeItemAt,
            System.Action<int, PrefabType> setItemType,
            System.Action refreshVegetationListUI,
            System.Action refreshPreviewListUI)
        {
            this.container = container;
            this.getItems = getItems;
            this.getSelectedIndex = getSelectedIndex;
            this.setSelectedIndex = setSelectedIndex;
            this.getCurrentProfile = getCurrentProfile;
            this.removeItemAt = removeItemAt;
            this.setItemType = setItemType;
            this.refreshVegetationListUI = refreshVegetationListUI;
            this.refreshPreviewListUI = refreshPreviewListUI;
        }

        public void Render()
        {
            if (container == null) return;
            var itemsAll = getItems?.Invoke() ?? new List<VegetationItem>();
            int total = itemsAll.Count;
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(total / (float)pageSize));
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            var items = itemsAll.Skip(pageIndex * pageSize).Take(pageSize).ToList();
            EnsurePager(pageIndex, pageCount);
            if (ListView == null)
            {
                var lv = new ListView
                {
                    selectionType = SelectionType.None,
                    itemsSource = items,
                    virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                    fixedItemHeight = 72
                };
                lv.style.flexGrow = 1;
                lv.makeItem = () =>
                {
                    var box = new VisualElement();
                    box.AddToClassList("preview-item");
                    var img = new Image();
                    img.AddToClassList("preview-item__image");
                    box.Add(img);
                    return box;
                };
                lv.bindItem = (elem, i) =>
                {
                    var it = (i >= 0 && i < items.Count) ? items[i] : null;
                    var img = elem.Q<Image>();
                    Texture2D tex = null;
                    if (it != null && it.prefab != null)
                    {
                        var id = it.prefab.GetInstanceID();
                        if (!previewTexCache.TryGetValue(id, out tex) || tex == null)
                        {
                            tex = AssetPreview.GetAssetPreview(it.prefab) ?? AssetPreview.GetMiniThumbnail(it.prefab);
                            previewTexCache[id] = tex;
                            if (tex == null) refreshPreviewListUI?.Invoke();
                        }
                    }
                    img.image = tex;
                    elem.userData = i;
                    var sel = i == (getSelectedIndex != null ? getSelectedIndex() : -1);
                    if (sel) elem.AddToClassList("preview-item--selected"); else elem.RemoveFromClassList("preview-item--selected");
                    elem.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button == 0)
                        {
                            setSelectedIndex?.Invoke(i);
                            evt.StopPropagation();
                        }
                        else if (evt.button == 1)
                        {
                            var menu = new GenericMenu();
                            menu.AddItem(new GUIContent("删除"), false, () =>
                            {
                                var idx = i;
                                MrTerrainPainter.Editor.Utils.UIThrottle.RunNextFrame(() =>
                                {
                                    removeItemAt?.Invoke(idx);
                                    refreshVegetationListUI?.Invoke();
                                    refreshPreviewListUI?.Invoke();
                                });
                            });
                            var values = (PrefabType[])System.Enum.GetValues(typeof(PrefabType));
                            for (int vi = 0; vi < values.Length; vi++)
                            {
                                var val = values[vi];
                                bool isCurrent = it != null && it.prefabType == val;
                                menu.AddItem(new GUIContent($"类型/{val}"), isCurrent, () =>
                                {
                                    var idx = i;
                                    setItemType?.Invoke(idx, val);
                                    var profile = getCurrentProfile?.Invoke();
                                    if (profile != null) EditorUtility.SetDirty(profile);
                                    MrTerrainPainter.Editor.Utils.UIThrottle.RunOnPanel(ListView, () => UpdateSelectionVisuals());
                                });
                            }
                            menu.ShowAsContext();
                            evt.StopPropagation();
                        }
                    });
                };
                ListView = lv;
                container.Clear();
                container.Add(pager);
                container.Add(ListView);
            }
            else
            {
                ListView.itemsSource = items;
                ListView.Rebuild();
                UpdatePagerLabel(pageIndex, pageCount);
            }
            MrTerrainPainter.Editor.Utils.UIThrottle.RunOnPanel(ListView, UpdateSelectionVisuals);
        }

        private void EnsurePager(int index, int count)
        {
            if (pager == null)
            {
                pager = new VisualElement();
                pager.AddToClassList("preview-pager");
                var left = new Button(() => { pageIndex = Mathf.Max(0, pageIndex - 1); Render(); }) { text = "<" };
                left.AddToClassList("preview-pager__btn");
                var label = new Label();
                label.name = "PagerLabel";
                label.AddToClassList("preview-pager__label");
                var right = new Button(() => { pageIndex = pageIndex + 1; Render(); }) { text = ">" };
                right.AddToClassList("preview-pager__btn");
                pager.Add(left);
                pager.Add(label);
                pager.Add(right);
            }
            UpdatePagerLabel(index, count);
        }

        private void UpdatePagerLabel(int index, int count)
        {
            var label = pager?.Q<Label>("PagerLabel");
            if (label != null) label.text = (count > 0 ? (index + 1) : 0) + " / " + count;
        }

        public void UpdateSelectionVisuals()
        {
            if (ListView == null) return;
            var children = ListView.contentContainer.Children().ToList();
            for (int ci = 0; ci < children.Count; ci++)
            {
                var ve = children[ci];
                var idx = ve.userData is int n ? n : -1;
                if (idx < 0) continue;
                if (idx == (getSelectedIndex != null ? getSelectedIndex() : -1)) ve.AddToClassList("preview-item--selected"); else ve.RemoveFromClassList("preview-item--selected");
            }
        }
    }
}

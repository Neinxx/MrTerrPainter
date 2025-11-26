using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    /// <summary>
    /// 缩略图列表视图：负责单个缩略图的渲染与交互
    /// </summary>
    public class ThumbListView
    {
        #region Callbacks Definition
        public struct ThumbListViewCallbacks
        {
            public Func<VegetationProfile> GetCurrentProfile;
            public Action<VegetationProfile> SetCurrentProfile;
            public Func<int> GetSelectedItemIndex;
            public Action<int> SetSelectedItemIndex;
            public Func<int, bool> IsIndexSelected;
            public Action<int> AddSelectedIndex;
            public Action<int> RemoveSelectedIndex;
            public Action ClearSelectedIndices;
            public Func<IEnumerable<int>> GetSelectedIndices;
            public Action UpdatePropertyPanelFromSelectedItem;
            public Action RefreshVegetationListUI;
            public Action RefreshPreviewListUI;
            public Action<VegetationProfile, int> RemoveItemAtFromProfile;
            public Action<VegetationProfile, IEnumerable<int>> RemoveItemsAtFromProfile;
            public Action<VegetationProfile, int, Runtime.Profiles.PrefabType> SetItemType;
            public Action<VegetationProfile, int> OpenPrefabPickerForItem;
            public Func<IEnumerable<Runtime.Profiles.PrefabType>> GetAvailableTypes;
        }
        #endregion

        private readonly VisualTreeAsset prefabIconTemplate;
        private readonly ThumbListViewCallbacks cb;

        private const string ThumbItemClassName = "thumb-item";
        private const string SelectedClassName = "thumb-item--selected";
        private const string EmptyClassName = "thumb-item--empty";

        public ThumbListView(VisualTreeAsset prefabIconUxml, ThumbListViewCallbacks callbacks)
        {
            prefabIconTemplate = prefabIconUxml;
            cb = callbacks;
        }

        /// <summary>
        /// 生成单个缩略图项 (主入口)
        /// </summary>
        public VisualElement MakeThumb(VegetationProfile profile, VegetationItem item, int index)
        {
            var thumb = CreateThumbRoot(out VisualElement rootElement);

            RenderIconAndType(rootElement, thumb, item);
            UpdateSelectionState(profile, index, thumb);
            RegisterInteractions(profile, item, index, thumb);

            return thumb;
        }

        #region 1. UI Creation & Styling

        private VisualElement CreateThumbRoot(out VisualElement rootElement)
        {
            VisualElement thumb;

            if (prefabIconTemplate != null)
            {
                rootElement = prefabIconTemplate.Instantiate();
                thumb = rootElement.Q<VisualElement>("ThumbItem") ?? rootElement;
            }
            else
            {
                rootElement = null;
                thumb = new VisualElement { style = { width = 64, height = 64 } };
            }

            thumb.AddToClassList(ThumbItemClassName);
            thumb.pickingMode = PickingMode.Position;
            return thumb;
        }

        private void UpdateSelectionState(VegetationProfile profile, int index, VisualElement thumb)
        {
            var currentProfile = cb.GetCurrentProfile?.Invoke();
            var isSelected = cb.IsIndexSelected?.Invoke(index) ?? false;

            if (currentProfile == profile && isSelected)
            {
                thumb.AddToClassList(SelectedClassName);
            }
            else
            {
                thumb.RemoveFromClassList(SelectedClassName);
            }
        }

        #endregion

        #region 2. Rendering Logic (Flattened)

        private void RenderIconAndType(VisualElement root, VisualElement thumb, VegetationItem item)
        {
            UpdateTypeLabel(root, item);

            var go = item?.prefab;
            var cachedTex = MrTerrainPainter.Editor.Services.AssetPreviewCache.GetCached(go);

            // Case 1: Cache Hit
            if (cachedTex != null)
            {
                SetIconTexture(root, thumb, cachedTex);
                return;
            }

            // Case 2: Empty Slot
            if (go == null)
            {
                SetupEmptyState(root, thumb, item);
                return;
            }

            // Case 3: Async Load
            thumb.AddToClassList(EmptyClassName);
            MrTerrainPainter.Editor.Services.AssetPreviewCache.Request(go, tex => ApplyAsyncTexture(thumb, root, tex));
        }

        private void ApplyAsyncTexture(VisualElement thumb, VisualElement root, Texture2D tex)
        {
            // Guard: UI might be destroyed
            if (thumb == null) return;

            if (tex != null)
            {
                SetIconTexture(root, thumb, tex);
                thumb.RemoveFromClassList(EmptyClassName);
            }

            // [Critical] Force Repaint for async update
            thumb.MarkDirtyRepaint();
        }

        private void SetIconTexture(VisualElement root, VisualElement thumb, Texture2D tex)
        {
            var iconImage = root?.Q<Image>();
            if (iconImage != null)
            {
                iconImage.scaleMode = ScaleMode.ScaleToFit;
                iconImage.image = tex;
            }
            else
            {
                var target = root?.Q<VisualElement>("Icon") ?? thumb;
                target.style.backgroundImage = new StyleBackground(tex);
            }
        }

        private void SetupEmptyState(VisualElement root, VisualElement thumb, VegetationItem item)
        {
            thumb.AddToClassList(EmptyClassName);
            var icon = root?.Q<VisualElement>("Icon");
            if (icon != null)
            {
                icon.style.alignItems = Align.Center;
                icon.style.justifyContent = Justify.Center;
            }

            // Click empty slot to open picker
            thumb.RegisterCallback<PointerDownEvent, VegetationItem>((evt, capturedItem) =>
            {
                if (evt.button != 0) return;
                cb.OpenPrefabPickerForItem?.Invoke(cb.GetCurrentProfile?.Invoke(), capturedItem?.Index ?? -1);
                evt.StopPropagation();
            }, item);
        }

        private void UpdateTypeLabel(VisualElement root, VegetationItem item)
        {
            var typeLabel = root?.Q<Label>("Type");
            if (typeLabel == null) return;

            var typeName = (item?.prefab == null) ? "Null" : item.prefabType.ToString();
            typeLabel.text = typeName;
            typeLabel.tooltip = typeName;
        }

        #endregion

        #region 3. Interaction Logic (Flattened)

        private void RegisterInteractions(VegetationProfile profile, VegetationItem item, int index, VisualElement thumb)
        {
            thumb.RegisterCallback<PointerDownEvent>(e => HandlePointerDown(e, profile, index, thumb));
        }

        private void HandlePointerDown(PointerDownEvent e, VegetationProfile profile, int index, VisualElement thumb)
        {
            if (profile == null) return;

            // Left Click
            if (e.button == 0)
            {
                HandleLeftClick(profile, index, e.ctrlKey, e.shiftKey);
                thumb.MarkDirtyRepaint(); // Instant feedback
                e.StopPropagation();
                return;
            }

            // Right Click
            if (e.button == 1)
            {
                HandleRightClick(profile, index, thumb);
                e.StopPropagation();
                return;
            }
        }

        private void HandleLeftClick(VegetationProfile profile, int index, bool ctrl, bool shift)
        {
            cb.SetCurrentProfile?.Invoke(profile);
            HandleSelection(index, ctrl, shift);

            cb.UpdatePropertyPanelFromSelectedItem?.Invoke();
            cb.RefreshVegetationListUI?.Invoke();
            cb.RefreshPreviewListUI?.Invoke();
        }

        private void HandleRightClick(VegetationProfile profile, int index, VisualElement thumb)
        {
            bool isSelected = cb.IsIndexSelected?.Invoke(index) ?? false;

            // Auto-select if right-clicking an unselected item
            if (!isSelected)
            {
                cb.SetCurrentProfile?.Invoke(profile);
                cb.ClearSelectedIndices?.Invoke();
                cb.AddSelectedIndex?.Invoke(index);
                cb.SetSelectedItemIndex?.Invoke(index);
                thumb.MarkDirtyRepaint();
            }

            ShowContextMenu(profile, index);
        }

        private void HandleSelection(int index, bool ctrlKey, bool shiftKey)
        {
            var selectedItemIndex = cb.GetSelectedItemIndex?.Invoke() ?? -1;

            if (ctrlKey)
            {
                if (cb.IsIndexSelected?.Invoke(index) ?? false)
                    cb.RemoveSelectedIndex?.Invoke(index);
                else
                    cb.AddSelectedIndex?.Invoke(index);

                cb.SetSelectedItemIndex?.Invoke(index);
            }
            else if (shiftKey && selectedItemIndex >= 0)
            {
                int start = Mathf.Min(selectedItemIndex, index);
                int end = Mathf.Max(selectedItemIndex, index);
                for (int i = start; i <= end; i++)
                    cb.AddSelectedIndex?.Invoke(i);

                cb.SetSelectedItemIndex?.Invoke(index);
            }
            else
            {
                cb.ClearSelectedIndices?.Invoke();
                cb.AddSelectedIndex?.Invoke(index);
                cb.SetSelectedItemIndex?.Invoke(index);
            }
        }

        #endregion

        #region 4. Context Menu

        private void ShowContextMenu(VegetationProfile profile, int index)
        {
            var menu = new GenericMenu();
            var selectedIndices = cb.GetSelectedIndices?.Invoke()?.ToList() ?? new List<int>();
            var currentItem = (profile != null && profile.Items != null && index >= 0 && index < profile.Items.Count)
                ? profile.Items[index] : null;

            menu.AddItem(new GUIContent("删除该预制体"), false, () =>
                cb.RemoveItemAtFromProfile?.Invoke(profile, index));

            if (selectedIndices.Count > 1 || (selectedIndices.Count == 1 && selectedIndices.First() != index))
            {
                menu.AddItem(new GUIContent("删除选中的预制体(批量)"), false, () =>
                    cb.RemoveItemsAtFromProfile?.Invoke(profile, selectedIndices));
            }

            menu.AddSeparator("");
            AddTypeMenu(menu, profile, currentItem, selectedIndices, index);
            menu.AddSeparator("");
            AddCopyPasteMenu(menu, profile, currentItem, selectedIndices, index);

            menu.ShowAsContext();
        }

        private void AddTypeMenu(GenericMenu menu, VegetationProfile profile, VegetationItem currentItem, List<int> selectedIndices, int index)
        {
            var typeList = cb.GetAvailableTypes?.Invoke()?.ToList();
            if (typeList == null || typeList.Count == 0)
                typeList = ((Runtime.Profiles.PrefabType[])Enum.GetValues(typeof(Runtime.Profiles.PrefabType))).ToList();

            foreach (var valLocal in typeList)
            {
                bool isCurrent = currentItem != null && currentItem.prefabType == valLocal;
                menu.AddItem(new GUIContent($"类型/{valLocal}"), isCurrent, () =>
                {
                    var indices = selectedIndices.Count > 0 ? selectedIndices : new List<int> { index };
                    foreach (var idx in indices)
                        cb.SetItemType?.Invoke(profile, idx, valLocal);
                    cb.RefreshPreviewListUI?.Invoke();
                });
            }
        }

        private void AddCopyPasteMenu(GenericMenu menu, VegetationProfile profile, VegetationItem currentItem, List<int> selectedIndices, int index)
        {
            menu.AddItem(new GUIContent("复制配置"), false, () =>
            {
                if (profile == null || currentItem == null) return;
                CopyBuffer.Set(currentItem);
            });

            menu.AddItem(new GUIContent("粘贴配置到选中"), false, () =>
            {
                if (profile == null) return;
                var indices = selectedIndices.Count > 0 ? selectedIndices : new List<int> { index };
                foreach (var idx in indices)
                {
                    if (idx < 0 || idx >= profile.Items.Count) continue;
                    var dst = profile.Items[idx];
                    if (dst != null) CopyBuffer.ApplyTo(dst);
                }
                cb.UpdatePropertyPanelFromSelectedItem?.Invoke();
                cb.RefreshPreviewListUI?.Invoke();
            });
        }

        private static class CopyBuffer
        {
            private static VegetationItem snapshot;
            public static void Set(VegetationItem src)
            {
                if (src == null) return;
                snapshot = new VegetationItem();
                // Manual Deep Copy of simplified fields
                snapshot.prefabType = src.prefabType;
                snapshot.weight = src.weight;
                snapshot.heightRange = src.heightRange;
                snapshot.slopeRange = src.slopeRange;
                snapshot.minSpacing = src.minSpacing;
                snapshot.yRotationRange = src.yRotationRange;
                snapshot.edgeSlopeThreshold = src.edgeSlopeThreshold;
                snapshot.embedDepthRange = src.embedDepthRange;
                snapshot.edgeReferenceHeightMeters = src.edgeReferenceHeightMeters;
                snapshot.offsets = src.offsets;
                snapshot.edgeOffsets = src.edgeOffsets;
                snapshot.probeStep = src.probeStep;
                snapshot.probeMaxDist = src.probeMaxDist;
                snapshot.edgeStacking = src.edgeStacking;
                snapshot.edgeStackingOffsetMeters = src.edgeStackingOffsetMeters;
                snapshot.edgeTopScaleBias = src.edgeTopScaleBias;
                snapshot.facadeScaleOffset = src.facadeScaleOffset;
            }
            public static void ApplyTo(VegetationItem dst)
            {
                if (snapshot == null || dst == null) return;
                dst.prefabType = snapshot.prefabType;
                dst.weight = snapshot.weight;
                dst.heightRange = snapshot.heightRange;
                dst.slopeRange = snapshot.slopeRange;
                dst.minSpacing = snapshot.minSpacing;
                dst.yRotationRange = snapshot.yRotationRange;
                dst.edgeSlopeThreshold = snapshot.edgeSlopeThreshold;
                dst.embedDepthRange = snapshot.embedDepthRange;
                dst.edgeReferenceHeightMeters = snapshot.edgeReferenceHeightMeters;
                dst.offsets = snapshot.offsets;
                dst.edgeOffsets = snapshot.edgeOffsets;
                dst.probeStep = snapshot.probeStep;
                dst.probeMaxDist = snapshot.probeMaxDist;
                dst.edgeStacking = snapshot.edgeStacking;
                dst.edgeStackingOffsetMeters = snapshot.edgeStackingOffsetMeters;
                dst.edgeTopScaleBias = snapshot.edgeTopScaleBias;
                dst.facadeScaleOffset = snapshot.facadeScaleOffset;
            }
        }

        #endregion
    }
}
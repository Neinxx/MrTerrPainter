using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    // 缩略图列表视图：负责单个缩略图的渲染与交互
    public class ThumbListView
    {
        // 保持回调结构不变，它是外部服务的接口
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

        private readonly VisualTreeAsset prefabIconTemplate;
        private readonly ThumbListViewCallbacks cb;

        // 常量定义，避免硬编码字符串
        private const string ThumbItemClassName = "thumb-item";
        private const string SelectedClassName = "thumb-item--selected";
        private const string EmptyClassName = "thumb-item--empty";

        public ThumbListView(VisualTreeAsset prefabIconUxml, ThumbListViewCallbacks callbacks)
        {
            prefabIconTemplate = prefabIconUxml;
            cb = callbacks;
        }

        /// <summary>
        /// 生成单个缩略图项
        /// </summary>
        public VisualElement MakeThumb(VegetationProfile profile, VegetationItem item, int index)
        {
            // 1. 创建并初始化根元素和缩略图元素
            var thumb = CreateThumbRoot(out VisualElement rootElement);

            // 2. 渲染图标和类型标签
            RenderIconAndType(rootElement, thumb, item);

            // 3. 设置选中状态样式
            UpdateSelectionState(profile, index, thumb);

            // 4. 注册交互事件
            RegisterInteractions(profile, item, index, thumb);

            return thumb;
        }

        #region Helper Methods

        /// <summary>
        /// 创建并返回缩略图的 VisualElement 根节点。
        /// </summary>
        private VisualElement CreateThumbRoot(out VisualElement rootElement)
        {
            VisualElement thumb;
            rootElement = null;

            if (prefabIconTemplate != null)
            {
                rootElement = prefabIconTemplate.Instantiate();
                // 查找 UXML 中定义的缩略图元素，如果未找到则使用根元素
                thumb = rootElement.Q<VisualElement>("ThumbItem") ?? rootElement;
            }
            else
            {
                // 如果没有模板，则手动创建基本 VisualElement
                thumb = new VisualElement
                {
                    style = { width = 64, height = 64 }
                };
            }

            thumb.AddToClassList(ThumbItemClassName);
            thumb.pickingMode = PickingMode.Position;
            return thumb;
        }

        /// <summary>
        /// 渲染图标和类型标签。
        /// </summary>
        private void RenderIconAndType(VisualElement root, VisualElement thumb, VegetationItem item)
        {
            var go = item?.prefab;
            var tex = go ? (AssetPreview.GetAssetPreview(go) ?? AssetPreview.GetMiniThumbnail(go)) : null;

            var icon = root?.Q<VisualElement>("Icon");
            var iconImage = icon?.Q<Image>();
            var host = icon ?? thumb;

            if (tex != null)
            {
                if (iconImage != null)
                {
                    iconImage.scaleMode = ScaleMode.ScaleToFit;
                    iconImage.image = tex;
                }
                else
                {
                    host.style.backgroundImage = new StyleBackground(tex);
                }
            }
            else
            {
                // 处理空预制体的情况
                thumb.AddToClassList(EmptyClassName);
                if (icon != null)
                {
                    icon.style.alignItems = Align.Center;
                    icon.style.justifyContent = Justify.Center;
                }

                // 注册点击打开 Prefab Picker 的事件（仅左键）
                thumb.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    cb.OpenPrefabPickerForItem?.Invoke(cb.GetCurrentProfile?.Invoke(), item?.Index ?? -1);
                    evt.StopPropagation();
                });
            }

            // 设置类型标签
            if (root != null)
            {
                var typeLabel = root.Q<Label>("Type");
                if (typeLabel != null && item != null)
                {
                    var isNull = item.prefab == null;
                    var typeName = isNull ? "Null" : item.prefabType.ToString();
                    typeLabel.text = typeName;
                    typeLabel.tooltip = typeName;
                }
            }
        }

        /// <summary>
        /// 根据当前选中状态更新缩略图的样式。
        /// </summary>
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

        /// <summary>
        /// 注册左键选择和右键菜单事件。
        /// </summary>
        private void RegisterInteractions(VegetationProfile profile, VegetationItem item, int index, VisualElement thumb)
        {
            // 左键选择（支持 Ctrl/Shift 多选）
            thumb.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;

                // 1. 设置当前 Profile
                if (profile == null) return;
                cb.SetCurrentProfile?.Invoke(profile);

                // 2. 处理选择逻辑
                HandleSelection(index, e.ctrlKey, e.shiftKey);

                // 3. 刷新 UI 和属性面板
                cb.UpdatePropertyPanelFromSelectedItem?.Invoke();
                cb.RefreshVegetationListUI?.Invoke();
                cb.RefreshPreviewListUI?.Invoke();

                e.StopPropagation();
            });

            // 右键菜单
            thumb.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return; // 检查是否为右键

                if (profile == null) return;
                // 确保当前项被选中，如果未选中，则先选中它
                if (!(cb.IsIndexSelected?.Invoke(index) ?? false))
                {
                    cb.SetCurrentProfile?.Invoke(profile);
                    cb.ClearSelectedIndices?.Invoke();
                    cb.AddSelectedIndex?.Invoke(index);
                    cb.SetSelectedItemIndex?.Invoke(index);
                    // 不需要立刻刷新 UI，因为菜单弹出后用户才会操作
                }

                ShowContextMenu(profile, index);
                e.StopPropagation();
            });
        }

        /// <summary>
        /// 处理缩略图的单选/多选逻辑。
        /// </summary>
        private void HandleSelection(int index, bool ctrlKey, bool shiftKey)
        {
            var selectedItemIndex = cb.GetSelectedItemIndex?.Invoke() ?? -1;

            if (ctrlKey)
            {
                // Ctrl: 切换选中状态
                if (cb.IsIndexSelected?.Invoke(index) ?? false)
                {
                    cb.RemoveSelectedIndex?.Invoke(index);
                }
                else
                {
                    cb.AddSelectedIndex?.Invoke(index);
                }
                cb.SetSelectedItemIndex?.Invoke(index);
            }
            else if (shiftKey && selectedItemIndex >= 0)
            {
                // Shift: 范围选择
                int start = Mathf.Min(selectedItemIndex, index);
                int end = Mathf.Max(selectedItemIndex, index);
                for (int i = start; i <= end; i++)
                {
                    cb.AddSelectedIndex?.Invoke(i);
                }
                cb.SetSelectedItemIndex?.Invoke(index);
            }
            else
            {
                // 单选: 清除所有选中，选中当前项
                cb.ClearSelectedIndices?.Invoke();
                cb.AddSelectedIndex?.Invoke(index);
                cb.SetSelectedItemIndex?.Invoke(index);
            }
        }

        /// <summary>
        /// 显示右键上下文菜单。
        /// </summary>
        private void ShowContextMenu(VegetationProfile profile, int index)
        {
            var menu = new GenericMenu();
            var selectedIndices = cb.GetSelectedIndices?.Invoke()?.ToList() ?? new List<int>();
            var currentItem = (profile != null && profile.Items != null && index >= 0 && index < profile.Items.Count)
                ? profile.Items[index]
                : null;

            // 1. 删除单个或选中的多个项
            menu.AddItem(new GUIContent("删除该预制体"), false, () =>
            {
                cb.RemoveItemAtFromProfile?.Invoke(profile, index);
            });

            // 批量删除（如果存在多个选中项，或者当前项不在已选中项中）
            if (selectedIndices.Count > 1 || (selectedIndices.Count == 1 && selectedIndices.First() != index))
            {
                menu.AddItem(new GUIContent("删除选中的预制体(批量)"), false, () =>
                {
                    // 确保删除的是当前 Profile 下选中的所有项
                    cb.RemoveItemsAtFromProfile?.Invoke(profile, selectedIndices);
                });
            }

            menu.AddSeparator("");

            // 2. 类型设置子菜单（动态枚举）
            var typeList = cb.GetAvailableTypes?.Invoke()?.ToList();
            if (typeList == null || typeList.Count == 0)
            {
                typeList = ((Runtime.Profiles.PrefabType[])Enum.GetValues(typeof(Runtime.Profiles.PrefabType))).ToList();
            }
            for (int vi = 0; vi < typeList.Count; vi++)
            {
                var valLocal = typeList[vi];
                bool isCurrent = currentItem != null && currentItem.prefabType == valLocal;
                menu.AddItem(new GUIContent($"类型/{valLocal}"), isCurrent, () =>
                {
                    // 批量或单项应用类型
                    var indices = selectedIndices != null && selectedIndices.Count > 0 ? selectedIndices : new List<int> { index };
                    foreach (var idxLocal in indices)
                    {
                        cb.SetItemType?.Invoke(profile, idxLocal, valLocal);
                    }
                    cb.RefreshPreviewListUI?.Invoke();
                });
            }

            menu.ShowAsContext();
        }

        #endregion
    }
}

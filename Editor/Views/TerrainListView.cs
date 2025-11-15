using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditorInternal;
using UnityEditor;

namespace MrTerrainPainter.Editor.Views
{
    // 地形列表视图：封装 Start/Control 页的 Terrain 列表渲染
    public class TerrainListView
    {
        private readonly VisualElement root;
        // 推荐将 ListView 的名称定义为常量，避免硬编码字符串和拼写错误
        private const string ListViewName = "TerrainListLV";
        private const string ContainerName = "TerrainList";
        private const string IMGUIContainerName = "TerrainReorderList";
        private ReorderableList reorderableList;

        public TerrainListView(VisualElement root)
        {
            this.root = root;
        }

        public void Refresh(List<Terrain> terrains)
        {
            if (root == null || terrains == null) return;

            // 1. 尝试查找列表容器 (VisualElement)
            var listContainer = root.Q<VisualElement>(ContainerName);

            // 2. 如果找到了容器，优先使用带增删的 ReorderableList（IMGUIContainer）
            if (listContainer != null)
            {
                UpdateFoldoutDisplay(listContainer, terrains.Count > 0);

                var imgui = listContainer.Q<IMGUIContainer>(IMGUIContainerName);
                if (imgui == null)
                {
                    imgui = new IMGUIContainer();
                    imgui.name = IMGUIContainerName;
                    listContainer.Clear();
                    listContainer.Add(imgui);
                }

                EnsureReorderableList(terrains);
                imgui.onGUIHandler = () => DrawReorderableList(terrains);
            }
            else
            {
                return;
            }
        }

        /// <summary>
        /// 检查容器自身或父级是否为 Foldout，并设置其 DisplayStyle
        /// </summary>
        private void UpdateFoldoutDisplay(VisualElement container, bool shouldDisplay)
        {
            var displayStyle = shouldDisplay ? DisplayStyle.Flex : DisplayStyle.None;

            // 兼容 Foldout 自身或父级为 Foldout 两种布局
            if (container is Foldout foldSelf)
            {
                foldSelf.style.display = displayStyle;
            }
            else if (container.parent is Foldout foldParent)
            {
                foldParent.style.display = displayStyle;
            }
        }

        private void EnsureReorderableList(List<Terrain> terrains)
        {
            if (reorderableList != null) return;
            reorderableList = new ReorderableList(terrains, typeof(Terrain), true, true, true, true);
            reorderableList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "Terrains");
            };
            reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var t = (index >= 0 && index < terrains.Count) ? terrains[index] : null;
                var newT = (Terrain)EditorGUI.ObjectField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), t, typeof(Terrain), true);
                if (newT != t)
                {
                    if (index >= 0 && index < terrains.Count)
                    {
                        terrains[index] = newT;
                    }
                }
            };
            reorderableList.onAddCallback = list =>
            {
                terrains.Add(null);
            };
            reorderableList.onRemoveCallback = list =>
            {
                if (list.index >= 0 && list.index < terrains.Count)
                {
                    terrains.RemoveAt(list.index);
                }
            };
        }

        private void DrawReorderableList(List<Terrain> terrains)
        {
            if (reorderableList == null)
            {
                EnsureReorderableList(terrains);
            }
            reorderableList.DoLayoutList();
        }
    }
}

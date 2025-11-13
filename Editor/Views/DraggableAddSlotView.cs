using System;
using System.Linq;
using MrTerrainPainter.Runtime.Profiles;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace MrTerrainPainter.Editor.Views
{
    // 永久可拖拽新增区域视图：用于在列表首位提供新增入口
    public class DraggableAddSlotView
    {
        public struct DraggableAddSlotViewCallbacks
        {
            public Action<VegetationProfile> OpenPrefabPickerForNewItem;
            public Action<VegetationProfile, GameObject> AddPrefabAsNewItem;
        }

        private readonly VisualTreeAsset draggableAreaTemplate;
        private readonly DraggableAddSlotViewCallbacks cb;

        public DraggableAddSlotView(VisualTreeAsset draggableAreaUxml, DraggableAddSlotViewCallbacks callbacks)
        {
            draggableAreaTemplate = draggableAreaUxml;
            cb = callbacks;
        }

        public VisualElement MakeDraggableArea(VegetationProfile profile)
        {
            if (profile == null) return null; // 提前返回
            VisualElement root = null;
            if (draggableAreaTemplate != null)
            {
                root = draggableAreaTemplate.Instantiate();
            }
            var thumb = root != null ? (root.Q<VisualElement>("ThumbItem") ?? root) : new VisualElement();
            if (root == null)
            {
                thumb.AddToClassList("thumb-item");
                thumb.style.width = 64;
                thumb.style.height = 64;
            }

            thumb.tooltip = "拖拽Prefab添加到列表，或点击选择Prefab新建条目";
            thumb.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                cb.OpenPrefabPickerForNewItem?.Invoke(profile);
                evt.StopPropagation();
            });
            thumb.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                bool hasPrefab = DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Any(o => o is GameObject);
                DragAndDrop.visualMode = hasPrefab ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                evt.StopPropagation();
            });
            thumb.RegisterCallback<DragPerformEvent>(evt =>
            {
                var pref = DragAndDrop.objectReferences.OfType<GameObject>().FirstOrDefault();
                if (pref == null) return; // 提前返回
                DragAndDrop.AcceptDrag();
                cb.AddPrefabAsNewItem?.Invoke(profile, pref);
                evt.StopPropagation();
            });
            return thumb;
        }
    }
}
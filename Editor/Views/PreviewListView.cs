using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    public class PreviewListView
    {
        private readonly VisualElement dropArea;
        private readonly System.Action<GameObject[]> onAddPrefabs;
        private readonly System.Func<bool> canAccept;

        public PreviewListView(VisualElement dropArea, System.Action<GameObject[]> onAddPrefabs, System.Func<bool> canAccept)
        {
            this.dropArea = dropArea;
            this.onAddPrefabs = onAddPrefabs;
            this.canAccept = canAccept;
            if (dropArea == null) return;
            dropArea.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            dropArea.RegisterCallback<DragPerformEvent>(OnDragPerform);
        }

        private void OnDragUpdated(DragUpdatedEvent e)
        {
            var refs = DragAndDrop.objectReferences;
            var hasGO = refs != null && refs.Any(o => o is GameObject);
            DragAndDrop.visualMode = (canAccept() && hasGO)
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;
            e.StopPropagation();
        }

        private void OnDragPerform(DragPerformEvent e)
        {
            var gos = DragAndDrop.objectReferences?.OfType<GameObject>().ToArray();
            if (gos != null && gos.Length > 0)
            {
                onAddPrefabs?.Invoke(gos);
            }
            DragAndDrop.AcceptDrag();
            e.StopPropagation();
        }
    }
}

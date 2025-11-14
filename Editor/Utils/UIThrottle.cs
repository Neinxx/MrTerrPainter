using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Utils
{
    public static class UIThrottle
    {
        public static void RunNextFrame(Action action)
        {
            EditorApplication.delayCall += () => action?.Invoke();
        }

        public static void RunOnPanel(VisualElement root, Action action)
        {
            if (root == null)
            {
                RunNextFrame(action);
                return;
            }
            root.schedule.Execute(() => action?.Invoke()).StartingIn(0);
        }
    }
}

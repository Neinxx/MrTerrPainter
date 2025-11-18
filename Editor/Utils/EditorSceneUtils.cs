using UnityEditor.SceneManagement;
using UnityEngine;

namespace MrTerrainPainter.Editor.Utils
{
    public static class EditorSceneUtils
    {
        public static void MarkSceneDirty()
        {
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }
    }
}

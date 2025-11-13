using System;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;

namespace MrTerrainPainter.Editor.Controllers
{
    public class PrefabPickerController : IPrefabPickerController
    {
        private int controlId = -1;
        private VegetationProfile profile;
        private int index = -1; // -1 表示新增

        private readonly Action<VegetationProfile, GameObject> onAddNew;
        private readonly Action<VegetationProfile, int, GameObject> onAssignExisting;

        public PrefabPickerController(
            Action<VegetationProfile, GameObject> onAddNew,
            Action<VegetationProfile, int, GameObject> onAssignExisting)
        {
            this.onAddNew = onAddNew ?? throw new ArgumentNullException(nameof(onAddNew));
            this.onAssignExisting = onAssignExisting ?? throw new ArgumentNullException(nameof(onAssignExisting));
        }

        public void OpenForItem(VegetationProfile profile, int index)
        {
            if (profile == null) return; // 提前返回
            if (index < 0 || index >= profile.Items.Count) return; // 提前返回
            this.profile = profile;
            this.index = index;
            controlId = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.ShowObjectPicker<GameObject>(null, false, "t:Prefab", controlId);
        }

        public void OpenForNew(VegetationProfile profile)
        {
            if (profile == null) return; // 提前返回
            this.profile = profile;
            index = -1;
            controlId = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.ShowObjectPicker<GameObject>(null, false, "t:Prefab", controlId);
        }

        public void HandleObjectPickerClosed()
        {
            if (controlId < 0) return; // 未打开选择器
            if (controlId != EditorGUIUtility.GetObjectPickerControlID()) return; // 非本控制器

            var selectedObj = EditorGUIUtility.GetObjectPickerObject() as GameObject;
            if (selectedObj == null || profile == null)
            {
                Reset();
                return;
            }

            if (index == -1)
            {
                onAddNew?.Invoke(profile, selectedObj);
            }
            else
            {
                onAssignExisting?.Invoke(profile, index, selectedObj);
            }

            Reset();
        }

        private void Reset()
        {
            controlId = -1;
            profile = null;
            index = -1;
        }
    }
}
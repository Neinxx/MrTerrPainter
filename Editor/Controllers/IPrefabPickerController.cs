using MrTerrainPainter.Runtime.Profiles;
using UnityEngine;
using UnityEditor;

namespace MrTerrainPainter.Editor.Controllers
{
    public interface IPrefabPickerController
    {
        void OpenForItem(VegetationProfile profile, int index);
        void OpenForNew(VegetationProfile profile);
        void HandleObjectPickerClosed();
    }
}
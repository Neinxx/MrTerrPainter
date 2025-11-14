using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;

namespace MrTerrainPainter.Editor.Controllers
{
    public class ProfileController
    {
        public void EnsureDataFolderExists(string path)
        {
            var normalized = path.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(normalized)) return;
            var segments = normalized.Split('/');
            if (segments.Length < 2) return;
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = segments[i];
                string combined = current + "/" + next;
                if (!AssetDatabase.IsValidFolder(combined))
                {
                    AssetDatabase.CreateFolder(current, next);
                }
                current = combined;
            }
            AssetDatabase.Refresh();
        }

        public VegetationProfile CreateNewVegetationProfileAsset(string folderPath)
        {
            EnsureDataFolderExists(folderPath);
            var profile = UnityEngine.ScriptableObject.CreateInstance<VegetationProfile>();
            profile.name = "VegetationProfile";
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/VegetationProfile.asset");
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return profile;
        }

        public void DeleteVegetationProfileAsset(VegetationProfile profile)
        {
            if (profile == null) return;
            var path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using prefabType = MrTerrainPainter.Runtime.Profiles;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;

namespace MrTerrainPainter.Editor.Controllers
{
    // 预制体条目相关业务控制器：新增、赋值、删除、清理
    // 仅处理数据与刷新，不感知 UI 结构
    public class PrefabAssignmentController
    {
        private readonly IRefreshController refreshController;
        private readonly Func<VegetationProfile> getCurrentProfile;
        private readonly Func<int> getSelectedItemIndex;
        private readonly Action<int> setSelectedItemIndex;
        private readonly HashSet<int> selectedThumbIndices;

        public PrefabAssignmentController(
            IRefreshController refreshController,
            Func<VegetationProfile> getCurrentProfile,
            Func<int> getSelectedItemIndex,
            Action<int> setSelectedItemIndex,
            HashSet<int> selectedThumbIndices)
        {
            this.refreshController = refreshController ?? throw new ArgumentNullException(nameof(refreshController));
            this.getCurrentProfile = getCurrentProfile ?? throw new ArgumentNullException(nameof(getCurrentProfile));
            this.getSelectedItemIndex = getSelectedItemIndex ?? (() => -1);
            this.setSelectedItemIndex = setSelectedItemIndex ?? (_ => { });
            this.selectedThumbIndices = selectedThumbIndices ?? new HashSet<int>();
        }

        public void AddPrefabAsNewItem(VegetationProfile profile, GameObject prefab)
        {
            if (profile == null || prefab == null) return; // 提前返回
            var item = new VegetationItem
            {
                // 初始不赋 prefab，通过 AssignPrefabToItem 统一处理赋值与刷新
                weight = 1f,
                uniformScaleRange = new Vector2(1f, 1f),
                yRotationRange = new Vector2(0f, 30f),
                heightRange = new Vector2(0f, 1000f),
                slopeRange = new Vector2(0f, 90f),
                alignToTerrainNormal = true
            };
            profile.AddItem(item);
            var newIndex = Mathf.Max(0, profile.Items.Count - 1);
            setSelectedItemIndex(newIndex);
            AssignPrefabToItem(profile, newIndex, prefab);
        }

        public void AssignPrefabToItem(VegetationProfile profile, int index, GameObject prefab)
        {
            if (profile == null || prefab == null) return; // 提前返回
            if (index < 0 || index >= profile.Items.Count) return; // 边界检查
            var target = profile.Items[index];
            if (target.prefab == prefab)
            {
                EditorUtility.SetDirty(profile);
                refreshController.RefreshAllUI();
                return;
            }
            target.prefab = prefab;
            EditorUtility.SetDirty(profile);
            refreshController.RefreshAllUI();
        }

        public void CreateNewVegetationItem()
        {
            var profile = getCurrentProfile?.Invoke();
            if (profile == null) return; // 提前返回
            var item = new VegetationItem
            {
                prefab = null,
                weight = 1f,
                uniformScaleRange = new Vector2(1f, 1f),
                yRotationRange = new Vector2(0f, 30f),
                heightRange = new Vector2(0f, 1000f),
                slopeRange = new Vector2(0f, 90f),
                alignToTerrainNormal = true
            };
            profile.AddItem(item);
            setSelectedItemIndex(Mathf.Max(0, profile.Items.Count - 1));
            EditorUtility.SetDirty(profile);
            refreshController.RefreshAllUI();
        }

        public void RemoveItemAt(int index)
        {
            var profile = getCurrentProfile?.Invoke();
            if (profile == null) return;
            if (index < 0 || index >= profile.Items.Count) return;
            Undo.RecordObject(profile, "Delete Vegetation Item");
            profile.RemoveAt(index);
            // 防越界并更新属性面板锚点
            var sel = getSelectedItemIndex();
            if (profile.Items.Count == 0)
                setSelectedItemIndex(-1);
            else if (sel >= profile.Items.Count)
                setSelectedItemIndex(profile.Items.Count - 1);
            // 清理多选状态中的已删除索引
            selectedThumbIndices.Remove(index);
            EditorUtility.SetDirty(profile);
            refreshController.RefreshAllUI();
        }

        public void RemoveItemAtFromProfile(VegetationProfile profile, int index)
        {
            if (profile == null) return;
            if (index < 0 || index >= profile.Items.Count) return;
            Undo.RecordObject(profile, "Delete Vegetation Item");
            profile.RemoveAt(index);
            EditorUtility.SetDirty(profile);
            // 若删除的是当前 Profile 项，修正选中状态
            if (profile == getCurrentProfile?.Invoke())
            {
                selectedThumbIndices.Remove(index);
                var sel = getSelectedItemIndex();
                setSelectedItemIndex(Mathf.Clamp(sel, 0, Mathf.Max(0, profile.Items.Count - 1)));
            }
            refreshController.RefreshAllUI();
        }

        public void RemoveItemsAtFromProfile(VegetationProfile profile, IEnumerable<int> indices)
        {
            if (profile == null || indices == null) return;
            var toRemove = indices
                .Where(i => i >= 0 && i < profile.Items.Count)
                .Distinct()
                .OrderByDescending(i => i)
                .ToList();
            if (toRemove.Count == 0) return;
            Undo.RecordObject(profile, "Delete Vegetation Items");
            foreach (var i in toRemove)
            {
                profile.RemoveAt(i);
            }
            EditorUtility.SetDirty(profile);
            if (profile == getCurrentProfile?.Invoke())
            {
                selectedThumbIndices.Clear();
                var sel = getSelectedItemIndex();
                setSelectedItemIndex(Mathf.Clamp(sel, 0, Mathf.Max(0, profile.Items.Count - 1)));
            }
            refreshController.RefreshAllUI();
        }

        public void CleanNullPrefabItems(VegetationProfile profile)
        {
            if (profile == null) return; // 提前返回
            for (int i = profile.Items.Count - 1; i >= 0; i--)
            {
                var it = profile.Items[i];
                if (it == null || it.prefab == null)
                {
                    profile.RemoveAt(i);
                }
            }
            EditorUtility.SetDirty(profile);
            refreshController.RefreshAllUI();
        }

        public void AddPrefabsToProfile(GameObject[] prefabs)
        {
            var profile = getCurrentProfile?.Invoke();
            if (profile == null || prefabs == null || prefabs.Length == 0) return;
            foreach (var p in prefabs)
            {
                if (p == null) continue;
                var item = new VegetationItem
                {
                    prefab = p,
                    weight = 1f,
                    uniformScaleRange = new Vector2(1f, 1f),
                    yRotationRange = new Vector2(0f, 30f),
                    heightRange = new Vector2(0f, 1000f),
                    slopeRange = new Vector2(0f, 90f),
                    alignToTerrainNormal = true
                };
                profile.AddItem(item);
            }
            EditorUtility.SetDirty(profile);
            refreshController.RefreshAllUI();
        }

        // 修改条目的 PrefabType，并统一刷新
        public void SetItemType(VegetationProfile profile, int index, prefabType.PrefabType type)
        {
            if (profile == null) return;
            if (index < 0 || index >= profile.Items.Count) return;
            var item = profile.Items[index];
            if (item == null) return;
            if (item.prefabType == type)
            {
                EditorUtility.SetDirty(profile);
                refreshController.RefreshAllUI();
                return;
            }
            item.prefabType = type;
            EditorUtility.SetDirty(profile);
            refreshController.RefreshAllUI();
        }
    }
}
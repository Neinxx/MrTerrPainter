using System.Collections.Generic;
using UnityEngine;

namespace MrTerrainPainter.Runtime.Profiles
{
    [CreateAssetMenu(fileName = "VegetationProfile", menuName = "MrTerrainPainter/Vegetation Profile", order = 1)]
    public class VegetationProfile : ScriptableObject
    {
        [SerializeField] private List<VegetationItem> items = new List<VegetationItem>();
        [SerializeField, Tooltip("是否已完成首次初始化（防止重复添加默认条目）")] private bool initialized = false;

        [Header("全局控制")]
        [Tooltip("随机种子，保持结果可复现。")]
        public int randomSeed = 12345;

        [Tooltip("密度（单位面积基础密度），按区域可叠加缩放。")]
        [Range(0f, 10f)]
        public float baseDensity = 1f;

        [Tooltip("最小间距约束（米），避免过度密集。")]
        [Range(0f, 10f)]
        public float minSpacing = 1.5f;

        public IReadOnlyList<VegetationItem> Items => items;

        public bool IsEmpty()
        {
            if (items == null || items.Count == 0) return true; // 提前返回
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it != null && it.IsValid()) return false;
            }
            return true;
        }

        public void AddItem(VegetationItem item)
        {
            if (item == null) return; // 提前返回
            items ??= new List<VegetationItem>();
            items.Add(item);
        }

        public void RemoveAt(int index)
        {
            if (items == null) return; // 提前返回
            if (index < 0 || index >= items.Count) return; // 提前返回
            items.RemoveAt(index);
        }

        // —— 生命周期：首次初始化默认条目 ——
        private void OnEnable()
        {
            EnsureInitialized();
        }

        private void OnValidate()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            // 仅在首次初始化时添加一个默认 Item（空槽），避免重复添加
            if (initialized) return; // 提前返回

            // 确保列表存在
            if (items == null) items = new List<VegetationItem>();

            // 若为空则添加一个默认占位条目
            if (items.Count == 0)
            {
                var defaultItem = new VegetationItem
                {
                    prefab = null,
                    weight = 1f,
                    uniformScaleRange = new Vector2(1f, 1f),
                    yRotationRange = new Vector2(0f, 360f),
                    heightRange = new Vector2(0f, 1000f),
                    slopeRange = new Vector2(0f, 90f),
                    baseDensity = 1f,
                    minSpacing = 1.5f,
                    alignToTerrainNormal = true
                };
                items.Add(defaultItem);
            }

            initialized = true;
        }
    }
}
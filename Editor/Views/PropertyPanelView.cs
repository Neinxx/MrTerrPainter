using System;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    // 属性面板视图：负责 Prefab 选择、权重、范围与密度/间距等控件的绑定与刷新
    public class PropertyPanelView
    {
        public struct PropertyPanelCallbacks
        {
            public Func<VegetationItem> GetSelectedItem;
            public Func<VegetationProfile> GetCurrentProfile;
            public Func<int> GetSelectedItemIndex;
            public Action<int> RemoveItemAt;
            public Action<VegetationProfile, int, GameObject> AssignPrefabToItem;
            public Action RefreshPreviewListUI;
            public Action RefreshVegetationListUI;
            public Action UpdatePropertyPanelFromSelectedItem;
            public Action MarkCurrentProfileDirty;
        }

        private readonly VisualElement root;

        private ObjectField uiSelectPrefab;
        private Slider uiWeigth;
        private MinMaxSlider uiSceleRange;
        private MinMaxSlider uiYrotationRange;
        private MinMaxSlider uiHeigthRange;
        private MinMaxSlider uiSlopeRange;
        private Slider uiBaseDensity;
        private Slider uiMinimumSpacing;

        private PropertyPanelCallbacks callbacks;

        public PropertyPanelView(VisualElement queryRoot)
        {
            root = queryRoot;
        }

        public void Bind(PropertyPanelCallbacks cb)
        {
            callbacks = cb;

            if (root == null) return;

            uiSelectPrefab = root.Q<ObjectField>("SelectPrefab");
            uiWeigth = root.Q<Slider>("Weigth");
            uiSceleRange = root.Q<MinMaxSlider>("SceleRange");
            uiYrotationRange = root.Q<MinMaxSlider>("YrotationRange");
            uiHeigthRange = root.Q<MinMaxSlider>("HeigthRange");
            uiSlopeRange = root.Q<MinMaxSlider>("SlopeRange");
            uiBaseDensity = root.Q<Slider>("BaseDensity");
            uiMinimumSpacing = root.Q<Slider>("MinimumSpacing");

            // 上下限初始化（遵循提前返回与单一职责）
            if (uiWeigth != null) { uiWeigth.lowValue = 0f; uiWeigth.highValue = 1f; }
            if (uiSceleRange != null) { uiSceleRange.lowLimit = 0f; uiSceleRange.highLimit = 5f; }
            if (uiHeigthRange != null) { uiHeigthRange.lowLimit = 0f; uiHeigthRange.highLimit = 1000f; }
            if (uiSlopeRange != null) { uiSlopeRange.lowLimit = 0f; uiSlopeRange.highLimit = 90f; }
            if (uiBaseDensity != null) { uiBaseDensity.lowValue = 0f; uiBaseDensity.highValue = 10f; }
            if (uiMinimumSpacing != null) { uiMinimumSpacing.lowValue = 0f; uiMinimumSpacing.highValue = 10f; }

            // 类型约束与事件绑定
            if (uiSelectPrefab != null)
            {
                uiSelectPrefab.objectType = typeof(GameObject);
                uiSelectPrefab.allowSceneObjects = false;
                uiSelectPrefab.RegisterValueChangedCallback(evt =>
                {
                    var item = callbacks.GetSelectedItem?.Invoke();
                    if (item == null) return; // 提前返回

                    var newGo = evt.newValue as GameObject;
                    if (newGo == null)
                    {
                        var index = callbacks.GetSelectedItemIndex != null ? callbacks.GetSelectedItemIndex() : -1;
                        callbacks.RemoveItemAt?.Invoke(index);
                        callbacks.RefreshPreviewListUI?.Invoke();
                        callbacks.UpdatePropertyPanelFromSelectedItem?.Invoke();
                        return; // 提前返回
                    }
                    var profile = callbacks.GetCurrentProfile?.Invoke();
                    var i = callbacks.GetSelectedItemIndex != null ? callbacks.GetSelectedItemIndex() : -1;
                    callbacks.AssignPrefabToItem?.Invoke(profile, i, newGo);
                });
            }

            uiWeigth?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0f, 1f);
                item.weight = v;
                uiWeigth.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiSceleRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                v.x = Mathf.Max(0f, v.x);
                v.y = Mathf.Max(v.x, v.y);
                item.uniformScaleRange = v;
                uiSceleRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiYrotationRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                float maxRot = uiYrotationRange != null ? uiYrotationRange.highLimit : 30f;
                v.x = Mathf.Clamp(v.x, 0f, maxRot);
                v.y = Mathf.Clamp(v.y, 0f, maxRot);
                if (v.y < v.x) v.y = v.x;
                item.yRotationRange = v;
                uiYrotationRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiHeigthRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                if (v.y < v.x) v.y = v.x;
                item.heightRange = v;
                uiHeigthRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiSlopeRange?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = evt.newValue;
                v.x = Mathf.Clamp(v.x, 0f, 90f);
                v.y = Mathf.Clamp(v.y, 0f, 90f);
                if (v.y < v.x) v.y = v.x;
                item.slopeRange = v;
                uiSlopeRange.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiBaseDensity?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0f, 10f);
                item.baseDensity = v;
                uiBaseDensity.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });

            uiMinimumSpacing?.RegisterValueChangedCallback(evt =>
            {
                var item = callbacks.GetSelectedItem?.Invoke();
                if (item == null) return;
                var v = Mathf.Clamp(evt.newValue, 0f, 10f);
                item.minSpacing = v;
                uiMinimumSpacing.SetValueWithoutNotify(v);
                callbacks.MarkCurrentProfileDirty?.Invoke();
            });
        }

        // 从选中条目刷新属性面板显示（保持与窗口逻辑一致）
        public void UpdateFromSelectedItem()
        {
            var item = callbacks.GetSelectedItem?.Invoke();
            if (item == null)
            {
                uiSelectPrefab?.SetValueWithoutNotify(null);
                uiWeigth?.SetValueWithoutNotify(0f);
                uiSceleRange?.SetValueWithoutNotify(Vector2.one);
                var maxRot = uiYrotationRange != null ? uiYrotationRange.highLimit : 30f;
                uiYrotationRange?.SetValueWithoutNotify(new Vector2(0f, maxRot));
                uiHeigthRange?.SetValueWithoutNotify(new Vector2(0f, 1000f));
                uiSlopeRange?.SetValueWithoutNotify(new Vector2(0f, 90f));
                uiBaseDensity?.SetValueWithoutNotify(0f);
                uiMinimumSpacing?.SetValueWithoutNotify(0f);
                return;
            }

            uiSelectPrefab?.SetValueWithoutNotify(item.prefab);
            uiWeigth?.SetValueWithoutNotify(item.weight);
            uiSceleRange?.SetValueWithoutNotify(item.uniformScaleRange);
            uiYrotationRange?.SetValueWithoutNotify(item.yRotationRange);
            uiHeigthRange?.SetValueWithoutNotify(item.heightRange);
            uiSlopeRange?.SetValueWithoutNotify(item.slopeRange);
            uiBaseDensity?.SetValueWithoutNotify(item.baseDensity);
            uiMinimumSpacing?.SetValueWithoutNotify(item.minSpacing);

            // —— 动态匹配上下限 ——
            if (uiSceleRange != null)
            {
                uiSceleRange.lowLimit = Mathf.Min(0f, item.uniformScaleRange.x);
                uiSceleRange.highLimit = Mathf.Max(5f, item.uniformScaleRange.y);
            }
            if (uiHeigthRange != null)
            {
                uiHeigthRange.lowLimit = Mathf.Min(0f, item.heightRange.x);
                uiHeigthRange.highLimit = Mathf.Max(1000f, item.heightRange.y);
            }
            if (uiSlopeRange != null)
            {
                uiSlopeRange.lowLimit = Mathf.Min(0f, item.slopeRange.x);
                uiSlopeRange.highLimit = Mathf.Max(90f, item.slopeRange.y);
            }
        }
    }
}
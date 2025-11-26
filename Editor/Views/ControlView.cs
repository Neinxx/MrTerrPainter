using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    public struct ControlViewCallbacks
    {
        public Action CreateNewVegetationProfileAsset;
        public Action ReloadAvailableProfiles;
        public Action RefreshAllUI;
        public Action SetListSelectionToCurrentProfile;
        public Action<VegetationProfile> DeleteVegetationProfileAsset;
        public Action<VegetationProfile> SetCurrentProfile;
        public Action ResetSelectionForProfileChange;
        public Func<VegetationProfile> GetCurrentProfile;
        public Action<float> OnListContentWidthMeasured;
    }

    public class ControlView
    {
        private readonly VisualElement root;
        private readonly VisualTreeAsset rowTemplate;
        private readonly Dictionary<VegetationProfile, VisualElement> _rowMap = new Dictionary<VegetationProfile, VisualElement>();
        private bool _subscribed;

        // 缓存初始化参数，以便 Refresh() 使用
        private List<VegetationProfile> _cachedAvailableProfiles;
        private List<VegetationProfile> _cachedExtraProfiles;
        private ControlViewCallbacks _cachedCb;
        private Func<VegetationProfile, VisualElement> _cachedMakeDraggableArea;
        private Func<VegetationProfile, VegetationItem, int, VisualElement> _cachedMakeThumb;
        private Func<int, int> _cachedThumbRows;

        public ListView ListView { get; private set; } // 保持兼容，虽然为空

        public ControlView(VisualElement contralRoot, VisualTreeAsset vegetationProfileRowTemplate)
        {
            root = contralRoot;
            rowTemplate = vegetationProfileRowTemplate;
        }

        // === 公开刷新接口 (修复的核心) ===
        public void Refresh()
        {
            if (_cachedAvailableProfiles != null)
            {
                SetupVegetationProfileList(
                    _cachedAvailableProfiles,
                    _cachedExtraProfiles,
                    _cachedCb,
                    _cachedMakeDraggableArea,
                    _cachedMakeThumb,
                    _cachedThumbRows
                );
            }
        }

        public void SetupVegetationProfileList(
            List<VegetationProfile> availableProfiles,
            List<VegetationProfile> extraProfiles,
            ControlViewCallbacks cb,
            Func<VegetationProfile, VisualElement> makeDraggableArea,
            Func<VegetationProfile, VegetationItem, int, VisualElement> makeThumb,
            Func<int, int> thumbRows)
        {
            // 1. 缓存参数
            _cachedAvailableProfiles = availableProfiles;
            _cachedExtraProfiles = extraProfiles;
            _cachedCb = cb;
            _cachedMakeDraggableArea = makeDraggableArea;
            _cachedMakeThumb = makeThumb;
            _cachedThumbRows = thumbRows;

            var host = root?.Q<VisualElement>("VegetationProfileList")
                       ?? root?.Q<VisualElement>("VegetationProfile");
            if (host == null) return;

            cb.ReloadAvailableProfiles?.Invoke();

            // 2. 获取或创建 ScrollView
            var sv = root.Q<ScrollView>("VegetationProfileListSV");
            if (sv == null)
            {
                sv = new ScrollView();
                sv.name = "VegetationProfileListSV";
                sv.AddToClassList("mt-veg-list");
                host.Add(sv);
            }

            // 3. 清空并重建列表 (这里是关键：清空 -> 重绘 = 实时刷新)
            sv.Clear();
            _rowMap.Clear();

            if (availableProfiles != null)
            {
                for (int i = 0; i < availableProfiles.Count; i++)
                {
                    var row = MakeRow();
                    var profile = availableProfiles[i];
                    BindRow(row, profile, availableProfiles, extraProfiles, cb, makeDraggableArea, makeThumb, thumbRows);
                    sv.Add(row);
                    if (profile != null && !_rowMap.ContainsKey(profile)) _rowMap[profile] = row;
                }
            }

            // 显式置空，防止外部错误调用
            ListView = null;

            var current = cb.GetCurrentProfile != null ? cb.GetCurrentProfile() : null;
            ApplySelection(current);

            if (!_subscribed)
            {
                _subscribed = true;
                MrTerrainPainter.Editor.Tools.MTPBrushContext.ProfileChanged += ApplySelection;
            }

            // 绑定新建按钮
            var btnCreateProfile = root.Q<Button>("CreateCreateButton");
            if (btnCreateProfile != null)
            {
                btnCreateProfile.text = string.IsNullOrEmpty(btnCreateProfile.text) ? "新建Profile" : btnCreateProfile.text;
                btnCreateProfile.AddToClassList("mt-buttonG");

                if (btnCreateProfile.userData is Action old) btnCreateProfile.clicked -= old;
                void handler()
                {
                    cb.CreateNewVegetationProfileAsset?.Invoke();
                    cb.ReloadAvailableProfiles?.Invoke();
                    cb.SetListSelectionToCurrentProfile?.Invoke();
                    cb.RefreshAllUI?.Invoke();
                }
                btnCreateProfile.userData = (Action)handler;
                btnCreateProfile.clicked += (Action)handler;
            }
        }

        private VisualElement MakeRow()
        {
            if (rowTemplate != null)
            {
                var ve = rowTemplate.Instantiate();
                ve.AddToClassList("profile-row");
                return ve;
            }
            // 后备创建代码保持不变...
            return new Label("Missing Template");
        }

        private void BindRow(
            VisualElement row,
            VegetationProfile profile,
            List<VegetationProfile> availableProfiles,
            List<VegetationProfile> extraProfiles,
            ControlViewCallbacks cb,
            Func<VegetationProfile, VisualElement> makeDraggableArea,
            Func<VegetationProfile, VegetationItem, int, VisualElement> makeThumb,
            Func<int, int> thumbRows)
        {
            var nameLabel = row.Q<Label>("Name");
            var profileFld = row.Q<ObjectField>("ProfileField");
            var delBtn = row.Q<Button>("DeleteProfileInline");
            var thumbs = row.Q<VisualElement>("Thumbs");
            var selectToggle = row.Q<Toggle>("Select") ?? row.Q<Toggle>();

            row.AddToClassList("profile-row");
            var current = cb.GetCurrentProfile != null ? cb.GetCurrentProfile() : null;
            if (profile != null && profile == current) row.AddToClassList("profile-row--selected");
            else row.RemoveFromClassList("profile-row--selected");

            if (delBtn == null || thumbs == null) return;

            // 点击缩略图区域切换 Profile
            thumbs.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (profile == null) return;
                // 使用 delayCall 确保在事件传播后执行刷新
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    cb.SetCurrentProfile?.Invoke(profile);
                    cb.ResetSelectionForProfileChange?.Invoke();
                    cb.RefreshAllUI?.Invoke();
                };
                // 不要 StopPropagation，否则可能阻挡内部缩略图的点击
            });

            if (nameLabel != null) nameLabel.text = profile != null ? profile.name : "(空)";

            // 删除逻辑
            void newDelHandler()
            {
                if (profile == null) return;
                extraProfiles.Remove(profile);
                MrTerrainPainter.Editor.Tools.MTPBrushContext.RemoveExtra(profile);
                cb.DeleteVegetationProfileAsset?.Invoke(profile);
                cb.ReloadAvailableProfiles?.Invoke();
                cb.RefreshAllUI?.Invoke();
            }
            MrTerrainPainter.Editor.Utils.SubscriptionGuard.ResetClick(delBtn, newDelHandler);

            if (profileFld != null)
            {
                profileFld.objectType = typeof(VegetationProfile);
                profileFld.allowSceneObjects = false;
                profileFld.SetValueWithoutNotify(profile);
                EventCallback<ChangeEvent<UnityEngine.Object>> newProfileCb = evt =>
                {
                    if (evt.newValue is not VegetationProfile p) return;
                    cb.SetCurrentProfile?.Invoke(p);
                    cb.ResetSelectionForProfileChange?.Invoke();
                    cb.RefreshAllUI?.Invoke();
                    if (nameLabel != null) nameLabel.text = p.name;
                };
                MrTerrainPainter.Editor.Utils.SubscriptionGuard.ResetObjectField(profileFld, newProfileCb);
            }

            if (selectToggle != null && profile != null)
            {
                var currentProfile = cb.GetCurrentProfile != null ? cb.GetCurrentProfile() : null;
                bool isExtra = extraProfiles.Contains(profile);
                selectToggle.SetValueWithoutNotify(isExtra);
                if (isExtra) row.AddToClassList("profile-row--checked");
                else row.RemoveFromClassList("profile-row--checked");

                EventCallback<ChangeEvent<bool>> newSelectCb = evt =>
                {
                    bool on = evt.newValue;
                    if (on)
                    {
                        if (profile != currentProfile && !extraProfiles.Contains(profile)) extraProfiles.Add(profile);
                        MrTerrainPainter.Editor.Tools.MTPBrushContext.AddExtra(profile);
                        row.AddToClassList("profile-row--checked");
                    }
                    else
                    {
                        extraProfiles.Remove(profile);
                        MrTerrainPainter.Editor.Tools.MTPBrushContext.RemoveExtra(profile);
                        row.RemoveFromClassList("profile-row--checked");
                    }
                };
                MrTerrainPainter.Editor.Utils.SubscriptionGuard.ResetToggle(selectToggle, newSelectCb);
            }

            thumbs.pickingMode = PickingMode.Position;
            thumbs.style.flexDirection = FlexDirection.Row;
            thumbs.style.flexWrap = Wrap.Wrap;
            thumbs.style.justifyContent = Justify.FlexStart;

            if (profile != null && profile.Items != null)
            {
                if (makeDraggableArea != null)
                {
                    var addArea = makeDraggableArea(profile);
                    if (addArea != null) thumbs.Add(addArea);
                }

                var count = Math.Min(9, profile.Items.Count);
                for (int i = 0; i < count; i++)
                {
                    var item = profile.Items[i];
                    var thumb = makeThumb?.Invoke(profile, item, i);
                    if (thumb != null) thumbs.Add(thumb);
                }

                // 移除占位符逻辑以简化，或者你可以保留
            }
        }

        private void ApplySelection(VegetationProfile current)
        {
            foreach (var kv in _rowMap)
            {
                if (kv.Key == current) kv.Value.AddToClassList("profile-row--selected");
                else kv.Value.RemoveFromClassList("profile-row--selected");
            }
        }
    }
}
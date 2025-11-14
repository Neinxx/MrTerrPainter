using System;
using System.Collections.Generic;
using System.Linq;
using MrTerrainPainter.Runtime.Profiles;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Views
{
    // 回调与状态接口：避免视图依赖窗口内部字段，实现单一职责
    public struct ContralViewCallbacks
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

    // 视图：负责 Contral 页的 VegetationProfile 列表构建与数据绑定
    public class ContralView
    {
        private readonly VisualElement root;
        private readonly VisualTreeAsset rowTemplate;

        public ListView ListView { get; private set; }

        private const float ThumbSize = 64;
        private const float ThumbGap = 8f;

        public ContralView(VisualElement contralRoot, VisualTreeAsset vegetationProfileRowTemplate)
        {
            root = contralRoot;
            rowTemplate = vegetationProfileRowTemplate;
        }

        // 主入口：构建 VegetationProfile 列表并完成绑定
        public void SetupVegetationProfileList(
            List<VegetationProfile> availableProfiles,
            List<VegetationProfile> extraProfiles,
            ContralViewCallbacks cb,
            Func<VegetationProfile, VisualElement> makeDraggableArea,
            Func<VegetationProfile, VegetationItem, int, VisualElement> makeThumb,
            Func<int, int> thumbRows)
        {
            var host = root?.Q<VisualElement>("VegetationProfileList")
                       ?? root?.Q<VisualElement>("VegetationProfile");
            if (host == null) return; // 提前返回：没有宿主容器

            host.Clear();
            cb.ReloadAvailableProfiles?.Invoke();

            var lv = new ListView
            {
                selectionType = SelectionType.None,
                reorderable = true,
                itemsSource = availableProfiles,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                style = { flexGrow = 1 }
            };

            lv.RegisterCallback<GeometryChangedEvent>(e =>
            {
                var w = lv.resolvedStyle.width;
                if (w > 0 && !float.IsNaN(w)) cb.OnListContentWidthMeasured?.Invoke(w);
            });

            lv.makeItem = () => MakeRow();
            lv.bindItem = (elem, i) =>
            {
                var profile = (i >= 0 && i < availableProfiles.Count) ? availableProfiles[i] : null;
                BindRow(elem, profile, availableProfiles, extraProfiles, cb, makeDraggableArea, makeThumb, thumbRows);
            };

            host.Add(lv);
            ListView = lv;

            // 使用 UXML 中的 CreateCreateButton 按钮
            var btnCreateProfile = root.Q<Button>("CreateCreateButton");
            if (btnCreateProfile != null)
            {
                btnCreateProfile.text = string.IsNullOrEmpty(btnCreateProfile.text) ? "新建Profile" : btnCreateProfile.text;
                btnCreateProfile.AddToClassList("mt-buttonG");
                // 防重复：清理旧的点击回调
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
            // 优先使用外部 UXML 行模板
            if (rowTemplate != null)
            {
                var ve = rowTemplate.Instantiate();
                ve.AddToClassList("profile-row");
                return ve;
            }
            // 后备：程序化创建（保证在 UXML 资源缺失时不阻塞功能）
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.minHeight = 112;
            row.style.flexShrink = 0;

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.marginBottom = 4;

            var nameLabel = new Label("Name") { name = "Name" };
            nameLabel.style.flexGrow = 1;
            nameLabel.style.marginLeft = 6;

            var profileFld = new ObjectField("ProfileField")
            {
                objectType = typeof(VegetationProfile),
                allowSceneObjects = false,
                name = "ProfileField"
            };
            profileFld.style.flexGrow = 1;
            profileFld.style.width = 220;

            var delBtn = new Button { name = "DeleteProfileInline", text = "删除Profile" };
            delBtn.style.marginLeft = 6;

            top.Add(nameLabel);
            top.Add(profileFld);
            top.Add(delBtn);

            var thumbs = new VisualElement { name = "Thumbs" };
            thumbs.style.flexGrow = 1;
            thumbs.style.marginLeft = 8;
            thumbs.style.marginBottom = 4;
            thumbs.style.flexWrap = Wrap.Wrap;

            row.Add(top);
            row.Add(thumbs);
            return row;
        }

        private void BindRow(
            VisualElement row,
            VegetationProfile profile,
            List<VegetationProfile> availableProfiles,
            List<VegetationProfile> extraProfiles,
            ContralViewCallbacks cb,
            Func<VegetationProfile, VisualElement> makeDraggableArea,
            Func<VegetationProfile, VegetationItem, int, VisualElement> makeThumb,
            Func<int, int> thumbRows)
        {
            var nameLabel = row.Q<Label>("Name");
            var profileFld = row.Q<ObjectField>("ProfileField");
            var delBtn = row.Q<Button>("DeleteProfileInline");
            var thumbs = row.Q<VisualElement>("Thumbs");
            var selectToggle = row.Q<Toggle>("Select") ?? row.Q<Toggle>();

            // 行选中样式
            row.AddToClassList("profile-row");
            var current = cb.GetCurrentProfile != null ? cb.GetCurrentProfile() : null;
            if (profile != null && profile == current) row.AddToClassList("profile-row--selected");
            else row.RemoveFromClassList("profile-row--selected");

            // 点击缩略图区域(Thumbs)切换当前 Profile（避免整行覆盖 ObjectField 的交互）

            // 健壮性：控件可能为空（模板或重绑定异常时）
            if (delBtn == null || thumbs == null)
            {
                return; // 提前返回，避免空引用崩溃
            }

            thumbs.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (profile == null) return;
                cb.SetCurrentProfile?.Invoke(profile);
                cb.ResetSelectionForProfileChange?.Invoke();
                cb.RefreshAllUI?.Invoke();
                evt.StopPropagation();
            });

            if (nameLabel != null)
            {
                nameLabel.text = profile != null ? profile.name : "(空)";
            }

            // 删除按钮
            if (delBtn.userData is Action oldDelHandler) delBtn.clicked -= oldDelHandler;
            void newDelHandler()
            {
                if (profile == null) return;
                extraProfiles.Remove(profile);
                cb.DeleteVegetationProfileAsset?.Invoke(profile);
                cb.ReloadAvailableProfiles?.Invoke();
                cb.RefreshAllUI?.Invoke();
            }
            delBtn.userData = (Action)newDelHandler;
            delBtn.clicked += (Action)newDelHandler;

            // Profile 字段变更
            if (profileFld != null)
            {
                profileFld.objectType = typeof(VegetationProfile);
                profileFld.allowSceneObjects = false;
                profileFld.SetValueWithoutNotify(profile);
                if (profileFld.userData is EventCallback<ChangeEvent<UnityEngine.Object>> oldProfileCb) profileFld.UnregisterCallback(oldProfileCb);
                EventCallback<ChangeEvent<UnityEngine.Object>> newProfileCb = evt =>
                {
                    if (evt.newValue is not VegetationProfile p) return;
                    cb.SetCurrentProfile?.Invoke(p);
                    cb.ResetSelectionForProfileChange?.Invoke();
                    cb.RefreshAllUI?.Invoke();
                    if (nameLabel != null) nameLabel.text = p.name;
                };
                profileFld.userData = newProfileCb;
                profileFld.RegisterCallback(newProfileCb);
            }

            // 复选：批量生成用
            if (selectToggle != null && profile != null)
            {
                var currentProfile = cb.GetCurrentProfile != null ? cb.GetCurrentProfile() : null;
                bool isExtra = extraProfiles.Contains(profile);
                selectToggle.SetValueWithoutNotify(isExtra);
                if (isExtra) row.AddToClassList("profile-row--checked");
                else row.RemoveFromClassList("profile-row--checked");

                var oldSelectCb = selectToggle.userData as EventCallback<ChangeEvent<bool>>;
                if (oldSelectCb != null) selectToggle.UnregisterCallback(oldSelectCb);
                EventCallback<ChangeEvent<bool>> newSelectCb = evt =>
                {
                    bool on = evt.newValue;
                    if (on)
                    {
                        if (profile != currentProfile && !extraProfiles.Contains(profile)) extraProfiles.Add(profile);
                        row.AddToClassList("profile-row--checked");
                    }
                    else
                    {
                        extraProfiles.Remove(profile);
                        row.RemoveFromClassList("profile-row--checked");
                    }
                };
                selectToggle.userData = newSelectCb;
                selectToggle.RegisterCallback(newSelectCb);
            }

            // 缩略图区域
            thumbs.Clear();
            thumbs.pickingMode = PickingMode.Position;
            thumbs.style.flexDirection = FlexDirection.Row;
            thumbs.style.flexWrap = Wrap.Wrap;
            thumbs.style.justifyContent = Justify.FlexStart;
            if (profile != null && profile.Items != null)
            {
                var addArea = makeDraggableArea?.Invoke(profile);
                if (addArea != null) thumbs.Add(addArea);
                foreach (var (item, idx) in profile.Items.Select((v, i) => (v, i)))
                {
                    var thumb = makeThumb?.Invoke(profile, item, idx);
                    if (thumb != null) thumbs.Add(thumb);
                }
                int rows = thumbRows?.Invoke(profile.Items.Count + 1) ?? 1;
                int header = 90;
                int rowHeight = header + rows * (int)(ThumbSize + ThumbGap);
                row.style.height = rowHeight;
            }
        }
    }
}

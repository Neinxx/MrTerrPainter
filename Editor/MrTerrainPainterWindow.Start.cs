using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using MrTerrainPainter.Runtime.Profiles;
using System.Collections.Generic; // 用于存储动画状态

namespace MrTerrainPainter.Editor
{
    public partial class MrTerrainPainterWindow
    {
        // 局部变量
        private Label _logoLabel;
        private VisualElement _mappingGuard;
        private double _lastClickTime;

        // 用于防止动画重叠播放
        private IVisualElementScheduledItem _currentAnimation;

        private void SetupStartPageEvents()
        {
            if (startRoot == null) return;

            _logoLabel = startRoot.Q<Label>("LOGO");
            if (_logoLabel != null)
            {
                // 初始化：确保 Transform 原点在中心，方便缩放旋转
                _logoLabel.style.transformOrigin = new TransformOrigin(50, 50);

                _logoLabel.RegisterCallback<PointerDownEvent>(evt =>
                {
                    // 简单的点击反馈（按下时缩小）
                    _logoLabel.style.scale = new Scale(new Vector2(0.9f, 0.9f));
                    _logoLabel.style.transitionDuration = new List<TimeValue> { new TimeValue(0.1f, TimeUnit.Second) };
                });

                _logoLabel.RegisterCallback<PointerUpEvent>(evt =>
                {
                    // 抬起时恢复，如果不触发双击逻辑
                    if (EditorApplication.timeSinceStartup - _lastClickTime >= 0.6f)
                    {
                        _logoLabel.style.scale = new Scale(Vector2.one);
                    }
                });

                _logoLabel.RegisterCallback<ClickEvent>(evt =>
                {
                    if (EditorApplication.timeSinceStartup - _lastClickTime < 0.6f)
                    {
                        // 双击：打开设置
                        OpenSettingsTab();
                        evt.StopPropagation();
                    }
                    else
                    {
                        // 单击：播放果冻动画
                        PlayJellyAnimation(_logoLabel);
                    }
                    _lastClickTime = EditorApplication.timeSinceStartup;
                });
            }

            // Navigation
            startRoot.Q<Button>("OpenControl")?.SetClickHandler(OpenPaintingSettings);
            startRoot.Q<Button>("OpenSettings")?.SetClickHandler(OpenSettingsTab);
            startRoot.Q<Button>("OpenSettingsGuard")?.SetClickHandler(OpenSettingsTab);

            // Scan Logic
            startRoot.Q<Button>("ScanTerrains")?.SetClickHandler(() =>
            {
                session.TerrainController.ScanSceneTerrains(session.TerrainListUIData, session.ScannedTerrainNames);
                OpenPaintingSettings();
            });

            // Quick Profile
            var quickProfile = startRoot.Q<ObjectField>("QuickProfile");
            if (quickProfile != null)
            {
                quickProfile.objectType = typeof(VegetationProfile);
                quickProfile.SetValueWithoutNotify(session.CurrentProfile);
                quickProfile.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue is VegetationProfile p) SetCurrentProfilePublic(p);
                });
            }

            _mappingGuard = startRoot.Q<VisualElement>("MappingGuard");
            CheckMappingStatus();
        }

        private void CheckMappingStatus()
        {
            if (_mappingGuard == null) return;
            bool valid = config?.mappingEntries?.Any(e => e?.type == MrTerrainPainter.Runtime.Profiles.PrefabType.Plant && e.node != null) ?? false;
            _mappingGuard.style.display = valid ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void CelebrateMappingCompleted()
        {
            if (_mappingGuard != null) _mappingGuard.style.display = DisplayStyle.None;

            if (_logoLabel == null) return;

            string originalText = _logoLabel.text;

            // 播放“成功”动画：膨胀 + 颜色变化 + 震动
            PlaySuccessPopAnimation(_logoLabel, () =>
            {
                // 动画结束回调：恢复文本
                _logoLabel.text = originalText;
                // 恢复颜色 (这里假设原本颜色由 USS 控制，清除内联样式即可)
                _logoLabel.style.color = StyleKeyword.Null;
            });
        }

        /// <summary>
        /// 播放果冻效果 (Squash & Stretch)
        /// </summary>
        private void PlayJellyAnimation(VisualElement target)
        {
            // 停止之前的动画
            _currentAnimation?.Pause();

            // 设置基础过渡属性
            target.style.transitionProperty = new List<StylePropertyName> {
                new StylePropertyName("scale"),
                new StylePropertyName("rotate")
            };
            target.style.transitionDuration = new List<TimeValue> { new TimeValue(0.15f, TimeUnit.Second) };
            target.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };

            // 动画序列 (时间轴)
            // 0ms: 初始状态
            // 1. 压扁 (Squash)
            target.style.scale = new Scale(new Vector2(1.25f, 0.75f));

            _currentAnimation = target.schedule.Execute(() =>
            {
                // 2. 拉伸 (Stretch)
                target.style.scale = new Scale(new Vector2(0.85f, 1.15f));
                target.style.rotate = new Rotate(new Angle(3f, AngleUnit.Degree)); // 微微倾斜

                target.schedule.Execute(() =>
                {
                    // 3. 轻微压扁 (Recoil)
                    target.style.scale = new Scale(new Vector2(1.05f, 0.95f));
                    target.style.rotate = new Rotate(new Angle(-1f, AngleUnit.Degree));

                    target.schedule.Execute(() =>
                    {
                        // 4. 恢复 (Reset)
                        target.style.scale = new Scale(Vector2.one);
                        target.style.rotate = new Rotate(new Angle(0));
                    }).StartingIn(120);
                }).StartingIn(120);
            }).StartingIn(100); // 稍微延迟一点，给第一帧留出渲染时间
        }

        /// <summary>
        /// 播放成功时的 冲击波效果 (Pop & Color)
        /// </summary>
        private void PlaySuccessPopAnimation(VisualElement target, Action onComplete)
        {
            _currentAnimation?.Pause();

            // 更改文本
            if (target is Label lbl) lbl.text = "COMPLETE!";

            // 设置颜色 (亮绿色)
            target.style.color = new StyleColor(new Color(0.3f, 0.9f, 0.4f));

            // 设置弹簧效果的过渡
            target.style.transitionProperty = new List<StylePropertyName> {
                new StylePropertyName("scale"),
                new StylePropertyName("color")
            };
            // 使用 BackOut 缓动模拟弹簧过冲效果
            target.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutBack) };
            target.style.transitionDuration = new List<TimeValue> { new TimeValue(0.4f, TimeUnit.Second) };

            // 1. 瞬间放大 (Pop)
            target.style.scale = new Scale(new Vector2(1.4f, 1.4f));

            // 2. 400ms 后恢复正常大小
            _currentAnimation = target.schedule.Execute(() =>
            {
                target.style.scale = new Scale(Vector2.one);

                // 再过 600ms (总计 1s) 结束动画
                target.schedule.Execute(() =>
                {
                    onComplete?.Invoke();
                }).StartingIn(600);

            }).StartingIn(400);
        }
    }

    internal static class ButtonExtensions
    {
        public static void SetClickHandler(this Button b, Action a) { if (b != null) b.clicked += a; }
    }
}
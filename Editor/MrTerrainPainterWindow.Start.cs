using System;
using System.Linq; // 用于简化集合查询
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using MrTerrainPainter.Runtime.Profiles;
using MrTerrainPainter.Editor.Tools; // 引用 Profile 命名空间

namespace MrTerrainPainter.Editor
{
    public partial class MrTerrainPainterWindow
    {
        // 常量定义
        private const string LogoElementName = "LOGO";
        private const string MappingGuardName = "MappingGuard";
        private const string AnimClassPulse1 = "mt-logo--pulse1";
        private const string AnimClassPulse2 = "mt-logo--pulse2";
        private const string AnimClassPulse3 = "mt-logo--pulse3";
        private const string AnimClassComplete = "mt-logo--complete";
        private const float DoubleClickInterval = 0.6f;

        // UI 缓存
        private Label _logoElement;
        private VisualElement _mappingGuard;

        // 双击检测状态
        private int _clickCount;
        private double _lastClickTime;

        /// <summary>
        /// 注册 Start 页面的 UI 事件 (入口方法)
        /// </summary>
        private void SetupStartPageEvents()
        {
            if (startRoot == null) return;

            // 1. 初始化各个模块
            SetupLogoLogic();
            SetupNavigationButtons();
            SetupScanLogic();
            SetupQuickProfileLogic();
            SetupMappingGuardLogic();
        }

        #region Setup Modules (初始化分块)

        private void SetupLogoLogic()
        {
            _logoElement = startRoot.Q<Label>(LogoElementName);
            if (_logoElement == null) return;

            // 重置点击状态
            _clickCount = 0;
            _lastClickTime = 0;

            _logoElement.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (CheckDoubleClick())
                {
                    OpenSettingsTab();
                    evt.StopPropagation();
                }
                PlayClickAnimation(_logoElement);
            });
        }

        private void SetupNavigationButtons()
        {
            // 使用扩展方法或简单的一行绑定，保持代码整洁
            BindClick("OpenControl", OpenPaintingSettings);
            BindClick("OpenSettings", OpenSettingsTab);
            BindClick("OpenSettingsGuard", OpenSettingsTab); // Guard 内部的按钮
        }

        private void SetupScanLogic()
        {
            var btnScan = startRoot.Q<Button>("ScanTerrains");
            if (btnScan != null)
            {
                btnScan.clicked += () =>
                {
                    terrainController?.ScanSceneTerrains(terrainListUIData, scannedTerrainNames);
                    OpenPaintingSettings();
                    RefreshTerrainListUI();
                    MrTerrainPainter.Editor.Tools.MTPBrushContext.SetSelectedTerrains(selectedTerrains);
                };
            }
        }

        private void SetupQuickProfileLogic()
        {
            var quickProfileField = startRoot.Q<ObjectField>("QuickProfile");
            if (quickProfileField == null) return;

            quickProfileField.objectType = typeof(VegetationProfile);
            quickProfileField.allowSceneObjects = false;
            quickProfileField.SetValueWithoutNotify(currentProfile);

            quickProfileField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is VegetationProfile vp)
                {
                    currentProfile = vp;
                    ReloadAvailableProfiles();
                    RefreshAllUI();
                }
            });
        }

        private void SetupMappingGuardLogic()
        {
            _mappingGuard = startRoot.Q<VisualElement>(MappingGuardName);
            UpdateMappingGuardState();
        }

        #endregion

        #region Logic & Helpers (逻辑与辅助)

        /// <summary>
        /// 检查配置并更新 MappingGuard 的显示状态
        /// </summary>
        private void UpdateMappingGuardState()
        {
            if (_mappingGuard == null) return;

            // 使用 LINQ 简化查询：检查是否存在有效的 Plant 映射
            bool hasPlantMapping = config?.mappingEntries?
                .Any(e => e != null && e.type == Runtime.Profiles.PrefabType.Plant && e.node != null) ?? false;

            _mappingGuard.style.display = hasPlantMapping ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// 绑定按钮点击事件的辅助方法
        /// </summary>
        private void BindClick(string buttonName, Action action)
        {
            var btn = startRoot.Q<Button>(buttonName);
            if (btn != null) btn.clicked += action;
        }

        /// <summary>
        /// 庆祝映射完成（外部调用）
        /// </summary>
        public void CelebrateMappingCompleted()
        {
            // 1. 隐藏 Guard
            if (_mappingGuard != null) _mappingGuard.style.display = DisplayStyle.None;
            // 如果 _mappingGuard 还没缓存 (极端情况)，尝试重新获取
            else
            {
                UIElementExtensions.SetDisplay(startRoot?.Q<VisualElement>(MappingGuardName), false);

            }
            ;

            // 2. 播放 Logo 动画
            if (_logoElement == null) _logoElement = startRoot?.Q<Label>(LogoElementName);
            if (_logoElement == null) return;

            string originalText = _logoElement.text;

            // 设置完成状态
            _logoElement.text = "Complete";
            _logoElement.AddToClassList(AnimClassComplete);

            // 播放序列动画
            PlayPulseSequence(_logoElement, () =>
            {
                // 动画结束后的回调
                _logoElement.text = originalText;
                _logoElement.RemoveFromClassList(AnimClassComplete);
                _logoElement.RemoveFromClassList(AnimClassPulse3);
            }, startDelay: 60);
        }

        private bool CheckDoubleClick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastClickTime > DoubleClickInterval)
            {
                _clickCount = 0;
            }
            _lastClickTime = now;
            _clickCount++;
            return _clickCount >= 2;
        }

        private void PlayClickAnimation(VisualElement element)
        {
            PlayPulseSequence(element, null, 0);
        }

        /// <summary>
        /// 执行脉冲动画序列
        /// </summary>
        private void PlayPulseSequence(VisualElement element, Action onComplete, long startDelay = 0)
        {
            if (element == null) return;

            // 清理旧状态
            element.RemoveFromClassList(AnimClassPulse1);
            element.RemoveFromClassList(AnimClassPulse2);
            element.RemoveFromClassList(AnimClassPulse3);

            // 步骤 1
            element.schedule.Execute(() =>
            {
                element.AddToClassList(AnimClassPulse1);
            }).StartingIn(startDelay);

            // 步骤 2
            element.schedule.Execute(() =>
            {
                element.RemoveFromClassList(AnimClassPulse1);
                element.AddToClassList(AnimClassPulse2);
            }).StartingIn(startDelay + 80);

            // 步骤 3
            element.schedule.Execute(() =>
            {
                element.RemoveFromClassList(AnimClassPulse2);
                element.AddToClassList(AnimClassPulse3);
            }).StartingIn(startDelay + 160);

            // 结束 (如果有回调，或者仅移除最后一个状态)
            if (onComplete != null)
            {
                element.schedule.Execute(onComplete).StartingIn(startDelay + 260);
            }
            else
            {
                element.schedule.Execute(() =>
                {
                    element.RemoveFromClassList(AnimClassPulse3);
                }).StartingIn(startDelay + 240);
            }
        }

        #endregion
    }
}

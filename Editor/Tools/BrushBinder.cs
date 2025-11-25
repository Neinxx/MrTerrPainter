using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Tools
{
    public class BrushBinder
    {
        private Services.BrushSettings _brush;
        public void Bind(Services.BrushSettings brush,
            Slider size,
            Slider strength,
            Slider density,
            Slider hardness,
            EnumField distribution,
            Toggle mixExtra,
            Slider spacingFactor,
            Slider spacingAbs,
            Toggle useAbs)
        {
            _brush = brush;
            if (_brush == null) return;
            Bind(size, () => _brush.size, v => _brush.size = v);
            Bind(strength, () => _brush.strength, v => _brush.strength = v);
            Bind(density, () => _brush.densityScale, v => _brush.densityScale = v);
            Bind(hardness, () => _brush.hardness, v => _brush.hardness = v);
            if (distribution != null)
            {
                distribution.Init(_brush.distribution);
                distribution.SetValueWithoutNotify(_brush.distribution);
                distribution.RegisterValueChangedCallback(evt => _brush.distribution = (Services.DistributionType)evt.newValue);
            }
            Bind(mixExtra, () => _brush.mixExtraProfiles, v => _brush.mixExtraProfiles = v);
            Bind(spacingFactor, () => _brush.strokeSpacingFactor, v => _brush.strokeSpacingFactor = v);
            Bind(spacingAbs, () => _brush.strokeSpacingAbsolute, v => _brush.strokeSpacingAbsolute = v);
            if (useAbs != null)
            {
                useAbs.SetValueWithoutNotify(_brush.useAbsoluteStrokeSpacing);
                spacingAbs?.SetEnabled(_brush.useAbsoluteStrokeSpacing);
                useAbs.RegisterValueChangedCallback(evt => { _brush.useAbsoluteStrokeSpacing = evt.newValue; spacingAbs?.SetEnabled(evt.newValue); });
            }
        }
        private void Bind(Slider s, Func<float> get, Action<float> set)
        {
            if (s == null) return;
            s.SetValueWithoutNotify(get());
            s.RegisterValueChangedCallback(evt => set(evt.newValue));
        }
        private void Bind(Toggle t, Func<bool> get, Action<bool> set)
        {
            if (t == null) return;
            t.SetValueWithoutNotify(get());
            t.RegisterValueChangedCallback(evt => set(evt.newValue));
        }
    }
}

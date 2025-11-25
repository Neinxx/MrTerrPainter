using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace MrTerrainPainter.Editor.Utils
{
    public class SubscriptionGuard
    {
        public static void ResetClick(Button btn, Action handler)
        {
            if (btn == null) return;
            if (btn.userData is Action old) btn.clicked -= old;
            btn.userData = (Action)handler;
            btn.clicked += (Action)handler;
        }
        public static void ResetToggle(Toggle t, EventCallback<ChangeEvent<bool>> cb)
        {
            if (t == null) return;
            var old = t.userData as EventCallback<ChangeEvent<bool>>;
            if (old != null) t.UnregisterCallback(old);
            t.userData = cb;
            t.RegisterCallback(cb);
        }
        public static void ResetObjectField(ObjectField f, EventCallback<ChangeEvent<UnityEngine.Object>> cb)
        {
            if (f == null) return;
            var old = f.userData as EventCallback<ChangeEvent<UnityEngine.Object>>;
            if (old != null) f.UnregisterCallback(old);
            f.userData = cb;
            f.RegisterCallback(cb);
        }
    }
}

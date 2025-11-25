using System;
using System.Collections.Generic;
using UnityEditor;

namespace MrTerrainPainter.Editor.Utils
{
    public class UIUpdateBatch
    {
        private readonly Queue<Action> _q = new Queue<Action>();
        private bool _queued;
        public void Enqueue(Action a)
        {
            if (a == null) return;
            _q.Enqueue(a);
            if (_queued) return;
            _queued = true;
            EditorApplication.delayCall += Flush;
        }
        private void Flush()
        {
            _queued = false;
            while (_q.Count > 0)
            {
                var a = _q.Dequeue();
                a?.Invoke();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    public static class AssetPreviewCache
    {
        private class Entry
        {
            public UnityEngine.Object obj;
            public Texture2D tex;
            public bool ready;
            public List<Action<Texture2D>> callbacks = new List<Action<Texture2D>>(2);
        }

        private static readonly Dictionary<int, Entry> s_entries = new Dictionary<int, Entry>();
        private static readonly LinkedList<int> s_lru = new LinkedList<int>();
        private const int Capacity = 128;
        private static bool s_listening;

        public static Texture2D GetCached(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            int id = obj.GetInstanceID();
            if (s_entries.TryGetValue(id, out var e) && e.ready) return e.tex;
            return null;
        }

        public static void Request(UnityEngine.Object obj, Action<Texture2D> onReady)
        {
            if (obj == null)
            {
                onReady?.Invoke(null);
                return;
            }
            int id = obj.GetInstanceID();
            if (s_entries.TryGetValue(id, out var existing))
            {
                if (existing.ready)
                {
                    TouchLRU(id);
                    onReady?.Invoke(existing.tex);
                    return;
                }
                existing.callbacks.Add(onReady);
                EnsureUpdate();
                return;
            }
            var e = new Entry { obj = obj, ready = false };
            e.callbacks.Add(onReady);
            s_entries[id] = e;
            EnsureUpdate();
        }

        private static void EnsureUpdate()
        {
            if (s_listening) return;
            s_listening = true;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            int processed = 0;
            const int BudgetPerFrame = 8;
            var ids = new List<int>(s_entries.Keys);
            for (int i = 0; i < ids.Count && processed < BudgetPerFrame; i++)
            {
                var id = ids[i];
                var e = s_entries[id];
                if (e.ready) continue;
                var tex = AssetPreview.GetAssetPreview(e.obj) ?? AssetPreview.GetMiniThumbnail(e.obj);
                if (tex == null) continue;
                e.tex = tex;
                e.ready = true;
                TouchLRU(id);
                var cbs = e.callbacks;
                e.callbacks = new List<Action<Texture2D>>(0);
                processed++;
                try
                {
                    for (int k = 0; k < cbs.Count; k++) cbs[k]?.Invoke(tex);
                }
                catch { }
            }

            // 清理超容量
            while (s_lru.Count > Capacity)
            {
                int tail = s_lru.Last.Value;
                s_lru.RemoveLast();
                s_entries.Remove(tail);
            }

            // 若无未完成项则停止监听
            bool anyPending = false;
            foreach (var kv in s_entries)
            {
                if (!kv.Value.ready)
                {
                    anyPending = true;
                    break;
                }
            }
            if (!anyPending)
            {
                EditorApplication.update -= OnEditorUpdate;
                s_listening = false;
            }
        }

        private static void TouchLRU(int id)
        {
            var node = s_lru.Find(id);
            if (node != null) { s_lru.Remove(node); s_lru.AddFirst(node); }
            else s_lru.AddFirst(id);
        }
    }
}

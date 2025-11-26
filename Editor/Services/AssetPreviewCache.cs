using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MrTerrainPainter.Editor.Services
{
    /// <summary>
    /// 资产预览图缓存服务 (优化版)
    /// </summary>
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
        private const int BudgetPerFrame = 10;
        private static bool s_listening;

        /// <summary>
        /// 同步获取缓存 (如果已就绪)
        /// </summary>
        public static Texture2D GetCached(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            int id = obj.GetInstanceID();

            if (s_entries.TryGetValue(id, out var e) && e.ready)
            {
                TouchLRU(id);
                return e.tex;
            }
            return null;
        }

        /// <summary>
        /// 请求获取预览图 (支持异步回调)
        /// </summary>
        public static void Request(UnityEngine.Object obj, Action<Texture2D> onReady)
        {
            if (obj == null)
            {
                onReady?.Invoke(null);
                return;
            }

            int id = obj.GetInstanceID();

            // 1. 命中现有记录
            if (s_entries.TryGetValue(id, out var existing))
            {
                HandleExistingEntry(id, existing, onReady);
                return;
            }

            // 2. 新建记录
            CreateNewEntry(id, obj, onReady);
        }

        #region 1. Request Helpers

        private static void HandleExistingEntry(int id, Entry e, Action<Texture2D> onReady)
        {
            // 情况 A: 已就绪，直接回调
            if (e.ready)
            {
                TouchLRU(id);
                onReady?.Invoke(e.tex);
                return;
            }

            // 情况 B: 加载中，追加回调 (去重)
            if (onReady != null && !e.callbacks.Contains(onReady))
            {
                e.callbacks.Add(onReady);
            }

            EnsureUpdateLoop();
        }

        private static void CreateNewEntry(int id, UnityEngine.Object obj, Action<Texture2D> onReady)
        {
            var e = new Entry { obj = obj, ready = false };
            if (onReady != null) e.callbacks.Add(onReady);

            s_entries[id] = e;
            TouchLRU(id); // 新项加入 LRU
            EnsureUpdateLoop();
        }

        #endregion

        #region 2. Update Loop Orchestrator

        private static void EnsureUpdateLoop()
        {
            if (s_listening) return;
            s_listening = true;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            // 1. 获取本帧任务
            var pendingIds = GetPendingIds();
            if (pendingIds.Count == 0)
            {
                StopUpdateLoop();
                return;
            }

            // 2. 批量处理 (受 Budget 限制)
            int processedCount = ProcessBatch(pendingIds);

            // 3. 内存清理
            if (processedCount > 0)
            {
                PruneCacheCapacity();
            }

            // 4. 检查是否需要继续监听
            CheckLoopTermination();
        }

        private static void StopUpdateLoop()
        {
            if (!s_listening) return;
            EditorApplication.update -= OnEditorUpdate;
            s_listening = false;
        }

        #endregion

        #region 3. Processing Logic

        private static List<int> GetPendingIds()
        {
            // 收集 ID 以避免遍历时修改集合
            // 这里可以优化为维护一个专门的 pending 列表，但当前规模下遍历字典开销可接受
            return s_entries.Where(kv => !kv.Value.ready).Select(kv => kv.Key).ToList();
        }

        private static int ProcessBatch(List<int> pendingIds)
        {
            int processed = 0;

            foreach (var id in pendingIds)
            {
                if (processed >= BudgetPerFrame) break;
                if (!s_entries.TryGetValue(id, out var entry)) continue;

                // 尝试解析预览图状态
                if (TryResolvePreview(entry.obj, out Texture2D resultTex))
                {
                    FinalizeEntry(id, entry, resultTex);
                    processed++;
                }
            }

            return processed;
        }

        /// <summary>
        /// 核心逻辑：判断资源是 就绪 / 加载中 / 无预览图
        /// </summary>
        /// <returns>True 表示处理完成 (无论成功失败), False 表示需要等待</returns>
        private static bool TryResolvePreview(UnityEngine.Object obj, out Texture2D result)
        {
            result = null;

            // A. 尝试获取高清图
            Texture2D preview = AssetPreview.GetAssetPreview(obj);
            if (preview != null)
            {
                result = preview;
                return true; // 成功
            }

            // B. 检查加载状态
            if (AssetPreview.IsLoadingAssetPreview(obj.GetInstanceID()))
            {
                return false; // 正在加载，跳过，下帧再试
            }

            // C. 加载结束但无图 (如脚本、纯数据资源) -> 回退到图标
            result = AssetPreview.GetMiniThumbnail(obj);
            return true; // 已完成 (回退)
        }

        private static void FinalizeEntry(int id, Entry e, Texture2D tex)
        {
            e.tex = tex;
            e.ready = true;
            TouchLRU(id);

            // 触发并清空回调
            NotifySubscribers(e.callbacks, tex);
            e.callbacks.Clear(); // 断开引用，释放内存
        }

        private static void NotifySubscribers(List<Action<Texture2D>> callbacks, Texture2D tex)
        {
            if (callbacks == null || callbacks.Count == 0) return;

            for (int i = 0; i < callbacks.Count; i++)
            {
                try
                {
                    callbacks[i]?.Invoke(tex);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        #endregion

        #region 4. Cache Management

        private static void PruneCacheCapacity()
        {
            while (s_lru.Count > Capacity)
            {
                int tailId = s_lru.Last.Value;
                s_lru.RemoveLast();
                s_entries.Remove(tailId);
            }
        }

        private static void CheckLoopTermination()
        {
            // 如果没有未就绪的项，停止 Update
            bool anyPending = s_entries.Values.Any(e => !e.ready);
            if (!anyPending)
            {
                StopUpdateLoop();
            }
        }

        private static void TouchLRU(int id)
        {
            var node = s_lru.Find(id);
            if (node != null)
            {
                s_lru.Remove(node);
                s_lru.AddFirst(node);
            }
            else
            {
                s_lru.AddFirst(id);
            }
        }

        #endregion
    }
}
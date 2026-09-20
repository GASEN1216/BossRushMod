using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>加载记录只在操作边界写入；固定容量，不在 Update 中分配或写日志。</summary>
    internal static class ResourceBundleLoader
    {
        [Serializable]
        internal sealed class LoadRecord
        {
            public string path, phase, status;
            public double startedSeconds, endedSeconds;
            public bool cancelled, failed;
        }
        private const int HistoryCapacity = 512;
        private static readonly Queue<LoadRecord> history = new Queue<LoadRecord>();
        private static readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<AssetBundle, UnityEngine.Object[]> preparedAssets = new Dictionary<AssetBundle, UnityEngine.Object[]>();
        private static int generation;

        private sealed class Pending
        {
            internal AssetBundleCreateRequest Request;
            internal AssetBundle Bundle;
            internal AssetBundleRequest Assets;
            internal LoadRecord AssetsRecord;
            internal bool Abandoned, Taken, Finished;
            internal LoadRecord Record;
            internal bool Drained { get { return Finished && (Assets == null || Assets.isDone); } }
            private void ReleaseOwned()
            {
                if (Bundle != null && !Taken) Bundle.Unload(true);
                Bundle = null;
                Pending current;
                if (pending.TryGetValue(Record.path, out current) && ReferenceEquals(current, this)) pending.Remove(Record.path);
            }
            internal void Complete(AsyncOperation unused)
            {
                if (Finished) return;
                Finished = true;
                Bundle = Request.assetBundle;
                Record.endedSeconds = Time.realtimeSinceStartupAsDouble;
                if (Abandoned)
                {
                    ReleaseOwned();
                }
                else { Record.failed = Bundle == null; Record.status = Bundle == null ? "failed" : "complete"; }
            }
            internal void Cancel(string reason)
            {
                if (Taken || Abandoned || (Finished && Bundle == null)) return;
                Abandoned = true;
                Record.cancelled = true;
                Record.failed = reason == "timeout";
                Record.status = reason;
                if (Assets != null && !Assets.isDone)
                {
                    AssetsRecord.cancelled = true; AssetsRecord.failed = reason == "timeout";
                    AssetsRecord.status = reason;
                    Assets.completed += unused => { AssetsRecord.endedSeconds = Time.realtimeSinceStartupAsDouble; ReleaseOwned(); };
                }
                else if (Finished) ReleaseOwned();
                // Unity does not cancel native requests. Complete remains attached and unloads a late result.
            }
        }

        private static LoadRecord Begin(string path, string phase)
        {
            if (history.Count == HistoryCapacity) history.Dequeue();
            var record = new LoadRecord { path = path, phase = phase, status = "pending", startedSeconds = Time.realtimeSinceStartupAsDouble };
            history.Enqueue(record);
            return record;
        }

        internal static LoadRecord[] Snapshot() { return history.ToArray(); }

        /// <summary>同步官方 GetPrefab 契约保留兜底；正常 bootstrap 已经异步准备好。</summary>
        internal static AssetBundle LoadFromFile(string path)
        {
            Pending operation;
            if (pending.TryGetValue(path, out operation) && operation.Abandoned && !operation.Drained)
            {
                var draining = Begin(path, "sync_compatibility");
                draining.failed = true; draining.cancelled = true; draining.status = "cancel_draining";
                draining.endedSeconds = Time.realtimeSinceStartupAsDouble;
                return null;
            }
            if (pending.TryGetValue(path, out operation) && !operation.Abandoned)
            {
                operation.Taken = true;
                // 仅官方同步查询抢在预载结束之前触发；Unity 会完成同一个请求，不发第二次加载。
                AssetBundle result = operation.Request.assetBundle;
                operation.Complete(null);
                pending.Remove(path);
                return result;
            }
            var record = Begin(path, "sync_compatibility");
            try
            {
                AssetBundle result = AssetBundle.LoadFromFile(path);
                record.failed = result == null;
                record.status = result == null ? "failed" : "complete";
                return result;
            }
            catch { record.failed = true; record.status = "exception"; throw; }
            finally { record.endedSeconds = Time.realtimeSinceStartupAsDouble; }
        }

        internal static T[] LoadAllAssets<T>(AssetBundle bundle) where T : UnityEngine.Object
        {
            UnityEngine.Object[] assets;
            if ((typeof(T) == typeof(GameObject) || typeof(T) == typeof(UnityEngine.Object)) && preparedAssets.TryGetValue(bundle, out assets))
            {
                preparedAssets.Remove(bundle);
                var typed = new T[assets.Length];
                for (int i = 0; i < assets.Length; i++) typed[i] = (T)assets[i];
                return typed;
            }
            var record = Begin(bundle.name, "assets_sync_compatibility");
            try { var result = bundle.LoadAllAssets<T>(); record.status = "complete"; return result; }
            catch { record.failed = true; record.status = "exception"; throw; }
            finally { record.endedSeconds = Time.realtimeSinceStartupAsDouble; }
        }

        /// <summary>持有资源直到 consumer 转交给现有 owner；任何退出都回收未转交的租约。</summary>
        internal static IEnumerator Prepare(string path, bool prefabs, Func<bool> cancelled, Action consumer, float timeoutSeconds = 60f)
        {
            int epoch = generation;
            if (cancelled()) yield break;
            if (!System.IO.File.Exists(path))
            {
                var missing = Begin(path, "bundle_async");
                missing.failed = true; missing.status = "missing"; missing.endedSeconds = Time.realtimeSinceStartupAsDouble;
                consumer();
                yield break;
            }
            double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            Pending existing;
            if (pending.TryGetValue(path, out existing))
            {
                // Abandoned native work must drain before re-opening the same bundle.
                while (!existing.Drained && !cancelled() && epoch == generation && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (cancelled() || epoch != generation) yield break;
                if (!existing.Drained)
                {
                    var waiting = Begin(path, "wait_existing");
                    waiting.failed = true; waiting.status = "timeout"; waiting.endedSeconds = Time.realtimeSinceStartupAsDouble;
                    yield break;
                }
                if (pending.TryGetValue(path, out existing) && !existing.Abandoned) { consumer(); yield break; }
                pending.Remove(path);
            }
            var operation = new Pending { Record = Begin(path, "bundle_async") };
            try { operation.Request = AssetBundle.LoadFromFileAsync(path); }
            catch (Exception e)
            {
                operation.Record.failed = true; operation.Record.status = "exception";
                operation.Record.endedSeconds = Time.realtimeSinceStartupAsDouble;
                Debug.LogWarning("[BossRushResources] " + path + ": " + e.Message);
            }
            if (operation.Request == null)
            {
                operation.Record.failed = true; operation.Record.status = "failed";
                operation.Record.endedSeconds = Time.realtimeSinceStartupAsDouble;
                consumer(); yield break;
            }
            pending[path] = operation;
            operation.Request.completed += operation.Complete;
            AssetBundleRequest assets = null;
            try
            {
                while (!operation.Request.isDone)
                {
                    if (cancelled() || epoch != generation || Time.realtimeSinceStartupAsDouble >= deadline)
                    { operation.Cancel(Time.realtimeSinceStartupAsDouble >= deadline ? "timeout" : "cancelled"); yield break; }
                    yield return null;
                }
                operation.Complete(null);
                if (operation.Taken) yield break;
                if (cancelled() || epoch != generation) { operation.Cancel("cancelled"); yield break; }
                if (operation.Bundle == null)
                {
                    pending.Remove(path);
                    consumer(); yield break;
                }
                if (prefabs)
                {
                    var record = operation.AssetsRecord = Begin(path, "assets_async");
                    try { assets = operation.Assets = operation.Bundle.LoadAllAssetsAsync<GameObject>(); }
                    catch (Exception e)
                    {
                        record.failed = true; record.status = "exception";
                        record.endedSeconds = Time.realtimeSinceStartupAsDouble;
                        Debug.LogWarning("[BossRushResources] " + path + ": " + e.Message);
                    }
                    if (assets == null)
                    {
                        record.failed = true; record.status = "failed"; record.endedSeconds = Time.realtimeSinceStartupAsDouble;
                        consumer(); yield break;
                    }
                    while (!assets.isDone)
                    {
                        if (cancelled() || epoch != generation || Time.realtimeSinceStartupAsDouble >= deadline)
                        {
                            operation.Cancel(Time.realtimeSinceStartupAsDouble >= deadline ? "timeout" : "cancelled");
                            yield break;
                        }
                        yield return null;
                    }
                    record.endedSeconds = Time.realtimeSinceStartupAsDouble; record.status = "complete";
                    if (operation.Taken) yield break;
                    if (cancelled() || epoch != generation) { operation.Cancel("cancelled"); yield break; }
                    preparedAssets[operation.Bundle] = assets.allAssets;
                }
                consumer();
            }
            finally
            {
                // Remove even a destroyed Unity wrapper, before cancellation clears its owned reference.
                if (!ReferenceEquals(operation.Bundle, null)) preparedAssets.Remove(operation.Bundle);
                if (!operation.Taken) operation.Cancel("cancelled");
                Pending current;
                if (operation.Finished && (assets == null || assets.isDone) && pending.TryGetValue(path, out current) && ReferenceEquals(current, operation)) pending.Remove(path);
            }
        }

        internal static void ResetStaticCaches()
        {
            generation++;
            var operations = new List<Pending>(pending.Values);
            foreach (var operation in operations) operation.Cancel("host_destroyed");
            preparedAssets.Clear();
            // In-flight callbacks still own native cleanup even after the Mod host is gone.
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>局内协程的登记与执行 owner。完成、异常、取消均释放记录；显式驱动嵌套 iterator 的完整生命周期。</summary>
    internal sealed class ZombieModeRunCoroutine : IEnumerator, IDisposable
    {
        private readonly ModBehaviour owner;
        private readonly ZombieModeRunState run;
        private readonly ZombieModeRunOnlyRecord record;
        private readonly Func<bool> valid;
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private Coroutine coroutine;
        private object current;
        private bool moving, disposed, disposeRequested, cancelRequested;

        private ZombieModeRunCoroutine(ModBehaviour owner, ZombieModeRunState run, int runId,
            IEnumerator routine, Func<bool> valid)
        {
            this.owner = owner;
            this.run = run;
            this.valid = valid;
            stack.Push(routine);
            record = new ZombieModeRunOnlyRecord { RunId = runId, Kind = ZombieModeRunOnlyObjectKind.Coroutine };
            record.CleanupAction = Cancel;
        }

        internal static Coroutine Start(ModBehaviour owner, ZombieModeRunState run, int runId,
            IEnumerator routine, Func<bool> valid)
        {
            var task = new ZombieModeRunCoroutine(owner, run, runId, routine, valid);
            // Unity 的 StartCoroutine 会同步推进到第一个 yield；必须先登记，以覆盖同步完成和重入退局。
            run.RunOnlyObjects.Add(task.record);
            try
            {
                task.coroutine = owner.StartCoroutine(task);
                if (task.coroutine == null) task.Dispose();
                else if (task.cancelRequested) task.StopNative();
                return task.coroutine;
            }
            catch (Exception e)
            {
                task.Dispose();
                ModBehaviour.DevLog("[ZombieMode] 协程启动失败: " + e.Message);
                return null;
            }
        }

        public object Current { get { return current; } }
        public void Reset() { throw new NotSupportedException(); }

        public bool MoveNext()
        {
            if (disposed) return false;
            bool yielded = false;
            moving = true;
            try
            {
                while (!disposeRequested && valid() && stack.Count > 0)
                {
                    IEnumerator next = stack.Peek();
                    if (!next.MoveNext())
                    {
                        stack.Pop();
                        DisposeIterator(next);
                        continue;
                    }
                    object value = next.Current;
                    IEnumerator child = value as IEnumerator;
                    if (child != null) stack.Push(child);
                    else
                    {
                        // WaitForSeconds / AsyncOperation / Coroutine 等交回 Unity，不丢弃 Current。
                        current = value;
                        yielded = true;
                        break;
                    }
                }
            }
            finally
            {
                moving = false;
                if (!yielded || disposeRequested) Dispose();
            }
            return yielded && !disposed;
        }

        private void Cancel()
        {
            cancelRequested = true;
            record.CleanupAction = null;
            try { StopNative(); }
            finally { Dispose(); }
        }

        private void StopNative()
        {
            if (coroutine == null) return;
            try { owner.StopCoroutine(coroutine); }
            catch (Exception e) { ModBehaviour.DevLog("[ZombieMode] StopCoroutine 失败: " + e.Message); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposeRequested = true;
            // 自己的 MoveNext 里触发退局时，等该次调用退出后再释放 iterator，避免重入 Dispose。
            if (moving) return;
            disposed = true;
            current = null;
            record.CleanupAction = null;
            // 退局正在反向遍历清理表时不修改表长；该路径紧接着由唯一 owner ClearRuntime。
            if (!run.IsCleaningUp) run.RunOnlyObjects.Remove(record);
            while (stack.Count > 0) DisposeIterator(stack.Pop());
        }

        private static void DisposeIterator(IEnumerator iterator)
        {
            try
            {
                IDisposable disposable = iterator as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
            catch (Exception e) { ModBehaviour.DevLog("[ZombieMode] 协程释放失败: " + e.Message); }
        }
    }
}

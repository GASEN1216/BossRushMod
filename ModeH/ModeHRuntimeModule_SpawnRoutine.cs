// Mode H 初始生成与接力协程的异常边界；同场重试仍由 MatchFlow 收口。
using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        /// <summary>
        /// 分帧生成本场敌军与我方选手，提交后交给 CombatControl。
        /// 任何一步失败都走「技术中止 + 同场重开」，**绝不判负**（§17.4）。
        /// </summary>
        private System.Collections.IEnumerator DriveMatchSpawning()
        {
            return DriveSpawnRoutine(DriveCompleteMatchSpawning(), "initial");
        }

        /// <summary>Unity 协程异常不会进入 OnUpdate 的异常边界；初始生成与接力共用技术重试出口。</summary>
        private System.Collections.IEnumerator DriveSpawnRoutine(System.Collections.IEnumerator routine, string phase)
        {
            if (routine == null || _runState == null) yield break;
            long ownerToken = _runState.OwnerToken;
            int generation = _sceneGeneration;
            int stateSequence = _runState.StateSequence;
            Stack<System.Collections.IEnumerator> stack = new Stack<System.Collections.IEnumerator>();
            stack.Push(routine);
            Exception failure = null;
            try
            {
                while (stack.Count > 0)
                {
                    if (!IsCallbackStillValid(ownerToken, generation)
                        || _runState.StateSequence != stateSequence) yield break;
                    bool moved = false;
                    object current = null;
                    try
                    {
                        moved = stack.Peek().MoveNext();
                        if (moved) current = stack.Peek().Current;
                    }
                    catch (Exception e) { failure = e; }
                    if (failure != null) break;
                    if (!moved)
                    {
                        IDisposable completed = stack.Pop() as IDisposable;
                        try { if (completed != null) completed.Dispose(); }
                        catch (Exception e) { failure = e; }
                        if (failure != null) break;
                        continue;
                    }
                    System.Collections.IEnumerator child = current as System.Collections.IEnumerator;
                    if (child != null) { stack.Push(child); continue; }
                    yield return current;
                }
            }
            finally
            {
                while (stack.Count > 0)
                {
                    try
                    {
                        IDisposable disposable = stack.Pop() as IDisposable;
                        if (disposable != null) disposable.Dispose();
                    }
                    catch (Exception e) { LogFailure("spawn_coroutine_dispose", e); }
                }
            }
            if (failure == null || !IsCallbackStillValid(ownerToken, generation)
                || _runState.StateSequence != stateSequence) yield break;
            LogFailure("spawn_coroutine_" + phase, failure);
            // 当前协程正在收尾，先丢掉自身句柄，避免技术重试清理反过来 StopCoroutine 自己。
            if (_runState.Lifecycle == ModeHLifecycle.RelayPending) _relaySpawnRoutine = null;
            else _spawnRoutine = null;
            AbortMatchSpawning("spawn_coroutine_" + phase + ":" + failure.GetType().Name);
        }

    }
}
